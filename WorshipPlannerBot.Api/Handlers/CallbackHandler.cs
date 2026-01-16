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
    private readonly ILogger<CallbackHandler> _logger;

    public CallbackHandler(
        IBotService botService,
        BotDbContext dbContext,
        RoleHandler roleHandler,
        RegistrationHandler registrationHandler,
        EventHandler eventHandler,
        ILogger<CallbackHandler> logger)
    {
        _botService = botService;
        _dbContext = dbContext;
        _roleHandler = roleHandler;
        _registrationHandler = registrationHandler;
        _eventHandler = eventHandler;
        _logger = logger;
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
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null)
            return;

        var messageText = FormatEventMessage(evt);
        var keyboard = CreateAttendanceKeyboard(evt.Id);

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
            AttendanceStatus.Yes => "✅ You're attending!",
            AttendanceStatus.No => "❌ You're not attending",
            AttendanceStatus.Maybe => "❓ You're marked as maybe",
            _ => "Response recorded"
        };

        await _botService.Client.AnswerCallbackQuery(callbackQuery.Id, responseText);
    }

    private string FormatEventMessage(Event evt)
    {
        var sb = new System.Text.StringBuilder();
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

    private InlineKeyboardMarkup CreateAttendanceKeyboard(int eventId)
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