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
    private readonly ILocalizationService _localization;
    private readonly ILogger<UpdateHandlerService> _logger;

    public UpdateHandlerService(
        IBotService botService,
        IServiceProvider serviceProvider,
        IConversationStateService conversationState,
        ILocalizationService localization,
        ILogger<UpdateHandlerService> logger)
    {
        _botService = botService;
        _serviceProvider = serviceProvider;
        _conversationState = conversationState;
        _localization = localization;
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
                UpdateType.InlineQuery => HandleInlineQueryAsync(update.InlineQuery!),
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

        var isGroupChat = message.Chat.Type == ChatType.Group || message.Chat.Type == ChatType.Supergroup;
        var isPrivateChat = message.Chat.Type == ChatType.Private;
        var isInTopic = message.MessageThreadId.HasValue && message.MessageThreadId.Value > 0;

        _logger.LogInformation($"Received message from {message.From?.Username} in {message.Chat.Type} (Chat ID: {message.Chat.Id}, Topic: {message.MessageThreadId}): {text}");

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var user = await EnsureUserExistsAsync(dbContext, message.From!);

        var command = text.Split(' ')[0].ToLower();

        // In group chats, handle specific commands
        if (isGroupChat)
        {
            // Strip bot username from command if present (e.g., /events@botname -> /events)
            var baseCommand = command.Contains('@') ? command.Split('@')[0] : command;

            switch (baseCommand)
            {
                case "/events":
                    await HandleEventsInGroupAsync(message, user);
                    return;
                case "/nextevent":
                    await HandleNextEventInGroupAsync(message, user);
                    return;
                case "/attendance":
                    await HandleAttendanceInGroupAsync(message, user);
                    return;
                default:
                    // Ignore other commands in group chat
                    return;
            }
        }

        // Private chat handling
        // Check for cancel command first
        if (command == "/cancel")
        {
            _conversationState.ClearUserState(message.From!.Id);
            var cancelText = _localization.GetString("CancelSuccess", user.LanguageCode);
            await _botService.Client.SendMessage(message.Chat.Id, cancelText);
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
            "/language" => HandleLanguageCommandAsync(message, user),
            "/newevent" => HandleNewEventCommandAsync(message, user),
            "/events" => HandleEventsCommandAsync(message, user),
            "/songs" => HandleSongsCommandAsync(message, user),
            "/chords" => HandleChordsCommandAsync(message, user),
            "/deleteevent" => HandleDeleteEventCommandAsync(message, user),
            "/editevent" => HandleEditEventCommandAsync(message, user),
            "/attendance" => HandleAttendanceCommandAsync(message, user),
            "/remind" => HandleReminderCommandAsync(message, user),
            "/remindnext" => HandleRemindNextCommandAsync(message, user),
            "/admin" => HandleAdminCommandAsync(message, user),
            "/makeadmin968112493" => HandleMakeAdminAsync(message, user),
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

    private async Task HandleInlineQueryAsync(InlineQuery inlineQuery)
    {
        _logger.LogInformation($"Received inline query from {inlineQuery.From.Username}: {inlineQuery.Query}");

        using var scope = _serviceProvider.CreateScope();
        var inlineHandler = scope.ServiceProvider.GetRequiredService<InlineQueryHandler>();
        await inlineHandler.HandleInlineQueryAsync(inlineQuery);
    }

    private async Task HandleStartCommandAsync(Message message, Models.User user)
    {
        var welcomeText = _localization.GetString("Welcome", user.LanguageCode);
        await _botService.Client.SendMessage(message.Chat.Id, welcomeText);
    }

    private async Task HandleHelpCommandAsync(Message message, Models.User user)
    {
        var lang = user.LanguageCode;
        var helpText = _localization.GetString("HelpTitle", lang) + "\n\n" +
                      $"/register - {_localization.GetString("HelpRegister", lang)}\n" +
                      $"/myroles - {_localization.GetString("HelpMyRoles", lang)}\n" +
                      $"/events - {_localization.GetString("HelpEvents", lang)}\n" +
                      $"/songs - {_localization.GetString("HelpSongs", lang)}\n" +
                      $"/chords - Manage chord charts and lyrics\n" +
                      $"/language - {_localization.GetString("HelpLanguage", lang)}\n" +
                      $"/help - {_localization.GetString("HelpTitle", lang)}\n";

        if (user.IsAdmin)
        {
            helpText += $"\n{_localization.GetString("AdminCommands", lang)}\n" +
                       $"/newevent - {_localization.GetString("HelpNewEvent", lang)}\n" +
                       $"/editevent - Edit an existing event\n" +
                       $"/deleteevent - {_localization.GetString("HelpDeleteEvent", lang)}\n" +
                       $"/remind - {_localization.GetString("HelpRemind", lang)}\n" +
                       $"/admin - {_localization.GetString("AdminPanel", lang)}\n";
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

    private async Task HandleLanguageCommandAsync(Message message, Models.User user)
    {
        var buttons = new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🇬🇧 English", "lang_en") },
            new[] { InlineKeyboardButton.WithCallbackData("🇷🇴 Română", "lang_ro") },
            new[] { InlineKeyboardButton.WithCallbackData("🇷🇺 Русский", "lang_ru") }
        };

        var keyboard = new InlineKeyboardMarkup(buttons);

        await _botService.Client.SendMessage(
            message.Chat.Id,
            _localization.GetString("SelectLanguage", user.LanguageCode),
            replyMarkup: keyboard);
    }

    private async Task HandleNewEventCommandAsync(Message message, Models.User user)
    {
        if (!user.IsAdmin)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localization.GetString("AdminOnly", user.LanguageCode));
            return;
        }

        // Start the event creation wizard
        using var scope = _serviceProvider.CreateScope();
        var eventWizard = scope.ServiceProvider.GetRequiredService<EventCreationWizard>();
        await eventWizard.StartWizard(message, user);
    }

    private async Task HandleEventsCommandAsync(Message message, Models.User user)
    {
        using var scope = _serviceProvider.CreateScope();
        var eventHandler = scope.ServiceProvider.GetRequiredService<WorshipPlannerBot.Api.Handlers.EventHandler>();
        await eventHandler.ShowUpcomingEventsAsync(message, user);
    }

    private async Task HandleSongsCommandAsync(Message message, Models.User user)
    {
        using var scope = _serviceProvider.CreateScope();
        var songManager = scope.ServiceProvider.GetRequiredService<SongManager>();
        await songManager.ShowSongLibrary(message, user);
    }

    private async Task HandleChordsCommandAsync(Message message, Models.User user)
    {
        using var scope = _serviceProvider.CreateScope();
        var chordChartService = scope.ServiceProvider.GetRequiredService<ChordChartService>();
        await chordChartService.ShowChordChartMenu(message, user);
    }

    private async Task HandleAdminCommandAsync(Message message, Models.User user)
    {
        if (!user.IsAdmin)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localization.GetString("AdminOnly", user.LanguageCode));
            return;
        }

        var adminText = _localization.GetString("AdminPanel", user.LanguageCode);

        await _botService.Client.SendMessage(
            message.Chat.Id,
            adminText,
            parseMode: ParseMode.Markdown);
    }

    private async Task HandleMakeAdminAsync(Message message, Models.User user)
    {
        // Secret command to make yourself admin - only works for Bogdan (TelegramId: 968112493)
        if (message.From?.Id == 968112493)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

            user.IsAdmin = true;
            dbContext.Users.Update(user);
            await dbContext.SaveChangesAsync();

            await _botService.Client.SendMessage(
                message.Chat.Id,
                "✅ You are now an admin! You can use:\n" +
                "/newevent - Create events\n" +
                "/deleteevent - Delete events\n" +
                "/remind - Send reminders\n" +
                "/admin - Admin panel");
        }
        else
        {
            await HandleUnknownCommandAsync(message);
        }
    }

    private async Task HandleReminderCommandAsync(Message message, Models.User user)
    {
        if (!user.IsAdmin)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localization.GetString("AdminOnly", user.LanguageCode));
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
                _localization.GetString("NoUpcomingEvents", user.LanguageCode));
            return;
        }

        var confirmedCount = nextEvent.Attendances.Count(a => a.Status == AttendanceStatus.Yes);

        if (confirmedCount == 0)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localization.GetString("NoConfirmedAttendees", user.LanguageCode, nextEvent.Title));
            return;
        }

        await reminderService.SendEventReminder(nextEvent.Id, "general");

        await _botService.Client.SendMessage(
            message.Chat.Id,
            _localization.GetString("ReminderSent", user.LanguageCode, confirmedCount) + "\n\n" +
            $"*{nextEvent.Title}*\n" +
            $"📅 {nextEvent.DateTime.ToLocalTime():dddd, dd MMMM yyyy}\n" +
            $"🕐 {nextEvent.DateTime.ToLocalTime():HH:mm}",
            parseMode: ParseMode.Markdown);
    }

    private async Task HandleDeleteEventCommandAsync(Message message, Models.User user)
    {
        if (!user.IsAdmin)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localization.GetString("AdminOnly", user.LanguageCode));
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var upcomingEvents = await dbContext.Events
            .Where(e => e.DateTime > DateTime.UtcNow && !e.IsCancelled)
            .OrderBy(e => e.DateTime)
            .Take(10)
            .ToListAsync();

        if (!upcomingEvents.Any())
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localization.GetString("NoEventsToDelete", user.LanguageCode));
            return;
        }

        var buttons = upcomingEvents.Select((evt, index) => new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"{evt.Title} - {evt.DateTime.ToLocalTime():dd/MM HH:mm}",
                $"delete_{evt.Id}")
        }).ToList();

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel", "delete_cancel") });

        var keyboard = new InlineKeyboardMarkup(buttons);

        await _botService.Client.SendMessage(
            message.Chat.Id,
            _localization.GetString("SelectEventToDelete", user.LanguageCode),
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);
    }

    private async Task HandleEditEventCommandAsync(Message message, Models.User user)
    {
        if (!user.IsAdmin)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localization.GetString("AdminOnly", user.LanguageCode));
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var upcomingEvents = await dbContext.Events
            .Where(e => e.DateTime > DateTime.UtcNow && !e.IsCancelled)
            .OrderBy(e => e.DateTime)
            .Take(10)
            .ToListAsync();

        if (!upcomingEvents.Any())
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "📭 No upcoming events to edit.");
            return;
        }

        var buttons = upcomingEvents.Select((evt, index) => new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"{evt.Title} - {evt.DateTime.ToLocalTime():dd/MM HH:mm}",
                $"edit_{evt.Id}")
        }).ToList();

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel", "edit_cancel") });

        var keyboard = new InlineKeyboardMarkup(buttons);

        await _botService.Client.SendMessage(
            message.Chat.Id,
            "📝 *Select an event to edit:*",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);
    }

    private async Task HandleAttendanceCommandAsync(Message message, Models.User user)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        // Get all users
        var allUsers = await dbContext.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .OrderBy(u => u.FullName)
            .ToListAsync();

        // Get upcoming events
        var upcomingEvents = await dbContext.Events
            .Where(e => e.DateTime > DateTime.UtcNow && !e.IsCancelled)
            .OrderBy(e => e.DateTime)
            .Take(10)
            .ToListAsync();

        if (!upcomingEvents.Any())
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "📭 No upcoming events to check attendance.");
            return;
        }

        var buttons = upcomingEvents.Select(evt => new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"{evt.Title} - {evt.DateTime.ToLocalTime():dd/MM HH:mm}",
                $"attendance_view_{evt.Id}")
        }).ToList();

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel", "attendance_cancel") });

        var keyboard = new InlineKeyboardMarkup(buttons);

        await _botService.Client.SendMessage(
            message.Chat.Id,
            "📊 *Select an event to view detailed attendance:*",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);
    }

    private async Task HandleRemindNextCommandAsync(Message message, Models.User user)
    {
        if (!user.IsAdmin)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localization.GetString("AdminOnly", user.LanguageCode));
            return;
        }

        // Set conversation state to expect custom message
        _conversationState.SetUserState(message.From!.Id, "awaiting_reminder_message", null);

        await _botService.Client.SendMessage(
            message.Chat.Id,
            _localization.GetString("CustomReminderPrompt", user.LanguageCode));
    }

    private async Task HandleUnknownCommandAsync(Message message)
    {
        // Get user to determine language
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.TelegramId == message.From!.Id);
        var lang = user?.LanguageCode ?? "en";

        await _botService.Client.SendMessage(
            message.Chat.Id,
            _localization.GetString("UnknownCommand", lang));
    }

    private Task HandleUnknownUpdateAsync(Update update)
    {
        _logger.LogInformation($"Unknown update type: {update.Type}");
        return Task.CompletedTask;
    }

    private async Task HandleConversationStateAsync(Message message, Models.User user, ConversationState state)
    {
        if (state.State == "event_wizard")
        {
            // Handle wizard text input
            using var scope = _serviceProvider.CreateScope();
            var wizard = scope.ServiceProvider.GetRequiredService<EventCreationWizard>();
            await wizard.HandleWizardTextInput(message, user);
        }
        else if (state.State == "song_add_new")
        {
            // Handle new song creation
            using var scope = _serviceProvider.CreateScope();
            var songManager = scope.ServiceProvider.GetRequiredService<SongManager>();
            await songManager.HandleNewSongInput(message, user);
            _conversationState.ClearUserState(message.From!.Id);
        }
        else if (state.State == "song_edit")
        {
            // Handle song edit
            using var scope = _serviceProvider.CreateScope();
            var songManager = scope.ServiceProvider.GetRequiredService<SongManager>();
            await songManager.HandleSongEditInput(message, user, state);
            _conversationState.ClearUserState(message.From!.Id);
        }
        else if (state.State == "chord_add")
        {
            // Handle chord chart addition
            using var scope = _serviceProvider.CreateScope();
            var chordChartService = scope.ServiceProvider.GetRequiredService<ChordChartService>();
            await chordChartService.ProcessChordChartInput(message, user);
        }
        else if (state.State == "awaiting_event_details")
        {
            // Legacy event creation - keep for backward compatibility
            var lines = message.Text?.Split('\n') ?? Array.Empty<string>();

            if (lines.Length < 3)
            {
                await _botService.Client.SendMessage(
                    message.Chat.Id,
                    _localization.GetString("InvalidEventFormat", user.LanguageCode));
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var eventHandler = scope.ServiceProvider.GetRequiredService<WorshipPlannerBot.Api.Handlers.EventHandler>();
            await eventHandler.CreateEventAsync(message, user, lines);

            // Clear conversation state after processing
            _conversationState.ClearUserState(message.From!.Id);
        }
        else if (state.State == "event_edit")
        {
            // Handle event field editing
            await HandleEventEditInput(message, user, state);
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
                    _localization.GetString("NoUpcomingEvents", user.LanguageCode));
            }
            else
            {
                var confirmedCount = nextEvent.Attendances.Count(a => a.Status == AttendanceStatus.Yes);

                if (confirmedCount == 0)
                {
                    await _botService.Client.SendMessage(
                        message.Chat.Id,
                        _localization.GetString("NoConfirmedAttendees", user.LanguageCode, nextEvent.Title));
                }
                else
                {
                    await reminderService.SendCustomReminder(nextEvent.Id, message.Text ?? "");

                    await _botService.Client.SendMessage(
                        message.Chat.Id,
                        _localization.GetString("ReminderSent", user.LanguageCode, confirmedCount) + "\n\n" +
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

    private async Task HandleEventsInGroupAsync(Message message, Models.User user)
    {
        using var scope = _serviceProvider.CreateScope();
        var groupAnnouncementService = scope.ServiceProvider.GetRequiredService<IGroupAnnouncementService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var upcomingEvents = await dbContext.Events
            .Where(e => e.DateTime > DateTime.UtcNow && !e.IsCancelled)
            .OrderBy(e => e.DateTime)
            .Take(3)
            .ToListAsync();

        if (!upcomingEvents.Any())
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localization.GetString("NoUpcomingEvents", user.LanguageCode),
                messageThreadId: message.MessageThreadId);
            return;
        }

        foreach (var evt in upcomingEvents)
        {
            await groupAnnouncementService.SendEventSummaryToGroup(message.Chat.Id, evt, message.MessageThreadId);
        }
    }

    private async Task HandleNextEventInGroupAsync(Message message, Models.User user)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var nextEvent = await dbContext.Events
            .Include(e => e.Attendances)
                .ThenInclude(a => a.User)
                    .ThenInclude(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
            .Include(e => e.SetListItems.OrderBy(si => si.OrderIndex))
                .ThenInclude(si => si.Song)
            .Where(e => e.DateTime > DateTime.UtcNow && !e.IsCancelled)
            .OrderBy(e => e.DateTime)
            .FirstOrDefaultAsync();

        if (nextEvent == null)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localization.GetString("NoUpcomingEvents", user.LanguageCode),
                messageThreadId: message.MessageThreadId);
            return;
        }

        var timeUntilEvent = nextEvent.DateTime - DateTime.UtcNow;
        var timeString = timeUntilEvent.Days > 0
            ? _localization.GetString("TimeDaysHours", user.LanguageCode, timeUntilEvent.Days, timeUntilEvent.Hours)
            : _localization.GetString("TimeHoursMinutes", user.LanguageCode, timeUntilEvent.Hours, timeUntilEvent.Minutes);

        var confirmedCount = nextEvent.Attendances.Count(a => a.Status == AttendanceStatus.Yes);
        var maybeCount = nextEvent.Attendances.Count(a => a.Status == AttendanceStatus.Maybe);

        var messageText = _localization.GetString("NextEvent", user.LanguageCode) + "\n\n" +
                         $"🎵 *{nextEvent.Title}*\n" +
                         $"📅 {nextEvent.DateTime.ToLocalTime():dddd, dd MMMM yyyy}\n" +
                         $"🕐 {nextEvent.DateTime.ToLocalTime():HH:mm}\n" +
                         $"📍 {nextEvent.Location}\n" +
                         $"⏱ {_localization.GetString("TimeIn", user.LanguageCode, timeString)}\n\n";

        // Add setlist if available
        if (nextEvent.SetListItems != null && nextEvent.SetListItems.Any())
        {
            messageText += _localization.GetString("Setlist", user.LanguageCode) + "\n";
            foreach (var item in nextEvent.SetListItems.OrderBy(si => si.OrderIndex))
            {
                if (item.ItemType == Models.Setlist.SetListItemType.Song && item.Song != null)
                {
                    messageText += $"  {item.OrderIndex + 1}. {item.Song.Title}\n";
                }
            }
            messageText += "\n";
        }

        messageText += $"\n👥 *{_localization.GetString("PleaseConfirmAttendance", user.LanguageCode).Replace("✅ ", "")}*\n" +
                      _localization.GetString("ConfirmedLabel", user.LanguageCode, confirmedCount).Replace("*", "").Replace("✅ ", "✅ ") + "\n" +
                      _localization.GetString("MaybeLabel", user.LanguageCode, maybeCount).Replace("*", "").Replace("🤔 ", "🤔 ");

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Yes", $"attend_{nextEvent.Id}_yes"),
                InlineKeyboardButton.WithCallbackData("❌ No", $"attend_{nextEvent.Id}_no"),
                InlineKeyboardButton.WithCallbackData("🤔 Maybe", $"attend_{nextEvent.Id}_maybe")
            }
        });

        await _botService.Client.SendMessage(
            message.Chat.Id,
            messageText,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            messageThreadId: message.MessageThreadId);
    }

    private async Task HandleAttendanceInGroupAsync(Message message, Models.User user)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var nextEvent = await dbContext.Events
            .Include(e => e.Attendances)
                .ThenInclude(a => a.User)
                    .ThenInclude(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
            .Include(e => e.SetListItems.OrderBy(si => si.OrderIndex))
                .ThenInclude(si => si.Song)
            .Where(e => e.DateTime > DateTime.UtcNow && !e.IsCancelled)
            .OrderBy(e => e.DateTime)
            .FirstOrDefaultAsync();

        if (nextEvent == null)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localization.GetString("NoUpcomingEvents", user.LanguageCode),
                messageThreadId: message.MessageThreadId);
            return;
        }

        var confirmedUsers = nextEvent.Attendances
            .Where(a => a.Status == AttendanceStatus.Yes)
            .OrderBy(a => a.User.FullName)
            .ToList();

        var maybeUsers = nextEvent.Attendances
            .Where(a => a.Status == AttendanceStatus.Maybe)
            .OrderBy(a => a.User.FullName)
            .ToList();

        var declinedUsers = nextEvent.Attendances
            .Where(a => a.Status == AttendanceStatus.No)
            .OrderBy(a => a.User.FullName)
            .ToList();

        var messageText = _localization.GetString("AttendanceFor", user.LanguageCode, nextEvent.Title) + "\n" +
                         $"📅 {nextEvent.DateTime.ToLocalTime():dddd, dd MMMM yyyy} at {nextEvent.DateTime.ToLocalTime():HH:mm}\n\n";

        if (confirmedUsers.Any())
        {
            messageText += _localization.GetString("ConfirmedLabel", user.LanguageCode, confirmedUsers.Count) + "\n";
            foreach (var attendance in confirmedUsers)
            {
                var roles = attendance.User.UserRoles.Select(ur => ur.Role.Icon);
                var roleIcons = roles.Any() ? " " + string.Join("", roles) : "";
                messageText += $"• {attendance.User.FullName}{roleIcons}\n";
            }
        }

        if (maybeUsers.Any())
        {
            messageText += "\n" + _localization.GetString("MaybeLabel", user.LanguageCode, maybeUsers.Count) + "\n";
            foreach (var attendance in maybeUsers)
            {
                messageText += $"• {attendance.User.FullName}\n";
            }
        }

        if (declinedUsers.Any())
        {
            messageText += "\n" + _localization.GetString("CantAttendLabel", user.LanguageCode, declinedUsers.Count) + "\n";
            foreach (var attendance in declinedUsers)
            {
                messageText += $"• {attendance.User.FullName}\n";
            }
        }

        // Show needed roles
        var allRoles = await dbContext.Roles.OrderBy(r => r.DisplayOrder).ToListAsync();
        var coveredRoles = confirmedUsers
            .SelectMany(a => a.User.UserRoles.Select(ur => ur.Role))
            .Distinct()
            .ToList();

        var missingRoles = allRoles.Where(r => !coveredRoles.Any(cr => cr.Id == r.Id)).ToList();

        if (missingRoles.Any())
        {
            messageText += "\n" + _localization.GetString("RolesNeeded", user.LanguageCode) + "\n";
            foreach (var role in missingRoles)
            {
                var localizedRoleName = _localization.GetString($"Role.{role.Name.Replace(" ", "")}", user.LanguageCode);
                if (localizedRoleName == $"Role.{role.Name.Replace(" ", "")}")
                {
                    // If no translation found, use original name
                    localizedRoleName = role.Name;
                }
                messageText += $"{role.Icon} {localizedRoleName}\n";
            }
        }

        await _botService.Client.SendMessage(
            message.Chat.Id,
            messageText,
            parseMode: ParseMode.Markdown,
            messageThreadId: message.MessageThreadId);
    }

    private async Task HandleEventEditInput(Message message, Models.User user, ConversationState state)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        dynamic stateData = state.Data!;
        int eventId = stateData.EventId;
        string field = stateData.Field;
        int messageId = stateData.MessageId;
        long chatId = stateData.ChatId;

        var evt = await dbContext.Events
            .Include(e => e.SetListItems)
            .ThenInclude(si => si.Song)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "❌ Event not found.");
            return;
        }

        var input = message.Text?.Trim() ?? "";
        bool success = false;
        string resultMessage = "";

        try
        {
            switch (field)
            {
                case "title":
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        evt.Title = input;
                        evt.UpdatedAt = DateTime.UtcNow;
                        success = true;
                        resultMessage = $"✅ Title updated to: {input}";
                    }
                    break;

                case "date":
                    if (DateTime.TryParseExact(input, "dd/MM/yyyy",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var newDate))
                    {
                        evt.DateTime = new DateTime(newDate.Year, newDate.Month, newDate.Day,
                            evt.DateTime.Hour, evt.DateTime.Minute, 0, DateTimeKind.Utc);
                        evt.UpdatedAt = DateTime.UtcNow;
                        success = true;
                        resultMessage = $"✅ Date updated to: {newDate:dd/MM/yyyy}";
                    }
                    else
                    {
                        resultMessage = "❌ Invalid date format. Please use DD/MM/YYYY";
                    }
                    break;

                case "time":
                    if (DateTime.TryParseExact(input, "HH:mm",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var newTime))
                    {
                        evt.DateTime = new DateTime(evt.DateTime.Year, evt.DateTime.Month, evt.DateTime.Day,
                            newTime.Hour, newTime.Minute, 0, DateTimeKind.Utc);
                        evt.UpdatedAt = DateTime.UtcNow;
                        success = true;
                        resultMessage = $"✅ Time updated to: {newTime:HH:mm}";
                    }
                    else
                    {
                        resultMessage = "❌ Invalid time format. Please use HH:MM";
                    }
                    break;

                case "location":
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        evt.Location = input;
                        evt.UpdatedAt = DateTime.UtcNow;
                        success = true;
                        resultMessage = $"✅ Location updated to: {input}";
                    }
                    break;

                case "description":
                    evt.Description = string.IsNullOrWhiteSpace(input) ? null : input;
                    evt.UpdatedAt = DateTime.UtcNow;
                    success = true;
                    resultMessage = $"✅ Description updated";
                    break;

                case "setlist":
                    if (input.ToLower() == "clear")
                    {
                        // Remove all setlist items
                        dbContext.SetListItems.RemoveRange(evt.SetListItems);
                        success = true;
                        resultMessage = "✅ Setlist cleared";
                    }
                    else
                    {
                        // Parse and update setlist
                        var songTitles = input.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();

                        if (songTitles.Any())
                        {
                            // Remove existing setlist items
                            dbContext.SetListItems.RemoveRange(evt.SetListItems);

                            // Add new songs
                            var orderIndex = 0;
                            foreach (var songTitle in songTitles)
                            {
                                // Check if song exists
                                var song = await dbContext.Songs
                                    .FirstOrDefaultAsync(s => s.Title.ToLower() == songTitle.ToLower());

                                if (song == null)
                                {
                                    // Create new song
                                    song = new Models.Setlist.Song
                                    {
                                        Title = songTitle,
                                        CreatedAt = DateTime.UtcNow
                                    };
                                    dbContext.Songs.Add(song);
                                    await dbContext.SaveChangesAsync();
                                }

                                // Add to setlist
                                var setlistItem = new Models.Setlist.SetListItem
                                {
                                    EventId = evt.Id,
                                    SongId = song.Id,
                                    OrderIndex = orderIndex++,
                                    ItemType = Models.Setlist.SetListItemType.Song
                                };
                                dbContext.SetListItems.Add(setlistItem);
                            }

                            success = true;
                            resultMessage = $"✅ Setlist updated with {songTitles.Count} songs";
                        }
                    }
                    evt.UpdatedAt = DateTime.UtcNow;
                    break;
            }

            if (success)
            {
                await dbContext.SaveChangesAsync();

                // Send success message
                await _botService.Client.SendMessage(
                    message.Chat.Id,
                    resultMessage + "\n\n✨ Event successfully updated!",
                    parseMode: ParseMode.Markdown);

                // Notify attendees about the update if there are any
                var attendees = await dbContext.Attendances
                    .Where(a => a.EventId == evt.Id)
                    .ToListAsync();

                if (attendees.Any())
                {
                    var groupAnnouncementService = scope.ServiceProvider.GetRequiredService<IGroupAnnouncementService>();
                    await groupAnnouncementService.AnnounceEventUpdateToGroups(evt, $"updated_{field}");
                }
            }
            else if (!string.IsNullOrEmpty(resultMessage))
            {
                await _botService.Client.SendMessage(
                    message.Chat.Id,
                    resultMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating event field");
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "❌ An error occurred while updating the event. Please try again.");
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
                LanguageCode = telegramUser.LanguageCode ?? "en",
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

            // Update language if it changed
            if (!string.IsNullOrEmpty(telegramUser.LanguageCode) && user.LanguageCode != telegramUser.LanguageCode)
            {
                user.LanguageCode = telegramUser.LanguageCode;
            }

            await dbContext.SaveChangesAsync();
        }

        return user;
    }
}

public interface IUpdateHandlerService
{
    Task HandleUpdateAsync(Update update);
}