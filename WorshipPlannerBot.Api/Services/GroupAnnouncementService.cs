using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WorshipPlannerBot.Api.Data;
using WorshipPlannerBot.Api.Models;

namespace WorshipPlannerBot.Api.Services;

public class GroupAnnouncementService : IGroupAnnouncementService
{
    private readonly IBotService _botService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILocalizationService _localization;
    private readonly ILogger<GroupAnnouncementService> _logger;
    private readonly IConfiguration _configuration;

    public GroupAnnouncementService(
        IBotService botService,
        IServiceProvider serviceProvider,
        ILocalizationService localization,
        IConfiguration configuration,
        ILogger<GroupAnnouncementService> logger)
    {
        _botService = botService;
        _serviceProvider = serviceProvider;
        _localization = localization;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task AnnounceNewEventToGroups(Event evt, Models.User creator)
    {
        var groupConfigs = GetConfiguredGroups();

        if (!groupConfigs.Any())
        {
            _logger.LogWarning("No group IDs configured for announcements");
            return;
        }

        // Reload event with setlist items
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var eventWithSetlist = await dbContext.Events
            .Include(e => e.SetListItems.OrderBy(si => si.OrderIndex))
            .ThenInclude(si => si.Song)
            .FirstOrDefaultAsync(e => e.Id == evt.Id);

        if (eventWithSetlist != null)
        {
            evt = eventWithSetlist;
        }

        var message = BuildEventAnnouncementMessage(evt, creator);
        var keyboard = BuildEventKeyboard(evt.Id);

        foreach (var config in groupConfigs)
        {
            try
            {
                await _botService.Client.SendMessage(
                    config.GroupId,
                    message,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard,
                    messageThreadId: config.TopicId);

                _logger.LogInformation($"Event announced to group {config.GroupId} (Topic: {config.TopicId})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to announce event to group {config.GroupId}");
            }
        }
    }

    public async Task AnnounceEventUpdateToGroups(Event evt, string updateType)
    {
        var groupConfigs = GetConfiguredGroups();

        if (!groupConfigs.Any())
            return;

        var message = updateType switch
        {
            "cancelled" => $"❌ *Eveniment anulat*\n\n{evt.Title}\n{evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy HH:mm}",
            "updated" => BuildEventAnnouncementMessage(evt, null),
            _ => null
        };

        if (message == null)
            return;

        foreach (var config in groupConfigs)
        {
            try
            {
                await _botService.Client.SendMessage(
                    config.GroupId,
                    message,
                    parseMode: ParseMode.Markdown,
                    messageThreadId: config.TopicId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to update event in group {config.GroupId}");
            }
        }
    }

    public async Task SendEventSummaryToGroup(long groupChatId, Event evt, int? messageThreadId = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var eventWithAttendances = await dbContext.Events
            .Include(e => e.Attendances)
                .ThenInclude(a => a.User)
                    .ThenInclude(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
            .Include(e => e.SetListItems.OrderBy(si => si.OrderIndex))
                .ThenInclude(si => si.Song)
            .FirstOrDefaultAsync(e => e.Id == evt.Id);

        if (eventWithAttendances == null)
            return;

        var summary = BuildEventSummary(eventWithAttendances);
        var keyboard = BuildEventKeyboard(evt.Id);

        await _botService.Client.SendMessage(
            groupChatId,
            summary,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            messageThreadId: messageThreadId);
    }

    private string BuildEventAnnouncementMessage(Event evt, Models.User? creator)
    {
        var message = $"📢 *Serviciu nou*\n\n";
        message += $"*{evt.Title}*\n";
        message += $"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy}\n";
        message += $"🕐 {evt.DateTime.ToLocalTime():HH:mm}\n";
        message += $"📍 {evt.Location}\n";

        if (!string.IsNullOrEmpty(evt.Description))
            message += $"\n📝 {evt.Description}\n";

        // Add setlist if available
        if (evt.SetListItems != null && evt.SetListItems.Any())
        {
            message += "\n🎶 *Lista de cântări:*\n";
            foreach (var item in evt.SetListItems.OrderBy(si => si.OrderIndex))
            {
                if (item.ItemType == Models.Setlist.SetListItemType.Song && item.Song != null)
                {
                    var songInfo = $"  {item.OrderIndex + 1}. {item.Song.Title}";
                    var details = new List<string>();

                    if (!string.IsNullOrEmpty(item.Song.Key))
                        details.Add(item.Song.Key);
                    if (!string.IsNullOrEmpty(item.Song.Tempo))
                        details.Add($"{item.Song.Tempo} BPM");

                    if (details.Any())
                        songInfo += $" ({string.Join(", ", details)})";

                    message += songInfo + "\n";
                }
                else if (!string.IsNullOrEmpty(item.CustomTitle))
                {
                    message += $"  {item.OrderIndex + 1}. {item.CustomTitle}\n";
                }
            }
        }

        if (creator != null)
            message += $"\n_Creat de {creator.FullName}_\n";

        message += "\n✅ Confirmați prezența:";

        return message;
    }

    private string BuildEventSummary(Event evt)
    {
        var summary = $"📊 *Statut: {evt.Title}*\n\n";
        summary += $"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy} at {evt.DateTime.ToLocalTime():HH:mm}\n";
        summary += $"📍 {evt.Location}\n\n";

        // Add setlist if available
        if (evt.SetListItems != null && evt.SetListItems.Any())
        {
            summary += "🎶 *Listă de cântări:*\n";
            foreach (var item in evt.SetListItems.OrderBy(si => si.OrderIndex))
            {
                if (item.ItemType == Models.Setlist.SetListItemType.Song && item.Song != null)
                {
                    var songInfo = $"  {item.OrderIndex + 1}. {item.Song.Title}";
                    var details = new List<string>();

                    if (!string.IsNullOrEmpty(item.Song.Key))
                        details.Add(item.Song.Key);
                    if (!string.IsNullOrEmpty(item.Song.Tempo))
                        details.Add($"{item.Song.Tempo} BPM");

                    if (details.Any())
                        songInfo += $" ({string.Join(", ", details)})";

                    summary += songInfo + "\n";
                }
                else if (!string.IsNullOrEmpty(item.CustomTitle))
                {
                    summary += $"  {item.OrderIndex + 1}. {item.CustomTitle}\n";
                }
            }
            summary += "\n";
        }

        var confirmedUsers = evt.Attendances.Where(a => a.Status == AttendanceStatus.Yes).ToList();
        var maybeUsers = evt.Attendances.Where(a => a.Status == AttendanceStatus.Maybe).ToList();
        var declinedUsers = evt.Attendances.Where(a => a.Status == AttendanceStatus.No).ToList();

        summary += $"✅ *Confirmat ({confirmedUsers.Count})*\n";
        if (confirmedUsers.Any())
        {
            foreach (var attendance in confirmedUsers)
            {
                var roles = attendance.User.UserRoles.Select(ur => ur.Role.Icon).ToList();
                var roleIcons = roles.Any() ? string.Join(" ", roles) : "";
                summary += $"• {attendance.User.FullName} {roleIcons}\n";
            }
        }

        if (maybeUsers.Any())
        {
            summary += $"\n🤔 *Posibil ({maybeUsers.Count})*\n";
            foreach (var attendance in maybeUsers)
            {
                summary += $"• {attendance.User.FullName}\n";
            }
        }

        if (declinedUsers.Any())
        {
            summary += $"\n❌ *Nu pot fi ({declinedUsers.Count})*\n";
            foreach (var attendance in declinedUsers)
            {
                summary += $"• {attendance.User.FullName}\n";
            }
        }

        // Show role coverage
        summary += "\n📋 *Instrumente/voci confirmate:*\n";
        var roleGroups = confirmedUsers
            .SelectMany(a => a.User.UserRoles.Select(ur => ur.Role))
            .GroupBy(r => r.Name)
            .OrderBy(g => g.First().DisplayOrder);

        foreach (var roleGroup in roleGroups)
        {
            summary += $"{roleGroup.First().Icon} {roleGroup.Key}: {roleGroup.Count()}\n";
        }

        return summary;
    }

    private InlineKeyboardMarkup BuildEventKeyboard(int eventId)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Da", $"attend_{eventId}_yes"),
                InlineKeyboardButton.WithCallbackData("❌ Nu", $"attend_{eventId}_no"),
                InlineKeyboardButton.WithCallbackData("🤔 Posibil", $"attend_{eventId}_maybe")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 Vezi Status", $"event_status_{eventId}")
            }
        });
    }

    private List<GroupConfig> GetConfiguredGroups()
    {
        var groupConfigs = new List<GroupConfig>();
        var groupIdsConfig = _configuration["BotConfiguration:GroupChatIds"];

        if (string.IsNullOrEmpty(groupIdsConfig))
            return groupConfigs;

        // Format: "groupId:topicId,groupId:topicId" or just "groupId" for main chat
        var configs = groupIdsConfig.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var config in configs)
        {
            var parts = config.Trim().Split(':');
            if (long.TryParse(parts[0], out var groupId))
            {
                var topicId = parts.Length > 1 && int.TryParse(parts[1], out var tid) ? tid : (int?)null;
                groupConfigs.Add(new GroupConfig { GroupId = groupId, TopicId = topicId });
            }
        }

        return groupConfigs;
    }

    private List<long> GetConfiguredGroupIds()
    {
        return GetConfiguredGroups().Select(g => g.GroupId).ToList();
    }

    private class GroupConfig
    {
        public long GroupId { get; set; }
        public int? TopicId { get; set; }
    }
}

public interface IGroupAnnouncementService
{
    Task AnnounceNewEventToGroups(Event evt, Models.User creator);
    Task AnnounceEventUpdateToGroups(Event evt, string updateType);
    Task SendEventSummaryToGroup(long groupChatId, Event evt, int? messageThreadId = null);
}