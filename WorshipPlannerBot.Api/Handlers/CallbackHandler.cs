using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WorshipPlannerBot.Api.Data;
using WorshipPlannerBot.Api.Models;
using WorshipPlannerBot.Api.Services;

namespace WorshipPlannerBot.Api.Handlers;

public class CallbackHandler
{
    private readonly IBotService _botService;
    private readonly BotDbContext _dbContext;
    private readonly RoleHandler _roleHandler;
    private readonly RegistrationHandler _registrationHandler;
    private readonly EventHandler _eventHandler;
    private readonly IConversationStateService _conversationState;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILocalizationService _localization;
    private readonly ILogger<CallbackHandler> _logger;
    private readonly SongManager _songManager;

    public CallbackHandler(
        IBotService botService,
        BotDbContext dbContext,
        RoleHandler roleHandler,
        RegistrationHandler registrationHandler,
        EventHandler eventHandler,
        IConversationStateService conversationState,
        IServiceProvider serviceProvider,
        ILocalizationService localization,
        ILogger<CallbackHandler> logger,
        SongManager songManager)
    {
        _botService = botService;
        _dbContext = dbContext;
        _roleHandler = roleHandler;
        _registrationHandler = registrationHandler;
        _eventHandler = eventHandler;
        _conversationState = conversationState;
        _serviceProvider = serviceProvider;
        _localization = localization;
        _logger = logger;
        _songManager = songManager;
    }

    public async Task HandleAsync(CallbackQuery callbackQuery)
    {
        if (callbackQuery.Data == null || callbackQuery.From == null)
            return;

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.TelegramId == callbackQuery.From.Id);

