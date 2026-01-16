using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WorshipPlannerBot.Api.Data;
using WorshipPlannerBot.Api.Handlers;
using WorshipPlannerBot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace WorshipPlannerBot.Api.Services;

public class UpdateHandlerService : IUpdateHandlerService
{
    private readonly IBotService _botService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConversationStateService _conversationState;
    private readonly ILogger<UpdateHandlerService> _logger;

    public UpdateHandlerService(
        IBotService botService,
        IServiceProvider serviceProvider,
        IConversationStateService conversationState,
        ILogger<UpdateHandlerService> logger)
    {
        _botService = botService;
        _serviceProvider = serviceProvider;
        _conversationState = conversationState;
        _logger = logger;
    }

    public async Task HandleUpdateAsync(Update update)
    {
        try
        {
            var handler = update.Type switch
            {
                UpdateType.Message => HandleMessageAsync(update.Message!),
                UpdateType.CallbackQuery => HandleCallbackQueryAsync(update.CallbackQuery!),
                _ => HandleUnknownUpdateAsync(update)
            };

            await handler;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update");
        }
    }

    private async Task HandleMessageAsync(Message message)
    {
        if (message.Text is not { } text)
            return;

        _logger.LogInformation($"Received message from {message.From?.Username}: {text}");

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var user = await EnsureUserExistsAsync(dbContext, message.From!);

        var command = text.Split(' ')[0].ToLower();

        // Check for cancel command first
        if (command == "/cancel")
        {
            _conversationState.ClearUserState(message.From!.Id);
            await _botService.Client.SendMessage(message.Chat.Id, "❌ Operation cancelled.");
            return;
        }

        // Check if user is in a conversation state
        var state = _conversationState.GetUserState(message.From!.Id);
        if (state != null)
        {
            await HandleConversationStateAsync(message, user, state);
            return;
        }

        var handler = command switch
        {
            "/start" => HandleStartCommandAsync(message, user),
            "/help" => HandleHelpCommandAsync(message, user),
            "/register" => HandleRegisterCommandAsync(message, user),
            "/myroles" => HandleMyRolesCommandAsync(message, user),
            "/newevent" => HandleNewEventCommandAsync(message, user),
            "/events" => HandleEventsCommandAsync(message, user),
            "/remind" => HandleReminderCommandAsync(message, user),
            "/remindnext" => HandleRemindNextCommandAsync(message, user),
            "/admin" => HandleAdminCommandAsync(message, user),
            _ => HandleUnknownCommandAsync(message)
        };

        await handler;
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery)
    {
        if (callbackQuery.Data is null)
            return;

        _logger.LogInformation($"Received callback: {callbackQuery.Data}");

        using var scope = _serviceProvider.CreateScope();
        var callbackHandler = scope.ServiceProvider.GetRequiredService<CallbackHandler>();
        await callbackHandler.HandleAsync(callbackQuery);

        await _botService.Client.AnswerCallbackQuery(callbackQuery.Id);
    }

    private async Task HandleStartCommandAsync(Message message, Models.User user)
    {
        var welcomeText = $"Welcome to Worship Planner Bot! 🎵\n\n" +
                         $"I help manage worship service planning and attendance.\n\n" +
                         $"Use /register to set up your profile and select your roles.\n" +
                         $"Use /help to see all available commands.";

        await _botService.Client.SendMessage(message.Chat.Id, welcomeText);
    }

    private async Task HandleHelpCommandAsync(Message message, Models.User user)
    {
        var helpText = "📋 *Available Commands:*\n\n" +
                      "/register - Set up your profile and roles\n" +
                      "/myroles - View and manage your roles\n" +
                      "/events - View upcoming worship services\n" +
                      "/help - Show this help message\n";

        if (user.IsAdmin)
        {
            helpText += "\n*Admin Commands:*\n" +
                       "/newevent - Create a new worship service\n" +
                       "/remind - Send reminder for next event\n" +
                       "/remindnext - Send custom message to attendees\n" +
                       "/admin - Admin management panel\n";
        }

        await _botService.Client.SendMessage(
            message.Chat.Id,
            helpText,
            parseMode: ParseMode.Markdown);
    }

