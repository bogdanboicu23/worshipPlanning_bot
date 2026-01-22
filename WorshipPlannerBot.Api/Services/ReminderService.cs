using System.Text;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using WorshipPlannerBot.Api.Data;
using WorshipPlannerBot.Api.Models;

namespace WorshipPlannerBot.Api.Services;

public class ReminderService : IReminderService
{
    private readonly IBotService _botService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILocalizationService _localization;
    private readonly ILogger<ReminderService> _logger;

    public ReminderService(
        IBotService botService,
        IServiceProvider serviceProvider,
        ILocalizationService localization,
        ILogger<ReminderService> logger)
    {
        _botService = botService;
        _serviceProvider = serviceProvider;
        _localization = localization;
        _logger = logger;
    }

    public async Task SendEventReminder(int eventId, string reminderType = "general")
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var evt = await dbContext.Events
            .Include(e => e.Attendances)
                .ThenInclude(a => a.User)
                    .ThenInclude(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null || evt.IsCancelled)
        {
            _logger.LogWarning($"Event {eventId} not found or cancelled");
            return;
        }

        var confirmedAttendees = evt.Attendances
            .Where(a => a.Status == AttendanceStatus.Yes)
            .Select(a => a.User)
            .ToList();

        if (!confirmedAttendees.Any())
        {
            _logger.LogInformation($"No confirmed attendees for event {eventId}");
            return;
        }

        foreach (var attendee in confirmedAttendees)
        {
            try
            {
                var reminderMessage = BuildReminderMessage(evt, reminderType, attendee.LanguageCode);
                await _botService.Client.SendMessage(
                    attendee.TelegramId,
                    reminderMessage,
                    parseMode: ParseMode.Markdown);

                _logger.LogInformation($"Reminder sent to {attendee.FullName} for event {evt.Title}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send reminder to user {attendee.Id}");
            }
        }
    }

    public async Task SendRoleBasedReminder(int eventId, int roleId)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var evt = await dbContext.Events
            .Include(e => e.Attendances)
                .ThenInclude(a => a.User)
                    .ThenInclude(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null || evt.IsCancelled)
            return;

        var role = await dbContext.Roles.FindAsync(roleId);
        if (role == null)
            return;

        var roleAttendees = evt.Attendances
            .Where(a => a.Status == AttendanceStatus.Yes
                && a.User.UserRoles.Any(ur => ur.RoleId == roleId))
            .Select(a => a.User)
            .ToList();

        if (!roleAttendees.Any())
        {
            _logger.LogInformation($"No {role.Name} confirmed for event {eventId}");
            return;
        }

        foreach (var attendee in roleAttendees)
        {
            try
            {
                var localizedRoleName = _localization.GetString($"Role.{role.Name.Replace(" ", "")}", attendee.LanguageCode);
                if (localizedRoleName == $"Role.{role.Name.Replace(" ", "")}")
                    localizedRoleName = role.Name;

                var reminderMessage = $"🔔 *{_localization.GetString("EventReminder", attendee.LanguageCode)} - {localizedRoleName}*\n\n" +
                                    $"{_localization.GetString("EventTitle", attendee.LanguageCode)}: {evt.Title}\n" +
                                    $"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy}\n" +
                                    $"🕐 {evt.DateTime.ToLocalTime():HH:mm}\n" +
                                    $"📍 {evt.Location}";

                await _botService.Client.SendMessage(
                    attendee.TelegramId,
                    reminderMessage,
                    parseMode: ParseMode.Markdown);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send role reminder to user {attendee.Id}");
            }
        }
    }

    public async Task SendCustomReminder(int eventId, string customMessage)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var evt = await dbContext.Events
            .Include(e => e.Attendances)
                .ThenInclude(a => a.User)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null || evt.IsCancelled)
            return;

        var confirmedAttendees = evt.Attendances
            .Where(a => a.Status == AttendanceStatus.Yes)
            .Select(a => a.User)
            .ToList();

        foreach (var attendee in confirmedAttendees)
        {
            try
            {
                var message = $"📢 *{_localization.GetString("MessageFromAdmin", attendee.LanguageCode)}*\n\n" +
                             $"{_localization.GetString("EventTitle", attendee.LanguageCode)}: {evt.Title}\n" +
                             $"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM}\n\n" +
                             $"{customMessage}";

                await _botService.Client.SendMessage(
                    attendee.TelegramId,
                    message,
                    parseMode: ParseMode.Markdown);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send custom reminder to user {attendee.Id}");
            }
        }
    }

    public async Task<List<Event>> GetUpcomingEventsForReminders(int hoursAhead)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var targetTime = DateTime.UtcNow.AddHours(hoursAhead);
        var windowStart = targetTime.AddMinutes(-30);
        var windowEnd = targetTime.AddMinutes(30);

        return await dbContext.Events
            .Where(e => !e.IsCancelled
                && e.DateTime >= windowStart
                && e.DateTime <= windowEnd)
            .Include(e => e.Attendances)
            .ToListAsync();
    }

    private string BuildReminderMessage(Event evt, string reminderType, string languageCode)
    {
        var timeUntilEvent = evt.DateTime - DateTime.UtcNow;
        var timeString = FormatTimeUntil(timeUntilEvent, languageCode);

        var sb = new StringBuilder();

        switch (reminderType.ToLower())
        {
            case "day-before":
                sb.AppendLine("📅 *Reminder: Tomorrow's Service*\n");
                break;
            case "morning-of":
                sb.AppendLine("☀️ *Today's Service Reminder*\n");
                break;
            case "1-hour":
                sb.AppendLine("⏰ *Starting in 1 Hour!*\n");
                break;
            default:
                sb.AppendLine($"🔔 *Event Reminder*\n");
                break;
        }

        sb.AppendLine($"*{evt.Title}*");
        sb.AppendLine($"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy}");
        sb.AppendLine($"🕐 {evt.DateTime.ToLocalTime():HH:mm}");
        sb.AppendLine($"📍 {evt.Location}");

        if (!string.IsNullOrEmpty(evt.Description))
            sb.AppendLine($"\n📝 {evt.Description}");

        if (!string.IsNullOrEmpty(timeString))
            sb.AppendLine($"\n⏳ {timeString}");

        sb.AppendLine("\nSee you there! 🙏");

        return sb.ToString();
    }

    private string FormatTimeUntil(TimeSpan timeSpan, string languageCode)
    {
        if (timeSpan.TotalMinutes < 0)
            return "Event has started";

        if (timeSpan.TotalHours < 1)
            return $"Starting in {(int)timeSpan.TotalMinutes} minutes";

        if (timeSpan.TotalHours < 24)
            return $"Starting in {(int)timeSpan.TotalHours} hours";

        if (timeSpan.TotalDays < 2)
            return "Tomorrow";

        return $"In {(int)timeSpan.TotalDays} days";
    }
}

public interface IReminderService
{
    Task SendEventReminder(int eventId, string reminderType = "general");
    Task SendRoleBasedReminder(int eventId, int roleId);
    Task SendCustomReminder(int eventId, string customMessage);
    Task<List<Event>> GetUpcomingEventsForReminders(int hoursAhead);
}