        if (user == null)
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "Please use /start to register first.");
            return;
        }

        var data = callbackQuery.Data;

        if (data.StartsWith("role_"))
        {
            await HandleRoleCallbackAsync(callbackQuery, user, data);
        }
        else if (data.StartsWith("attend_"))
        {
            await HandleAttendanceCallbackAsync(callbackQuery, user, data);
        }
        else if (data.StartsWith("delete_"))
        {
            await HandleDeleteCallbackAsync(callbackQuery, user, data);
        }
        else if (data.StartsWith("edit_"))
        {
            await HandleEditCallbackAsync(callbackQuery, user, data);
        }
        else if (data.StartsWith("attendance_view_"))
        {
            await HandleAttendanceViewCallbackAsync(callbackQuery, data);
        }
        else if (data.StartsWith("lang_"))
        {
            await HandleLanguageCallbackAsync(callbackQuery, user, data);
        }
        else if (data.StartsWith("chord_"))
        {
            await HandleChordCallbackAsync(callbackQuery, user, data);
        }
        else if (data.StartsWith("wizard_") ||
                 data.StartsWith("song_toggle_") ||
                 data.StartsWith("songs_done") ||
                 data.StartsWith("songs_skip"))
        {
            // Check if we're in wizard context
            var state = _conversationState.GetUserState(callbackQuery.From.Id);
            if (state?.State == "event_wizard")
            {
                await HandleWizardCallbackAsync(callbackQuery, user, data);
            }
            else if (data.StartsWith("song"))
            {
                // Handle song callbacks outside wizard context
                await HandleSongCallbackAsync(callbackQuery, user, data);
            }
        }
        else if (data.StartsWith("event_status_"))
        {
            await HandleEventStatusCallbackAsync(callbackQuery, user, data);
        }
        else if (data.StartsWith("song") || data.StartsWith("songs"))
        {
            await HandleSongCallbackAsync(callbackQuery, user, data);
        }
    }

    private async Task HandleRoleCallbackAsync(CallbackQuery callbackQuery, Models.User user, string data)
    {
        if (data == "role_done")
        {
            await _registrationHandler.CompleteRegistrationAsync(callbackQuery, user);
            return;
        }

        var roleIdStr = data.Replace("role_", "");
        if (int.TryParse(roleIdStr, out var roleId))
        {
            await _roleHandler.ToggleUserRoleAsync(user.Id, roleId);

            var keyboard = await _roleHandler.CreateRoleSelectionKeyboard(user.Id);

            await _botService.Client.EditMessageReplyMarkup(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                replyMarkup: keyboard);

            var role = await _dbContext.Roles.FindAsync(roleId);
            var hasRole = await _dbContext.UserRoles
                .AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == roleId);

            var action = hasRole ? "selected" : "deselected";
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                $"{role?.Name} {action}");
        }
    }

    private async Task HandleAttendanceCallbackAsync(CallbackQuery callbackQuery, Models.User user, string data)
    {
        var parts = data.Split('_');
        if (parts.Length != 3)
            return;

        if (!int.TryParse(parts[1], out var eventId))
            return;

        var statusStr = parts[2];
        var status = statusStr switch
        {
            "yes" => AttendanceStatus.Yes,
            "no" => AttendanceStatus.No,
            "maybe" => AttendanceStatus.Maybe,
            _ => AttendanceStatus.Maybe
        };

        var attendance = await _dbContext.Attendances
            .FirstOrDefaultAsync(a => a.EventId == eventId && a.UserId == user.Id);

        if (attendance == null)
        {
            attendance = new Attendance
            {
                EventId = eventId,
                UserId = user.Id,
                Status = status,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.Attendances.Add(attendance);
        }
        else
        {
            attendance.Status = status;
            attendance.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        var evt = await _dbContext.Events
            .Include(e => e.Attendances)
            .ThenInclude(a => a.User)
            .ThenInclude(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Include(e => e.SetListItems.OrderBy(si => si.OrderIndex))
            .ThenInclude(si => si.Song)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null)
            return;

        var messageText = FormatEventMessage(evt, user.LanguageCode);
        var keyboard = CreateAttendanceKeyboard(evt.Id, user.LanguageCode);

        try
        {
            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                messageText,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating message");
        }

        var responseText = status switch
        {
            AttendanceStatus.Yes => _localization.GetString("AttendanceYes", user.LanguageCode),
            AttendanceStatus.No => _localization.GetString("AttendanceNo", user.LanguageCode),
            AttendanceStatus.Maybe => _localization.GetString("AttendanceMaybe", user.LanguageCode),
            _ => "Response recorded"
        };

        await _botService.Client.AnswerCallbackQuery(callbackQuery.Id, responseText);
    }

    private string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Escape markdown special characters
        return text.Replace("_", "\\_")
                   .Replace("*", "\\*")
                   .Replace("[", "\\[")
                   .Replace("]", "\\]")
                   .Replace("(", "\\(")
                   .Replace(")", "\\)")
                   .Replace("~", "\\~")
                   .Replace("`", "\\`")
                   .Replace(">", "\\>")
                   .Replace("#", "\\#")
                   .Replace("+", "\\+")
                   .Replace("-", "\\-")
                   .Replace("=", "\\=")
                   .Replace("|", "\\|")
                   .Replace("{", "\\{")
                   .Replace("}", "\\}")
                   .Replace(".", "\\.")
                   .Replace("!", "\\!");
    }

    private string FormatEventMessage(Event evt, string languageCode)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"🎵 *{EscapeMarkdown(evt.Title)}*");
        sb.AppendLine($"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy}");
        sb.AppendLine($"🕐 {evt.DateTime.ToLocalTime():HH:mm}");
        sb.AppendLine($"📍 {EscapeMarkdown(evt.Location)}");

        if (!string.IsNullOrEmpty(evt.Description))
            sb.AppendLine($"📝 {EscapeMarkdown(evt.Description)}");

        // Add setlist if available
        if (evt.SetListItems != null && evt.SetListItems.Any())
        {
            sb.AppendLine();
            sb.AppendLine(_localization.GetString("Setlist", languageCode));
            foreach (var item in evt.SetListItems.OrderBy(si => si.OrderIndex))
            {
                if (item.ItemType == Models.Setlist.SetListItemType.Song && item.Song != null)
                {
                    var songInfo = $"  {item.OrderIndex + 1}. {EscapeMarkdown(item.Song.Title)}";
                    var details = new List<string>();

                    if (!string.IsNullOrEmpty(item.Song.Key))
                        details.Add(EscapeMarkdown(item.Song.Key));
                    if (!string.IsNullOrEmpty(item.Song.Tempo))
                        details.Add($"{EscapeMarkdown(item.Song.Tempo)} BPM");

                    if (details.Any())
                        songInfo += $" ({string.Join(", ", details)})";

                    sb.AppendLine(songInfo);
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine($"*{_localization.GetString("PleaseConfirmAttendance", languageCode).Replace("✅ ", "")}*");

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
                var users = string.Join(", ", roleGroup.Select(x => EscapeMarkdown(x.User.FullName)));
                var localizedRoleName = _localization.GetString($"Role.{roleGroup.Key.Name.Replace(" ", "")}", languageCode);
                if (localizedRoleName == $"Role.{roleGroup.Key.Name.Replace(" ", "")}")
                    localizedRoleName = roleGroup.Key.Name;
                sb.AppendLine($"{roleGroup.Key.Icon} {EscapeMarkdown(localizedRoleName)}: {users}");
            }
        }

        var totalYes = evt.Attendances.Count(a => a.Status == AttendanceStatus.Yes);
        var totalNo = evt.Attendances.Count(a => a.Status == AttendanceStatus.No);
        var totalMaybe = evt.Attendances.Count(a => a.Status == AttendanceStatus.Maybe);

        sb.AppendLine();
        sb.AppendLine($"✅ {_localization.GetString("ButtonYes", languageCode).Replace("✅ ", "")}: {totalYes} | ❌ {_localization.GetString("ButtonNo", languageCode).Replace("❌ ", "")}: {totalNo} | ❓ {_localization.GetString("ButtonMaybe", languageCode).Replace("🤔 ", "")}: {totalMaybe}");

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

    private async Task HandleDeleteCallbackAsync(CallbackQuery callbackQuery, Models.User user, string data)
    {
        // Check if user is admin
        if (!user.IsAdmin)
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "⚠️ Only administrators can delete events.");
            return;
        }

        if (data == "delete_cancel")
        {
            await _botService.Client.DeleteMessage(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId);
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "Deletion cancelled.");
            return;
        }

        // Check if this is a confirmation
        if (data.StartsWith("delete_confirm_"))
        {
            var eventIdStr = data.Replace("delete_confirm_", "");
            if (int.TryParse(eventIdStr, out var eventId))
            {
                var evt = await _dbContext.Events
                    .Include(e => e.Attendances)
                    .FirstOrDefaultAsync(e => e.Id == eventId);

                if (evt == null)
                {
                    await _botService.Client.AnswerCallbackQuery(
                        callbackQuery.Id,
                        "❌ Event not found.");
                    return;
                }

                // Delete the event
                _dbContext.Events.Remove(evt);
                await _dbContext.SaveChangesAsync();

                await _botService.Client.EditMessageText(
                    callbackQuery.Message!.Chat.Id,
                    callbackQuery.Message.MessageId,
                    $"✅ Event '{evt.Title}' has been deleted successfully.");

                await _botService.Client.AnswerCallbackQuery(
                    callbackQuery.Id,
                    "Event deleted!");

                // Announce cancellation to groups if there were attendees
                if (evt.Attendances.Any())
                {
                    var groupAnnouncementService = _serviceProvider.GetRequiredService<IGroupAnnouncementService>();
                    await groupAnnouncementService.AnnounceEventUpdateToGroups(evt, "cancelled");
                }
            }
            return;
        }

        // First click - show confirmation
        var deleteEventIdStr = data.Replace("delete_", "");
        if (int.TryParse(deleteEventIdStr, out var deleteEventId))
        {
            var eventToDelete = await _dbContext.Events
                .Include(e => e.Attendances)
                .FirstOrDefaultAsync(e => e.Id == deleteEventId);

            if (eventToDelete == null)
            {
                await _botService.Client.AnswerCallbackQuery(
                    callbackQuery.Id,
                    "❌ Event not found.");
                return;
            }

            var confirmKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Yes, Delete", $"delete_confirm_{deleteEventId}"),
                    InlineKeyboardButton.WithCallbackData("❌ Cancel", "delete_cancel")
                }
            });

            var attendanceInfo = eventToDelete.Attendances.Any()
                ? $"\n\n⚠️ This event has {eventToDelete.Attendances.Count} attendance responses."
                : "";

            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                $"⚠️ *Confirm Deletion*\n\n" +
                $"Are you sure you want to delete:\n\n" +
                $"*{eventToDelete.Title}*\n" +
                $"📅 {eventToDelete.DateTime.ToLocalTime():dddd, dd MMMM yyyy}\n" +
                $"🕐 {eventToDelete.DateTime.ToLocalTime():HH:mm}" +
                attendanceInfo,
                parseMode: ParseMode.Markdown,
                replyMarkup: confirmKeyboard);

            await _botService.Client.AnswerCallbackQuery(callbackQuery.Id);
        }
    }

    private async Task HandleEditCallbackAsync(CallbackQuery callbackQuery, Models.User user, string data)
    {
        // Check if user is admin
        if (!user.IsAdmin)
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "⚠️ Only administrators can edit events.");
            return;
        }

        if (data == "edit_cancel")
        {
            await _botService.Client.DeleteMessage(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId);
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "Edit cancelled.");
            return;
        }

        // Parse the event ID from the callback data
        var eventIdStr = data.Replace("edit_", "").Split('_')[0];
        if (!int.TryParse(eventIdStr, out var eventId))
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "❌ Invalid event ID.");
            return;
        }

        var evt = await _dbContext.Events
            .Include(e => e.SetListItems)
            .ThenInclude(si => si.Song)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null)
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "❌ Event not found.");
            return;
        }

        // Handle field-specific edit callbacks
        if (data.Contains("_field_"))
        {
            var field = data.Split("_field_")[1];
            await HandleEventFieldEdit(callbackQuery, evt, field, user);
            return;
        }

        // Show edit options menu
        var editButtons = new List<InlineKeyboardButton[]>
        {
            new[] { InlineKeyboardButton.WithCallbackData("📝 Edit Title", $"edit_{eventId}_field_title") },
            new[] { InlineKeyboardButton.WithCallbackData("📅 Edit Date", $"edit_{eventId}_field_date") },
            new[] { InlineKeyboardButton.WithCallbackData("🕐 Edit Time", $"edit_{eventId}_field_time") },
            new[] { InlineKeyboardButton.WithCallbackData("📍 Edit Location", $"edit_{eventId}_field_location") },
            new[] { InlineKeyboardButton.WithCallbackData("📄 Edit Description", $"edit_{eventId}_field_description") },
            new[] { InlineKeyboardButton.WithCallbackData("🎵 Edit Setlist", $"edit_{eventId}_field_setlist") },
            new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel", "edit_cancel") }
        };

        var keyboard = new InlineKeyboardMarkup(editButtons);

        var messageText = $"✏️ *Edit Event*\n\n" +
                         $"*{evt.Title}*\n" +
                         $"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy}\n" +
                         $"🕐 {evt.DateTime.ToLocalTime():HH:mm}\n" +
                         $"📍 {evt.Location}\n";

        if (!string.IsNullOrEmpty(evt.Description))
            messageText += $"📝 {evt.Description}\n";

        if (evt.SetListItems != null && evt.SetListItems.Any())
        {
            messageText += "\n🎵 *Setlist:*\n";
            foreach (var item in evt.SetListItems.OrderBy(si => si.OrderIndex))
            {
                if (item.Song != null)
                    messageText += $"  {item.OrderIndex + 1}. {item.Song.Title}\n";
            }
        }

        messageText += "\n*Select what you want to edit:*";

        await _botService.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            messageText,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);

        await _botService.Client.AnswerCallbackQuery(callbackQuery.Id);
    }

    private async Task HandleEventFieldEdit(CallbackQuery callbackQuery, Event evt, string field, Models.User user)
    {
        var messageText = field switch
        {
            "title" => "📝 *Edit Title*\n\nCurrent: " + evt.Title + "\n\nSend the new title:",
            "date" => $"📅 *Edit Date*\n\nCurrent: {evt.DateTime.ToLocalTime():dd/MM/yyyy}\n\nSend the new date (DD/MM/YYYY):",
            "time" => $"🕐 *Edit Time*\n\nCurrent: {evt.DateTime.ToLocalTime():HH:mm}\n\nSend the new time (HH:MM):",
            "location" => "📍 *Edit Location*\n\nCurrent: " + evt.Location + "\n\nSend the new location:",
            "description" => "📄 *Edit Description*\n\nCurrent: " + (evt.Description ?? "None") + "\n\nSend the new description:",
            "setlist" => "🎵 *Edit Setlist*\n\nSend the songs (one per line) or type 'clear' to remove all songs:",
            _ => null
        };

        if (messageText == null)
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "Invalid field.");
            return;
        }

        // Set conversation state for text input
        _conversationState.SetUserState(callbackQuery.From.Id, "event_edit", new
        {
            EventId = evt.Id,
            Field = field,
            MessageId = callbackQuery.Message!.MessageId,
            ChatId = callbackQuery.Message.Chat.Id
        });

        await _botService.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            messageText,
            parseMode: ParseMode.Markdown);

        await _botService.Client.AnswerCallbackQuery(
            callbackQuery.Id,
            "Please send the new value.");
    }

    private async Task HandleAttendanceViewCallbackAsync(CallbackQuery callbackQuery, string data)
    {
        if (data == "attendance_cancel")
        {
            await _botService.Client.DeleteMessage(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId);
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "Cancelled.");
            return;
        }

        var eventIdStr = data.Replace("attendance_view_", "");
        if (!int.TryParse(eventIdStr, out var eventId))
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "❌ Invalid event ID.");
            return;
        }

        // Get all users
        var allUsers = await _dbContext.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .OrderBy(u => u.FullName)
            .ToListAsync();

        // Get the event with attendances
        var evt = await _dbContext.Events
            .Include(e => e.Attendances)
            .ThenInclude(a => a.User)
            .ThenInclude(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null)
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "❌ Event not found.");
            return;
        }

        // Build detailed attendance message
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📊 *Attendance Report*\n");
        sb.AppendLine($"🎵 *{EscapeMarkdown(evt.Title)}*");
        sb.AppendLine($"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy}");
        sb.AppendLine($"🕐 {evt.DateTime.ToLocalTime():HH:mm}\n");

        // Get users who responded
        var respondedUserIds = evt.Attendances.Select(a => a.UserId).ToHashSet();
        var notRespondedUsers = allUsers.Where(u => !respondedUserIds.Contains(u.Id)).ToList();

        // Group attendances by status
        var confirmedUsers = evt.Attendances
            .Where(a => a.Status == AttendanceStatus.Yes)
            .OrderBy(a => a.User.FullName)
            .ToList();

        var maybeUsers = evt.Attendances
            .Where(a => a.Status == AttendanceStatus.Maybe)
            .OrderBy(a => a.User.FullName)
            .ToList();

        var declinedUsers = evt.Attendances
            .Where(a => a.Status == AttendanceStatus.No)
            .OrderBy(a => a.User.FullName)
            .ToList();

        // Show confirmed attendees
        sb.AppendLine($"✅ *Confirmed ({confirmedUsers.Count}):*");
        if (confirmedUsers.Any())
        {
            foreach (var attendance in confirmedUsers)
            {
                var roles = attendance.User.UserRoles.Select(ur => ur.Role.Icon);
                var roleIcons = roles.Any() ? " " + string.Join("", roles) : "";
                sb.AppendLine($"• {EscapeMarkdown(attendance.User.FullName)}{roleIcons}");
            }
        }
        else
        {
            sb.AppendLine("_None_");
        }

        // Show maybe attendees
        sb.AppendLine($"\n🤔 *Maybe ({maybeUsers.Count}):*");
        if (maybeUsers.Any())
        {
            foreach (var attendance in maybeUsers)
            {
                var roles = attendance.User.UserRoles.Select(ur => ur.Role.Icon);
                var roleIcons = roles.Any() ? " " + string.Join("", roles) : "";
                sb.AppendLine($"• {EscapeMarkdown(attendance.User.FullName)}{roleIcons}");
            }
        }
        else
        {
            sb.AppendLine("_None_");
        }

        // Show declined attendees
        sb.AppendLine($"\n❌ *Declined ({declinedUsers.Count}):*");
        if (declinedUsers.Any())
        {
            foreach (var attendance in declinedUsers)
            {
                sb.AppendLine($"• {EscapeMarkdown(attendance.User.FullName)}");
            }
        }
        else
        {
            sb.AppendLine("_None_");
        }

        // Show users who haven't responded
        sb.AppendLine($"\n⏳ *No Response ({notRespondedUsers.Count}):*");
        if (notRespondedUsers.Any())
        {
            foreach (var notRespondedUser in notRespondedUsers)
            {
                var roles = notRespondedUser.UserRoles.Select(ur => ur.Role.Icon);
                var roleIcons = roles.Any() ? " " + string.Join("", roles) : "";
                sb.AppendLine($"• {EscapeMarkdown(notRespondedUser.FullName)}{roleIcons}");
            }
        }
        else
        {
            sb.AppendLine("_All users have responded_");
        }

        // Summary statistics
        sb.AppendLine($"\n📈 *Summary:*");
        sb.AppendLine($"Total registered users: {allUsers.Count}");
        sb.AppendLine($"Responses received: {evt.Attendances.Count} ({(evt.Attendances.Count * 100.0 / allUsers.Count):F1}%)");

        // Role coverage
        var allRoles = await _dbContext.Roles.OrderBy(r => r.DisplayOrder).ToListAsync();
        var coveredRoles = confirmedUsers
            .SelectMany(a => a.User.UserRoles.Select(ur => ur.Role))
            .Distinct()
            .ToList();

        var missingRoles = allRoles.Where(r => !coveredRoles.Any(cr => cr.Id == r.Id)).ToList();

        if (missingRoles.Any())
        {
            sb.AppendLine($"\n⚠️ *Missing roles:*");
            foreach (var role in missingRoles)
            {
                sb.AppendLine($"{role.Icon} {EscapeMarkdown(role.Name)}");
            }
        }
        else
        {
            sb.AppendLine($"\n✅ *All roles covered!*");
        }

        await _botService.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            sb.ToString(),
            parseMode: ParseMode.Markdown);

        await _botService.Client.AnswerCallbackQuery(callbackQuery.Id);
    }

    private async Task HandleLanguageCallbackAsync(CallbackQuery callbackQuery, Models.User user, string data)
    {
        var newLanguage = data.Replace("lang_", "");

        // Update user's language preference
        user.LanguageCode = newLanguage;
        await _dbContext.SaveChangesAsync();

        // Get localization service
        var localizationService = _serviceProvider.GetRequiredService<ILocalizationService>();

        // Send confirmation in the new language
        var confirmationMessage = localizationService.GetString("LanguageChanged", newLanguage);

        await _botService.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            confirmationMessage);

        await _botService.Client.AnswerCallbackQuery(
            callbackQuery.Id,
            "✅");
    }

    private async Task HandleWizardCallbackAsync(CallbackQuery callbackQuery, Models.User user, string data)
    {
        using var scope = _serviceProvider.CreateScope();
        var wizard = scope.ServiceProvider.GetRequiredService<EventCreationWizard>();
        await wizard.HandleWizardCallback(callbackQuery, user, data);
    }

    private async Task HandleEventStatusCallbackAsync(CallbackQuery callbackQuery, Models.User user, string data)
    {
        var eventIdStr = data.Replace("event_status_", "");
        if (!int.TryParse(eventIdStr, out var eventId))
        {
            await _botService.Client.AnswerCallbackQuery(callbackQuery.Id, "Invalid event ID");
            return;
        }

        var evt = await _dbContext.Events
            .Include(e => e.Attendances)
            .ThenInclude(a => a.User)
            .ThenInclude(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Include(e => e.SetListItems.OrderBy(si => si.OrderIndex))
            .ThenInclude(si => si.Song)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null)
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                _localization.GetString("EventNotFound", user.LanguageCode));
            return;
        }

        var statusMessage = BuildEventStatusMessage(evt, user.LanguageCode);

        await _botService.Client.SendMessage(
            callbackQuery.From.Id,
            statusMessage,
            parseMode: ParseMode.Markdown);

        await _botService.Client.AnswerCallbackQuery(
            callbackQuery.Id,
            _localization.GetString("StatusSentPrivate", user.LanguageCode));
    }

    private string BuildEventStatusMessage(Event evt, string languageCode)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"{_localization.GetString("EventStatus", languageCode, EscapeMarkdown(evt.Title))}");
        sb.AppendLine();
        sb.AppendLine($"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy}");
        sb.AppendLine($"🕐 {evt.DateTime.ToLocalTime():HH:mm}");
        sb.AppendLine($"📍 {EscapeMarkdown(evt.Location)}");

        if (!string.IsNullOrEmpty(evt.Description))
            sb.AppendLine($"📝 {EscapeMarkdown(evt.Description)}");

        // Add setlist if available
        if (evt.SetListItems != null && evt.SetListItems.Any())
        {
            sb.AppendLine();
            sb.AppendLine(_localization.GetString("Setlist", languageCode));
            foreach (var item in evt.SetListItems.OrderBy(si => si.OrderIndex))
            {
                if (item.ItemType == Models.Setlist.SetListItemType.Song && item.Song != null)
                {
                    var songInfo = $"  {item.OrderIndex + 1}. {EscapeMarkdown(item.Song.Title)}";
                    var details = new List<string>();

                    if (!string.IsNullOrEmpty(item.Song.Key))
                        details.Add(EscapeMarkdown(item.Song.Key));
                    if (!string.IsNullOrEmpty(item.Song.Tempo))
                        details.Add($"{EscapeMarkdown(item.Song.Tempo)} BPM");

                    if (details.Any())
                        songInfo += $" ({string.Join(", ", details)})";

                    sb.AppendLine(songInfo);
                }
            }
        }

        // Attendance by status
        var confirmedUsers = evt.Attendances.Where(a => a.Status == AttendanceStatus.Yes).ToList();
        var maybeUsers = evt.Attendances.Where(a => a.Status == AttendanceStatus.Maybe).ToList();
        var declinedUsers = evt.Attendances.Where(a => a.Status == AttendanceStatus.No).ToList();

        sb.AppendLine();
        sb.AppendLine(_localization.GetString("AttendanceSummary", languageCode));

        if (confirmedUsers.Any())
        {
            sb.AppendLine();
            sb.AppendLine(_localization.GetString("ConfirmedLabel", languageCode, confirmedUsers.Count));
            foreach (var attendance in confirmedUsers.OrderBy(a => a.User.FullName))
            {
                var roles = attendance.User.UserRoles.Select(ur => ur.Role.Icon).ToList();
                var roleIcons = roles.Any() ? " " + string.Join(" ", roles) : "";
                sb.AppendLine($"• {EscapeMarkdown(attendance.User.FullName)}{roleIcons}");
            }
        }

        if (maybeUsers.Any())
        {
            sb.AppendLine();
            sb.AppendLine(_localization.GetString("MaybeLabel", languageCode, maybeUsers.Count));
            foreach (var attendance in maybeUsers.OrderBy(a => a.User.FullName))
            {
                sb.AppendLine($"• {attendance.User.FullName}");
            }
        }

        if (declinedUsers.Any())
        {
            sb.AppendLine();
            sb.AppendLine(_localization.GetString("CantAttendLabel", languageCode, declinedUsers.Count));
            foreach (var attendance in declinedUsers.OrderBy(a => a.User.FullName))
            {
                sb.AppendLine($"• {attendance.User.FullName}");
            }
        }

        // Show role coverage
        var roleGroups = confirmedUsers
            .SelectMany(a => a.User.UserRoles.Select(ur => ur.Role))
            .GroupBy(r => r.Name)
            .OrderBy(g => g.First().DisplayOrder);

        if (roleGroups.Any())
        {
            sb.AppendLine();
            sb.AppendLine(_localization.GetString("RoleCoverage", languageCode));
            foreach (var roleGroup in roleGroups)
            {
                var localizedRoleName = _localization.GetString($"Role.{roleGroup.Key.Replace(" ", "")}", languageCode);
                if (localizedRoleName == $"Role.{roleGroup.Key.Replace(" ", "")}")
                    localizedRoleName = roleGroup.Key;
                sb.AppendLine($"{roleGroup.First().Icon} {localizedRoleName}: {roleGroup.Count()}");
            }
        }

        return sb.ToString();
    }

    private async Task HandleChordCallbackAsync(CallbackQuery callbackQuery, Models.User user, string data)
    {
        using var scope = _serviceProvider.CreateScope();
        var chordChartService = scope.ServiceProvider.GetRequiredService<ChordChartService>();

        // Handle chord chart callbacks
        if (data.StartsWith("chord_song_"))
        {
            var songIdStr = data.Replace("chord_song_", "");
            if (int.TryParse(songIdStr, out var songId))
            {
                await chordChartService.HandleChordSongSelection(callbackQuery, songId);
            }
        }
        else if (data.StartsWith("chord_view_"))
        {
            var chartIdStr = data.Replace("chord_view_", "");
            if (int.TryParse(chartIdStr, out var chartId))
            {
                await chordChartService.ShowChordChart(callbackQuery, chartId);
            }
        }
        else if (data.StartsWith("chord_add_"))
        {
            var songIdStr = data.Replace("chord_add_", "");
            if (int.TryParse(songIdStr, out var songId))
            {
                await chordChartService.StartAddChordChart(callbackQuery, songId);
            }
        }
        else if (data.StartsWith("chord_transpose_"))
        {
            if (data.Contains("_to_"))
            {
                // Handle actual transposition
                var parts = data.Split('_');
                if (parts.Length >= 4 && int.TryParse(parts[2], out var chartId))
                {
                    var newKey = parts[parts.Length - 1];
                    // TODO: Implement actual transpose functionality
                    await _botService.Client.AnswerCallbackQuery(
                        callbackQuery.Id,
                        $"Transposing to {newKey} (coming soon)");
                }
            }
            else
            {
                var chartIdStr = data.Replace("chord_transpose_", "");
                if (int.TryParse(chartIdStr, out var chartId))
                {
                    await chordChartService.TransposeChordChart(callbackQuery, chartId);
                }
            }
        }
        else if (data == "chord_cancel")
        {
            await _botService.Client.DeleteMessage(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId);
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "Cancelled.");
        }
        else if (data == "chord_back")
        {
            await chordChartService.ShowChordChartMenu(callbackQuery.Message!, user);
            await _botService.Client.AnswerCallbackQuery(callbackQuery.Id);
        }
    }

    private async Task HandleSongCallbackAsync(CallbackQuery callbackQuery, Models.User user, string data)
    {
        try
        {
            // Handle song list navigation
            if (data.StartsWith("songs_page_"))
            {
                var pageStr = data.Replace("songs_page_", "");
                if (int.TryParse(pageStr, out var page))
                {
                    await _songManager.ShowSongLibrary(callbackQuery.Message!, user, page);
                }
                return;
            }

            if (data == "songs_list" || data == "songs_back")
            {
                await _songManager.ShowSongLibrary(callbackQuery.Message!, user);
                return;
            }

            // Handle song viewing
            if (data.StartsWith("song_view_"))
            {
                var songIdStr = data.Replace("song_view_", "");
                if (int.TryParse(songIdStr, out var songId))
                {
                    await _songManager.ShowSongDetails(callbackQuery, songId, user);
                }
                return;
            }

            // Handle song editing
            if (data.StartsWith("song_edit_"))
            {
                var parts = data.Split('_');
                if (parts.Length == 3 && int.TryParse(parts[2], out var songId))
                {
                    await _songManager.StartSongEdit(callbackQuery, songId);
                }
                else if (parts.Length == 4) // Field specific edit
                {
                    var field = parts[2];
                    if (int.TryParse(parts[3], out var songIdField))
                    {
                        await _songManager.PromptSongFieldEdit(callbackQuery, songIdField, field);

                        // Store the edit state for text input handling
                        _conversationState.SetUserState(callbackQuery.From.Id, "song_edit", new
                        {
                            SongId = songIdField,
                            Field = field,
                            MessageId = callbackQuery.Message!.MessageId
                        });
                    }
                }
                return;
            }

            // Handle song deletion
            if (data.StartsWith("song_delete_"))
            {
                var parts = data.Split('_');
                if (parts.Length >= 3)
                {
                    var songIdStr = parts[2];
                    if (int.TryParse(songIdStr, out var songId))
                    {
                        if (data == $"song_delete_confirm_{songId}")
                        {
                            // Confirmed deletion
                            var song = await _dbContext.Songs.FindAsync(songId);
                            if (song != null)
                            {
                                _dbContext.Songs.Remove(song);
                                await _dbContext.SaveChangesAsync();

                                await _botService.Client.EditMessageText(
                                    callbackQuery.Message!.Chat.Id,
                                    callbackQuery.Message.MessageId,
                                    $"✅ Cântarea *{song.Title}* a fost ștearsă.",
                                    parseMode: ParseMode.Markdown);

                                // Return to list after delay
                                await Task.Delay(2000);
                                await _songManager.ShowSongLibrary(callbackQuery.Message, user);
                            }
                        }
                        else
                        {
                            await _songManager.DeleteSong(callbackQuery, songId);
                        }
                    }
                }
                return;
            }

            // Handle new song creation
            if (data == "song_add_new")
            {
                await _botService.Client.EditMessageText(
                    callbackQuery.Message!.Chat.Id,
                    callbackQuery.Message.MessageId,
                    "🎵 *Adaugă Cântare Nouă*\n\n" +
                    "Trimite numele cântării:\n\n" +
                    "_Puteți include și detalii opționale astfel:_\n" +
                    "Titlu | Tonalitate | Tempo\n\n" +
                    "Exemplu: Amazing Grace | G | 72",
                    parseMode: ParseMode.Markdown);

                _conversationState.SetUserState(callbackQuery.From.Id, "song_add_new", new
                {
                    MessageId = callbackQuery.Message.MessageId
                });
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling song callback");
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "A apărut o eroare. Vă rugăm încercați din nou.");
        }
    }
}