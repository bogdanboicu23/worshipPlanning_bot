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
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventHandler> _logger;
    private readonly ILocalizationService _localization;

    public EventHandler(IBotService botService, BotDbContext dbContext, IServiceProvider serviceProvider, ILogger<EventHandler> logger, ILocalizationService localization)
    {
        _botService = botService;
        _dbContext = dbContext;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _localization = localization;
    }

    public async Task StartEventCreationAsync(Message message, Models.User user)
    {
        var text = "📅 *Create New Worship Service*\n\n" +
                  "Please provide event details in the following format:\n\n" +
                  "Title\n" +
                  "Date and Time (DD/MM/YYYY HH:MM)\n" +
                  "Location\n" +
                  "Description (optional)\n" +
                  "Songs (optional, one per line)\n\n" +
                  "Example:\n" +
                  "Sunday Worship Service\n" +
                  "25/01/2025 10:30\n" +
                  "Main Hall\n" +
                  "Regular Sunday morning worship\n" +
                  "Amazing Grace\n" +
                  "How Great Is Our God\n" +
                  "10,000 Reasons";

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
            .Include(e => e.SetListItems.OrderBy(si => si.OrderIndex))
            .ThenInclude(si => si.Song)
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
            var messageText = FormatEventMessage(evt, user.LanguageCode);
            var keyboard = CreateAttendanceKeyboard(evt.Id, user.LanguageCode);

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
                    "❌ Introduceți titlul, data și locația cel puțin.");
                return;
            }

            var title = lines[0].Trim();
            var dateTimeStr = lines[1].Trim();
            var location = lines[2].Trim();
            var description = lines.Length > 3 && !string.IsNullOrWhiteSpace(lines[3]) ? lines[3].Trim() : null;

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

            // Add songs to setlist if provided
            if (lines.Length > 4)
            {
                var orderIndex = 0;
                for (int i = 4; i < lines.Length; i++)
                {
                    var songTitle = lines[i].Trim();
                    if (!string.IsNullOrWhiteSpace(songTitle))
                    {
                        // Check if song already exists in database
                        var song = await _dbContext.Songs
                            .FirstOrDefaultAsync(s => s.Title.ToLower() == songTitle.ToLower());

                        if (song == null)
                        {
                            // Create new song
                            song = new Models.Setlist.Song
                            {
                                Title = songTitle,
                                CreatedAt = DateTime.UtcNow
                            };
                            _dbContext.Songs.Add(song);
                            await _dbContext.SaveChangesAsync();
                        }

                        // Add to setlist
                        var setlistItem = new Models.Setlist.SetListItem
                        {
                            EventId = newEvent.Id,
                            SongId = song.Id,
                            OrderIndex = orderIndex++,
                            ItemType = Models.Setlist.SetListItemType.Song
                        };
                        _dbContext.SetListItems.Add(setlistItem);
                    }
                }
                await _dbContext.SaveChangesAsync();
            }

            // Reload event with setlist for display
            var completeEvent = await _dbContext.Events
                .Include(e => e.SetListItems.OrderBy(si => si.OrderIndex))
                .ThenInclude(si => si.Song)
                .FirstOrDefaultAsync(e => e.Id == newEvent.Id);

            if (completeEvent != null)
            {
                newEvent = completeEvent;
            }

            var confirmationText = $"✅ {_localization.GetString("EventCreatedSuccess", user.LanguageCode)}\n\n{FormatEventMessage(newEvent, user.LanguageCode)}";
            var keyboard = CreateAttendanceKeyboard(newEvent.Id, user.LanguageCode);

            await _botService.Client.SendMessage(
                message.Chat.Id,
                confirmationText,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);

            // Announce to groups
            var groupAnnouncementService = _serviceProvider.GetRequiredService<IGroupAnnouncementService>();
            await groupAnnouncementService.AnnounceNewEventToGroups(newEvent, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event");
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "❌ An error occurred while creating the event. Please try again.");
        }
    }

    private string FormatEventMessage(Event evt, string languageCode)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🎵 *{evt.Title}*");
        sb.AppendLine($"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy}");
        sb.AppendLine($"🕐 {evt.DateTime.ToLocalTime():HH:mm}");
        sb.AppendLine($"📍 {evt.Location}");

        if (!string.IsNullOrEmpty(evt.Description))
            sb.AppendLine($"📝 {evt.Description}");

        // Add setlist if available
        if (evt.SetListItems != null && evt.SetListItems.Any())
        {
            sb.AppendLine($"\n🎶 *{_localization.GetString("Setlist", languageCode)}:*");
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

                    sb.AppendLine(songInfo);
                }
                else if (!string.IsNullOrEmpty(item.CustomTitle))
                {
                    sb.AppendLine($"  {item.OrderIndex + 1}. {item.CustomTitle}");
                }
            }
        }

        sb.AppendLine($"\n*{_localization.GetString("PleaseConfirmAttendance", languageCode).Replace("✅ ", "")}*");

        var attendanceByRole = evt.Attendances
            .Where(a => a.Status == AttendanceStatus.Yes)
            .SelectMany(a => a.User.UserRoles.Select(ur => new { Role = ur.Role, User = a.User }))
            .GroupBy(x => x.Role)
            .OrderBy(g => g.Key.DisplayOrder);

        if (!attendanceByRole.Any())
        {
            sb.AppendLine(_localization.GetString("NoConfirmationsYet", languageCode));
        }
        else
        {
            foreach (var roleGroup in attendanceByRole)
            {
                var users = string.Join(", ", roleGroup.Select(x => x.User.FullName));
                var localizedRoleName = _localization.GetString($"Role.{roleGroup.Key.Name.Replace(" ", "")}", languageCode);
                if (localizedRoleName == $"Role.{roleGroup.Key.Name.Replace(" ", "")}")
                    localizedRoleName = roleGroup.Key.Name;
                sb.AppendLine($"{roleGroup.Key.Icon} {localizedRoleName}: {users}");
            }
        }

        var totalYes = evt.Attendances.Count(a => a.Status == AttendanceStatus.Yes);
        var totalNo = evt.Attendances.Count(a => a.Status == AttendanceStatus.No);
        var totalMaybe = evt.Attendances.Count(a => a.Status == AttendanceStatus.Maybe);

        sb.AppendLine($"\n✅ {_localization.GetString("ButtonYes", languageCode).Replace("✅ ", "")}: {totalYes} | ❌ {_localization.GetString("ButtonNo", languageCode).Replace("❌ ", "")}: {totalNo} | ❓ {_localization.GetString("ButtonMaybe", languageCode).Replace("🤔 ", "")}: {totalMaybe}");

        return sb.ToString();
    }

    private InlineKeyboardMarkup CreateAttendanceKeyboard(int eventId, string languageCode)
    {
        var buttons = new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(_localization.GetString("ButtonYes", languageCode), $"attend_{eventId}_yes"),
                InlineKeyboardButton.WithCallbackData(_localization.GetString("ButtonNo", languageCode), $"attend_{eventId}_no"),
                InlineKeyboardButton.WithCallbackData(_localization.GetString("ButtonMaybe", languageCode), $"attend_{eventId}_maybe")
            }
        };

        return new InlineKeyboardMarkup(buttons);
    }
}