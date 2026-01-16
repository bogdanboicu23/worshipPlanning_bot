using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WorshipPlannerBot.Api.Data;
using WorshipPlannerBot.Api.Models;
using WorshipPlannerBot.Api.Services;

namespace WorshipPlannerBot.Api.Handlers;

public class EventHandler
{
    private readonly IBotService _botService;
    private readonly BotDbContext _dbContext;
    private readonly ILogger<EventHandler> _logger;

    public EventHandler(IBotService botService, BotDbContext dbContext, ILogger<EventHandler> logger)
    {
        _botService = botService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task StartEventCreationAsync(Message message, Models.User user)
    {
        var text = "📅 *Create New Worship Service*\n\n" +
                  "Please provide event details in the following format:\n\n" +
                  "Title\n" +
                  "Date and Time (DD/MM/YYYY HH:MM)\n" +
                  "Location\n" +
                  "Description (optional)\n\n" +
                  "Example:\n" +
                  "Sunday Worship Service\n" +
                  "25/01/2025 10:30\n" +
                  "Main Hall\n" +
                  "Regular Sunday morning worship";

        await _botService.Client.SendMessage(
            message.Chat.Id,
            text,
            parseMode: ParseMode.Markdown);
    }

    public async Task ShowUpcomingEventsAsync(Message message, Models.User user)
    {
        var upcomingEvents = await _dbContext.Events
            .Where(e => e.DateTime >= DateTime.UtcNow && !e.IsCancelled)
            .OrderBy(e => e.DateTime)
            .Take(10)
            .Include(e => e.Attendances)
            .ThenInclude(a => a.User)
            .ThenInclude(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .ToListAsync();

        if (!upcomingEvents.Any())
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "📭 No upcoming events scheduled.");
            return;
        }

        foreach (var evt in upcomingEvents)
        {
            var messageText = FormatEventMessage(evt);
            var keyboard = CreateAttendanceKeyboard(evt.Id, user.Id);

            await _botService.Client.SendMessage(
                message.Chat.Id,
                messageText,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
    }

    public async Task CreateEventAsync(Message message, Models.User user, string[] lines)
    {
        try
        {
            if (lines.Length < 3)
            {
                await _botService.Client.SendMessage(
                    message.Chat.Id,
                    "❌ Invalid format. Please provide at least Title, Date/Time, and Location.");
                return;
            }

            var title = lines[0].Trim();
            var dateTimeStr = lines[1].Trim();
            var location = lines[2].Trim();
            var description = lines.Length > 3 ? lines[3].Trim() : null;

            if (!DateTime.TryParseExact(dateTimeStr, "dd/MM/yyyy HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var eventDateTime))
            {
                await _botService.Client.SendMessage(
                    message.Chat.Id,
                    "❌ Invalid date format. Please use DD/MM/YYYY HH:MM");
                return;
            }

            var newEvent = new Event
            {
                Title = title,
                DateTime = eventDateTime.ToUniversalTime(),
                Location = location,
                Description = description,
                CreatedByUserId = user.Id,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Events.Add(newEvent);
            await _dbContext.SaveChangesAsync();

            var confirmationText = $"✅ Event created successfully!\n\n{FormatEventMessage(newEvent)}";
            var keyboard = CreateAttendanceKeyboard(newEvent.Id, user.Id);

            await _botService.Client.SendMessage(
                message.Chat.Id,
                confirmationText,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event");
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "❌ An error occurred while creating the event. Please try again.");
        }
    }

    private string FormatEventMessage(Event evt)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🎵 *{evt.Title}*");
        sb.AppendLine($"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy}");
        sb.AppendLine($"🕐 {evt.DateTime.ToLocalTime():HH:mm}");
        sb.AppendLine($"📍 {evt.Location}");

        if (!string.IsNullOrEmpty(evt.Description))
            sb.AppendLine($"📝 {evt.Description}");

        sb.AppendLine("\n*Attendance:*");

        var attendanceByRole = evt.Attendances
            .Where(a => a.Status == AttendanceStatus.Yes)
            .SelectMany(a => a.User.UserRoles.Select(ur => new { Role = ur.Role, User = a.User }))
            .GroupBy(x => x.Role)
            .OrderBy(g => g.Key.DisplayOrder);

        if (!attendanceByRole.Any())
        {
            sb.AppendLine("No confirmations yet");
        }
        else
        {
            foreach (var roleGroup in attendanceByRole)
            {
                var users = string.Join(", ", roleGroup.Select(x => x.User.FirstName));
                sb.AppendLine($"{roleGroup.Key.Icon} {roleGroup.Key.Name}: {users}");
            }
        }

        var totalYes = evt.Attendances.Count(a => a.Status == AttendanceStatus.Yes);
        var totalNo = evt.Attendances.Count(a => a.Status == AttendanceStatus.No);
        var totalMaybe = evt.Attendances.Count(a => a.Status == AttendanceStatus.Maybe);

        sb.AppendLine($"\n✅ Yes: {totalYes} | ❌ No: {totalNo} | ❓ Maybe: {totalMaybe}");

        return sb.ToString();
    }

    private InlineKeyboardMarkup CreateAttendanceKeyboard(int eventId, int userId)
    {
        var buttons = new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Yes", $"attend_{eventId}_yes"),
                InlineKeyboardButton.WithCallbackData("❌ No", $"attend_{eventId}_no"),
                InlineKeyboardButton.WithCallbackData("❓ Maybe", $"attend_{eventId}_maybe")
            }
        };

        return new InlineKeyboardMarkup(buttons);
    }
}