    private async Task HandleRegisterCommandAsync(Message message, Models.User user)
    {
        using var scope = _serviceProvider.CreateScope();
        var registrationHandler = scope.ServiceProvider.GetRequiredService<RegistrationHandler>();
        await registrationHandler.StartRegistrationAsync(message, user);
    }

    private async Task HandleMyRolesCommandAsync(Message message, Models.User user)
    {
        using var scope = _serviceProvider.CreateScope();
        var roleHandler = scope.ServiceProvider.GetRequiredService<RoleHandler>();
        await roleHandler.ShowUserRolesAsync(message, user);
    }

    private async Task HandleNewEventCommandAsync(Message message, Models.User user)
    {
        if (!user.IsAdmin)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "⚠️ This command is only available to administrators.");
            return;
        }

        // Set conversation state to expect event details
        _conversationState.SetUserState(message.From!.Id, "awaiting_event_details", user);

        using var scope = _serviceProvider.CreateScope();
        var eventHandler = scope.ServiceProvider.GetRequiredService<WorshipPlannerBot.Api.Handlers.EventHandler>();
        await eventHandler.StartEventCreationAsync(message, user);
    }

    private async Task HandleEventsCommandAsync(Message message, Models.User user)
    {
        using var scope = _serviceProvider.CreateScope();
        var eventHandler = scope.ServiceProvider.GetRequiredService<WorshipPlannerBot.Api.Handlers.EventHandler>();
        await eventHandler.ShowUpcomingEventsAsync(message, user);
    }

    private async Task HandleAdminCommandAsync(Message message, Models.User user)
    {
        if (!user.IsAdmin)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "⚠️ This command is only available to administrators.");
            return;
        }

        var adminText = "🛠 *Admin Panel*\n\n" +
                       "Available admin actions:\n" +
                       "/newevent - Create a new worship service\n" +
                       "/setrole @username role - Assign role to user\n" +
                       "/makeadmin @username - Grant admin privileges\n" +
                       "/removeadmin @username - Revoke admin privileges\n";

        await _botService.Client.SendMessage(
            message.Chat.Id,
            adminText,
            parseMode: ParseMode.Markdown);
    }

    private async Task HandleReminderCommandAsync(Message message, Models.User user)
    {
        if (!user.IsAdmin)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "⚠️ This command is only available to administrators.");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var reminderService = scope.ServiceProvider.GetRequiredService<IReminderService>();

        // Get the next upcoming event
        var nextEvent = await dbContext.Events
            .Where(e => e.DateTime > DateTime.UtcNow && !e.IsCancelled)
            .OrderBy(e => e.DateTime)
            .Include(e => e.Attendances)
            .FirstOrDefaultAsync();

        if (nextEvent == null)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "📭 No upcoming events found.");
            return;
        }

        var confirmedCount = nextEvent.Attendances.Count(a => a.Status == AttendanceStatus.Yes);

        if (confirmedCount == 0)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                $"⚠️ No confirmed attendees for '{nextEvent.Title}'");
            return;
        }

        await reminderService.SendEventReminder(nextEvent.Id, "general");

        await _botService.Client.SendMessage(
            message.Chat.Id,
            $"✅ Reminder sent to {confirmedCount} confirmed attendees for:\n\n" +
            $"*{nextEvent.Title}*\n" +
            $"📅 {nextEvent.DateTime.ToLocalTime():dddd, dd MMMM yyyy}\n" +
            $"🕐 {nextEvent.DateTime.ToLocalTime():HH:mm}",
            parseMode: ParseMode.Markdown);
    }

    private async Task HandleRemindNextCommandAsync(Message message, Models.User user)
    {
        if (!user.IsAdmin)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "⚠️ This command is only available to administrators.");
            return;
        }

        // Set conversation state to expect custom message
        _conversationState.SetUserState(message.From!.Id, "awaiting_reminder_message", null);

        await _botService.Client.SendMessage(
            message.Chat.Id,
            "📝 Please type the custom reminder message you want to send to all confirmed attendees of the next event.\n\n" +
            "Type /cancel to cancel.");
    }

    private async Task HandleUnknownCommandAsync(Message message)
    {
        await _botService.Client.SendMessage(
            message.Chat.Id,
            "❓ Unknown command. Use /help to see available commands.");
    }

    private Task HandleUnknownUpdateAsync(Update update)
    {
        _logger.LogInformation($"Unknown update type: {update.Type}");
        return Task.CompletedTask;
    }

    private async Task HandleConversationStateAsync(Message message, Models.User user, ConversationState state)
    {
        if (state.State == "awaiting_event_details")
        {
            // Parse event details and create event
            var lines = message.Text?.Split('\n') ?? Array.Empty<string>();

            if (lines.Length < 3)
            {
                await _botService.Client.SendMessage(
                    message.Chat.Id,
                    "❌ Invalid format. Please provide at least:\n\nTitle\nDate/Time (DD/MM/YYYY HH:MM)\nLocation\n\nOr type /cancel to cancel event creation.");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var eventHandler = scope.ServiceProvider.GetRequiredService<WorshipPlannerBot.Api.Handlers.EventHandler>();
            await eventHandler.CreateEventAsync(message, user, lines);

            // Clear conversation state after processing
            _conversationState.ClearUserState(message.From!.Id);
        }
        else if (state.State == "awaiting_reminder_message")
        {
            // Send custom reminder message
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            var reminderService = scope.ServiceProvider.GetRequiredService<IReminderService>();

            // Get the next upcoming event
            var nextEvent = await dbContext.Events
                .Where(e => e.DateTime > DateTime.UtcNow && !e.IsCancelled)
                .OrderBy(e => e.DateTime)
                .Include(e => e.Attendances)
                .FirstOrDefaultAsync();

            if (nextEvent == null)
            {
                await _botService.Client.SendMessage(
                    message.Chat.Id,
                    "📭 No upcoming events found.");
            }
            else
            {
                var confirmedCount = nextEvent.Attendances.Count(a => a.Status == AttendanceStatus.Yes);

                if (confirmedCount == 0)
                {
                    await _botService.Client.SendMessage(
                        message.Chat.Id,
                        $"⚠️ No confirmed attendees for '{nextEvent.Title}'");
                }
                else
                {
                    await reminderService.SendCustomReminder(nextEvent.Id, message.Text ?? "");

                    await _botService.Client.SendMessage(
                        message.Chat.Id,
                        $"✅ Custom message sent to {confirmedCount} confirmed attendees for:\n\n" +
                        $"*{nextEvent.Title}*\n" +
                        $"📅 {nextEvent.DateTime.ToLocalTime():dddd, dd MMMM yyyy}\n" +
                        $"🕐 {nextEvent.DateTime.ToLocalTime():HH:mm}",
                        parseMode: ParseMode.Markdown);
                }
            }

            // Clear conversation state
            _conversationState.ClearUserState(message.From!.Id);
        }
    }

    private async Task<Models.User> EnsureUserExistsAsync(BotDbContext dbContext, Telegram.Bot.Types.User telegramUser)
    {
        var user = await dbContext.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.TelegramId == telegramUser.Id);

        if (user == null)
        {
            user = new Models.User
            {
                TelegramId = telegramUser.Id,
                FirstName = telegramUser.FirstName,
                LastName = telegramUser.LastName,
                Username = telegramUser.Username,
                CreatedAt = DateTime.UtcNow,
                LastActiveAt = DateTime.UtcNow
            };

            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }
        else
        {
            user.LastActiveAt = DateTime.UtcNow;
            user.FirstName = telegramUser.FirstName;
            user.LastName = telegramUser.LastName;
            user.Username = telegramUser.Username;
            await dbContext.SaveChangesAsync();
        }

        return user;
    }
}

public interface IUpdateHandlerService
{
    Task HandleUpdateAsync(Update update);
}