using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WorshipPlannerBot.Api.Data;
using WorshipPlannerBot.Api.Models;
using WorshipPlannerBot.Api.Models.Setlist;
using WorshipPlannerBot.Api.Services;

namespace WorshipPlannerBot.Api.Handlers;

public class EventCreationWizard
{
    private readonly IBotService _botService;
    private readonly BotDbContext _dbContext;
    private readonly IConversationStateService _conversationState;
    private readonly IGroupAnnouncementService _groupAnnouncementService;
    private readonly ILocalizationService _localization;
    private readonly ILogger<EventCreationWizard> _logger;
    private readonly SongManager _songManager;

    public EventCreationWizard(
        IBotService botService,
        BotDbContext dbContext,
        IConversationStateService conversationState,
        IGroupAnnouncementService groupAnnouncementService,
        ILocalizationService localization,
        ILogger<EventCreationWizard> logger,
        SongManager songManager)
    {
        _botService = botService;
        _dbContext = dbContext;
        _conversationState = conversationState;
        _groupAnnouncementService = groupAnnouncementService;
        _localization = localization;
        _logger = logger;
        _songManager = songManager;
    }

    public async Task StartWizard(Message message, Models.User user)
    {
        // Initialize wizard state
        var wizardState = new EventWizardState
        {
            UserId = user.Id,
            CurrentStep = WizardStep.SelectTemplate,
            CreatedAt = DateTime.UtcNow
        };

        _conversationState.SetUserState(message.From!.Id, "event_wizard", wizardState);

        // Show template selection
        await ShowTemplateSelection(message, user);
    }

    private async Task ShowTemplateSelection(Message message, Models.User user)
    {
        var lang = user.LanguageCode;

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🙏 " + _localization.GetString("WizardTemplateSunday", lang), "wizard_template_sunday")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🎸 " + _localization.GetString("WizardTemplateRehearsal", lang), "wizard_template_rehearsal")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(_localization.GetString("ButtonCancel", lang), "wizard_cancel")
            }
        });

        var text = "📅 *Creează eveniment nou*\n\n" +
                  "Selectează unul din aceste 2";

        await _botService.Client.SendMessage(
            message.Chat.Id,
            text,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);
    }

    public async Task HandleWizardCallback(CallbackQuery callbackQuery, Models.User user, string data)
    {
        var state = _conversationState.GetUserState(callbackQuery.From.Id);
        if (state?.State != "event_wizard" || state.Data is not EventWizardState wizardState)
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "Session expired. Please start again with /newevent");
            return;
        }

        // Handle cancel
        if (data == "wizard_cancel")
        {
            _conversationState.ClearUserState(callbackQuery.From.Id);
            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                "❌ Event creation cancelled.");
            return;
        }

        // Handle song-related callbacks regardless of step (they can happen during AddSongs step)
        if (data.StartsWith("song_toggle_") || data == "songs_done" || data == "songs_skip")
        {
            await HandleSongSelection(callbackQuery, user, wizardState, data);
            return;
        }

        // Route based on current step
        switch (wizardState.CurrentStep)
        {
            case WizardStep.SelectTemplate:
                await HandleTemplateSelection(callbackQuery, user, wizardState, data);
                break;
            case WizardStep.SelectDate:
                await HandleDateSelection(callbackQuery, user, wizardState, data);
                break;
            case WizardStep.SelectTime:
                await HandleTimeSelection(callbackQuery, user, wizardState, data);
                break;
            case WizardStep.SelectLocation:
                await HandleLocationSelection(callbackQuery, user, wizardState, data);
                break;
            case WizardStep.AddSongs:
                await HandleSongSelection(callbackQuery, user, wizardState, data);
                break;
            case WizardStep.Confirm:
                await HandleConfirmation(callbackQuery, user, wizardState, data);
                break;
        }
    }

    private async Task HandleTemplateSelection(CallbackQuery callbackQuery, Models.User user, EventWizardState state, string data)
    {
        var template = data.Replace("wizard_template_", "");

        // Apply template defaults
        switch (template)
        {
            case "sunday":
                state.Title = "Serviciu";
                state.DefaultTime = "08:30";
                state.Location = "Biserică - sala mică";
                state.Description = "Serviciu de închinare";
                break;
            case "rehearsal":
                state.Title = "Repetiție";
                state.DefaultTime = "19:00";
                state.Location = "Biserică - sala mică";
                state.Description = "Repetiție cu grupul de laudă";
                break;
            default:
                // For any other value, default to Sunday
                state.Title = "Serviciu";
                state.DefaultTime = "08:30";
                state.Location = "Biserica - sala mică";
                state.Description = "Serviciu de închinared";
                break;
        }

        state.Template = template;
        state.CurrentStep = WizardStep.SelectDate;
        await ShowDateSelection(callbackQuery, user, state);
    }

    private async Task ShowDateSelection(CallbackQuery? callbackQuery, Models.User user, EventWizardState state, Message? message = null)
    {
        var today = DateTime.Today;
        var buttons = new List<InlineKeyboardButton[]>();

        // Show next 14 days
        for (int i = 0; i < 14; i += 2)
        {
            var date1 = today.AddDays(i);
            var date2 = today.AddDays(i + 1);

            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{date1:ddd, MMM d}",
                    $"wizard_date_{date1:yyyy-MM-dd}"),
                InlineKeyboardButton.WithCallbackData(
                    $"{date2:ddd, MMM d}",
                    $"wizard_date_{date2:yyyy-MM-dd}")
            });
        }

        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("📅 Altă dată", "wizard_date_custom")
        });

        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("◀️ Înapoi", "wizard_back"),
            InlineKeyboardButton.WithCallbackData("❌ Anulare", "wizard_cancel")
        });

        var keyboard = new InlineKeyboardMarkup(buttons);

        var text = $"📅 *Alege data*\n\nEveniment: *{state.Title}*\n\nAlege data:";

        if (callbackQuery != null)
        {
            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
        else if (message != null)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
    }

    private async Task HandleDateSelection(CallbackQuery callbackQuery, Models.User user, EventWizardState state, string data)
    {
        if (data == "wizard_back")
        {
            state.CurrentStep = WizardStep.SelectTemplate;
            await ShowTemplateSelection(callbackQuery.Message!, user);
            return;
        }

        if (data == "wizard_date_custom")
        {
            state.CurrentStep = WizardStep.EnterDate;
            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                "📅 *Alege data*\n\nScrie data în formatul următor: DD/MM/YYYY");
            return;
        }

        var dateStr = data.Replace("wizard_date_", "");
        if (DateTime.TryParse(dateStr, out var date))
        {
            state.Date = date;
            state.CurrentStep = WizardStep.SelectTime;
            await ShowTimeSelection(callbackQuery, user, state);
        }
    }

    private async Task ShowTimeSelection(CallbackQuery? callbackQuery, Models.User user, EventWizardState state, Message? message = null)
    {
        var commonTimes = new[]
        {
            "08:00","09:00", "09:30", "10:00", "10:30",
            "11:00", "14:00", "15:00", "16:00",
            "17:00", "18:00", "18:30", "19:00", "19:30",
        };

        var buttons = new List<InlineKeyboardButton[]>();

        // Create rows of 4 time buttons
        for (int i = 0; i < commonTimes.Length; i += 4)
        {
            var row = new List<InlineKeyboardButton>();
            for (int j = 0; j < 4 && i + j < commonTimes.Length; j++)
            {
                var time = commonTimes[i + j];
                var isDefault = time == state.DefaultTime;
                var label = isDefault ? $"⭐ {time}" : time;
                row.Add(InlineKeyboardButton.WithCallbackData(label, $"wizard_time_{time}"));
            }
            buttons.Add(row.ToArray());
        }

        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("🕐 Altă oră", "wizard_time_custom")
        });

        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("◀️ Înapoi", "wizard_back"),
            InlineKeyboardButton.WithCallbackData("❌ Anulare", "wizard_cancel")
        });

        var keyboard = new InlineKeyboardMarkup(buttons);

        var text = $"🕐 *Alege ora*\n\n" +
                  $"Eveniment: *{state.Title}*\n" +
                  $"Data: *{state.Date:dddd, dd MMMM yyyy}*\n\nAlege timpul:";

        if (callbackQuery != null)
        {
            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
        else if (message != null)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
    }

    private async Task HandleTimeSelection(CallbackQuery callbackQuery, Models.User user, EventWizardState state, string data)
    {
        if (data == "wizard_back")
        {
            state.CurrentStep = WizardStep.SelectDate;
            await ShowDateSelection(callbackQuery, user, state);
            return;
        }

        if (data == "wizard_time_custom")
        {
            state.CurrentStep = WizardStep.EnterTime;
            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                "🕐 *Enter Time*\n\nScrie timpul în formatul următor: HH:MM (format 24-ore)");
            return;
        }

        var timeStr = data.Replace("wizard_time_", "");
        state.Time = timeStr;
        state.CurrentStep = WizardStep.SelectLocation;
        await ShowLocationSelection(callbackQuery, user, state);
    }

    private async Task ShowLocationSelection(CallbackQuery? callbackQuery, Models.User user, EventWizardState state, Message? message = null)
    {
        // Get common locations from past events
        var commonLocations = await _dbContext.Events
            .Where(e => !string.IsNullOrEmpty(e.Location))
            .GroupBy(e => e.Location)
            .Select(g => new { Location = g.Key, Count = g.Count() })
            .OrderByDescending(l => l.Count)
            .Take(6)
            .Select(l => l.Location)
            .ToListAsync();

        var buttons = new List<InlineKeyboardButton[]>();

        // Add default location if set
        if (!string.IsNullOrEmpty(state.Location))
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData($"⭐ {state.Location}", $"wizard_location_{state.Location ?? "default"}")
            });
        }

        // Add common locations
        foreach (var location in commonLocations.Where(l => l != state.Location))
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(location, $"wizard_location_{location}")
            });
        }

        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("📍 Custom Location", "wizard_location_custom")
        });

        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("◀️ Înapoi", "wizard_back"),
            InlineKeyboardButton.WithCallbackData("❌ Anulare", "wizard_cancel")
        });

        var keyboard = new InlineKeyboardMarkup(buttons);

        var text = $"📍 *Alege locația*\n\n" +
                  $"Eveniment: *{state.Title}*\n" +
                  $"Data: *{state.Date:dddd, dd MMMM yyyy}*\n" +
                  $"Ora: *{state.Time}*\n\n" +
                  $"Alege locația:";

        if (callbackQuery != null)
        {
            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
        else if (message != null)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
    }

    private async Task HandleLocationSelection(CallbackQuery callbackQuery, Models.User user, EventWizardState state, string data)
    {
        if (data == "wizard_back")
        {
            state.CurrentStep = WizardStep.SelectTime;
            await ShowTimeSelection(callbackQuery, user, state);
            return;
        }

        if (data == "wizard_location_custom")
        {
            state.CurrentStep = WizardStep.EnterLocation;
            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                "📍 *Alege locația*\n\nAlege locația pentru eveniment:");
            return;
        }

        var location = data.Replace("wizard_location_", "");
        state.Location = location;
        state.CurrentStep = WizardStep.AddSongs;
        await ShowSongOptions(callbackQuery, user, state);
    }

    private async Task ShowSongOptions(CallbackQuery? callbackQuery, Models.User user, EventWizardState state, Message? message = null)
    {
        // Create keyboard with song selection
        var selectedSongIds = state.SelectedSongIds ?? new List<int>();
        var keyboard = await _songManager.CreateSongSelectionKeyboard(selectedSongIds);

        var text = $"🎶 *Selectează cântări*\n\n" +
                  $"Eveniment: *{state.Title}*\n" +
                  $"Data: *{state.Date:dddd, dd MMMM yyyy}*\n" +
                  $"Ora: *{state.Time}*\n" +
                  $"Locația: *{state.Location}*\n\n";

        if (selectedSongIds.Any())
        {
            text += $"✅ *Cântări selectate: {selectedSongIds.Count}*\n";

            // Show names of selected songs
            var selectedSongs = await _dbContext.Songs
                .Where(s => selectedSongIds.Contains(s.Id))
                .OrderBy(s => s.Title)
                .ToListAsync();

            foreach (var song in selectedSongs)
            {
                text += $"   • {song.Title}";
                if (!string.IsNullOrEmpty(song.Key))
                    text += $" ({song.Key})";
                text += "\n";
            }
            text += "\n";
        }
        else
        {
            text += "_Nicio cântare selectată încă_\n\n";
        }

        text += "Apasă pe cântări pentru a le selecta/deselecta:";

        if (callbackQuery != null)
        {
            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
        else if (message != null)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                text,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
    }

    private async Task HandleSongSelection(CallbackQuery callbackQuery, Models.User user, EventWizardState state, string data)
    {
        if (data == "wizard_back")
        {
            state.CurrentStep = WizardStep.SelectLocation;
            await ShowLocationSelection(callbackQuery, user, state);
            return;
        }

        if (data == "songs_skip" || data == "wizard_songs_skip")
        {
            state.CurrentStep = WizardStep.Confirm;
            await ShowConfirmation(callbackQuery, user, state);
            return;
        }

        if (data == "songs_done")
        {
            // Convert selected song IDs to song names for display
            if (state.SelectedSongIds != null && state.SelectedSongIds.Any())
            {
                var songs = await _dbContext.Songs
                    .Where(s => state.SelectedSongIds.Contains(s.Id))
                    .ToListAsync();
                state.Songs = songs.Select(s => s.Title).ToList();
            }
            state.CurrentStep = WizardStep.Confirm;
            await ShowConfirmation(callbackQuery, user, state);
            return;
        }

        // Handle song toggle
        if (data.StartsWith("song_toggle_"))
        {
            var songIdStr = data.Replace("song_toggle_", "");
            if (int.TryParse(songIdStr, out var songId))
            {
                state.SelectedSongIds ??= new List<int>();

                if (state.SelectedSongIds.Contains(songId))
                {
                    state.SelectedSongIds.Remove(songId);

                    // Get song name for feedback
                    var song = await _dbContext.Songs.FindAsync(songId);
                    await _botService.Client.AnswerCallbackQuery(
                        callbackQuery.Id,
                        $"❌ {song?.Title ?? "Cântare"} deselectată");
                }
                else
                {
                    state.SelectedSongIds.Add(songId);

                    // Get song name for feedback
                    var song = await _dbContext.Songs.FindAsync(songId);
                    await _botService.Client.AnswerCallbackQuery(
                        callbackQuery.Id,
                        $"✅ {song?.Title ?? "Cântare"} selectată");
                }

                // Update the state in conversation service
                _conversationState.SetUserState(callbackQuery.From.Id, "event_wizard", state);

                // Refresh the song selection keyboard
                await ShowSongOptions(callbackQuery, user, state);
            }
            return;
        }

        if (data == "wizard_songs_add")
        {
            state.CurrentStep = WizardStep.EnterSongs;
            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                "🎵 *Adaugă cântări noi*\n\n" +
                "Introdu cântări în formatul:\n" +
                "Titlu | Tonalitate | Tempo\n\n" +
                "Exemplu:\n" +
                "Amazing Grace | G | 72\n" +
                "How Great Is Our God | C | 80\n" +
                "10,000 Reasons | G | 73\n\n" +
                "Sau doar titlul:\n" +
                "Amazing Grace\n\n" +
                "Scrie done sau skip când termini");
            return;
        }
    }

    private async Task ShowConfirmation(CallbackQuery? callbackQuery, Models.User user, EventWizardState state, Message? message = null)
    {
        var summary = $"✅ *Confirmă crearea evenimentului*\n\n" +
                     $"📝 Titlu: *{state.Title}*\n" +
                     $"📅 Data: *{state.Date:dddd, dd MMMM yyyy}*\n" +
                     $"🕐 Ora: *{state.Time}*\n" +
                     $"📍 Locația: *{state.Location}*\n";

        if (!string.IsNullOrEmpty(state.Description))
            summary += $"📄 Descriere: {state.Description}\n";

        // Display songs with details
        if (state.SelectedSongIds != null && state.SelectedSongIds.Any())
        {
            var songs = await _dbContext.Songs
                .Where(s => state.SelectedSongIds.Contains(s.Id))
                .ToListAsync();

            if (songs.Any())
            {
                summary += $"\n🎶 *Lista de cântări:*\n";
                for (int i = 0; i < songs.Count; i++)
                {
                    var song = songs[i];
                    summary += $"  {i + 1}. {song.Title}";

                    var details = new List<string>();
                    if (!string.IsNullOrEmpty(song.Key))
                        details.Add(song.Key);
                    if (!string.IsNullOrEmpty(song.Tempo))
                        details.Add($"{song.Tempo} BPM");

                    if (details.Any())
                        summary += $" ({string.Join(", ", details)})";

                    summary += "\n";
                }
            }
        }
        else if (state.Songs != null && state.Songs.Any())
        {
            summary += $"\n🎶 *Lista de cântări:*\n";
            for (int i = 0; i < state.Songs.Count; i++)
            {
                summary += $"  {i + 1}. {state.Songs[i]}\n";
            }
        }

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Create Event", "wizard_confirm_yes")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✏️ Edit", "wizard_confirm_edit"),
                InlineKeyboardButton.WithCallbackData("❌ Cancel", "wizard_cancel")
            }
        });

        if (callbackQuery != null)
        {
            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                summary,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
        else if (message != null)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                summary,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
    }

    private async Task HandleConfirmation(CallbackQuery callbackQuery, Models.User user, EventWizardState state, string data)
    {
        if (data == "wizard_confirm_edit")
        {
            // Go back to template selection to start over
            state.CurrentStep = WizardStep.SelectTemplate;
            await ShowTemplateSelection(callbackQuery.Message!, user);
            return;
        }

        if (data == "wizard_confirm_yes")
        {
            await CreateEvent(callbackQuery, user, state);
        }
    }

    private async Task CreateEvent(CallbackQuery callbackQuery, Models.User user, EventWizardState state)
    {
        try
        {
            // Parse date and time
            var dateTimeStr = $"{state.Date:yyyy-MM-dd} {state.Time}";
            if (!DateTime.TryParseExact(dateTimeStr, "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var eventDateTime))
            {
                await _botService.Client.EditMessageText(
                    callbackQuery.Message!.Chat.Id,
                    callbackQuery.Message.MessageId,
                    "❌ Error parsing date/time. Please try again.");
                return;
            }

            // Create event
            var evt = new Event
            {
                Title = state.Title!,
                Description = state.Description,
                DateTime = eventDateTime.ToUniversalTime(),
                Location = state.Location!,
                CreatedByUserId = user.Id,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Events.Add(evt);
            await _dbContext.SaveChangesAsync();

            // Add songs if provided
            if (state.SelectedSongIds != null && state.SelectedSongIds.Any())
            {
                var songs = await _dbContext.Songs
                    .Where(s => state.SelectedSongIds.Contains(s.Id))
                    .ToListAsync();

                for (int i = 0; i < songs.Count; i++)
                {
                    var setlistItem = new SetListItem
                    {
                        EventId = evt.Id,
                        SongId = songs[i].Id,
                        OrderIndex = i,
                        ItemType = SetListItemType.Song
                    };

                    _dbContext.SetListItems.Add(setlistItem);
                }

                await _dbContext.SaveChangesAsync();
            }
            else if (state.Songs != null && state.Songs.Any())
            {
                // Fallback for text-entered songs
                for (int i = 0; i < state.Songs.Count; i++)
                {
                    var song = await _dbContext.Songs
                        .FirstOrDefaultAsync(s => s.Title == state.Songs[i]);

                    if (song == null)
                    {
                        song = new Song
                        {
                            Title = state.Songs[i],
                            CreatedAt = DateTime.UtcNow
                        };
                        _dbContext.Songs.Add(song);
                        await _dbContext.SaveChangesAsync();
                    }

                    var setlistItem = new SetListItem
                    {
                        EventId = evt.Id,
                        SongId = song.Id,
                        OrderIndex = i,
                        ItemType = SetListItemType.Song
                    };

                    _dbContext.SetListItems.Add(setlistItem);
                }

                await _dbContext.SaveChangesAsync();
            }

            // Clear conversation state
            _conversationState.ClearUserState(callbackQuery.From.Id);

            // Send success message
            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                $"✅ Eveniment creat cu succes!\n\n" +
                $"*{evt.Title}*\n" +
                $"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy}\n" +
                $"🕐 {evt.DateTime.ToLocalTime():HH:mm}\n" +
                $"📍 {evt.Location}",
                parseMode: ParseMode.Markdown);

            // Announce to groups
            await _groupAnnouncementService.AnnounceNewEventToGroups(evt, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event");
            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                "❌ Error creating event. Please try again.");
        }
    }

    public async Task HandleWizardTextInput(Message message, Models.User user)
    {
        var state = _conversationState.GetUserState(message.From!.Id);
        if (state?.State != "event_wizard" || state.Data is not EventWizardState wizardState)
            return;

        var text = message.Text ?? "";

        switch (wizardState.CurrentStep)
        {
            case WizardStep.EnterTitle:
                wizardState.Title = text;
                wizardState.CurrentStep = WizardStep.SelectDate;
                await ShowDateSelection(null, user, wizardState, message);
                break;

            case WizardStep.EnterDate:
                if (DateTime.TryParseExact(text, "dd/MM/yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    wizardState.Date = date;
                    wizardState.CurrentStep = WizardStep.SelectTime;
                    await ShowTimeSelection(null, user, wizardState, message);
                }
                else
                {
                    await _botService.Client.SendMessage(
                        message.Chat.Id,
                        "❌ Invalid date format. Please use DD/MM/YYYY");
                }
                break;

            case WizardStep.EnterTime:
                if (TimeSpan.TryParseExact(text, @"hh\:mm",
                    CultureInfo.InvariantCulture, out var time))
                {
                    wizardState.Time = text;
                    wizardState.CurrentStep = WizardStep.SelectLocation;
                    await ShowLocationSelection(null, user, wizardState, message);
                }
                else
                {
                    await _botService.Client.SendMessage(
                        message.Chat.Id,
                        "❌ Invalid time format. Please use HH:MM (24-hour format)");
                }
                break;

            case WizardStep.EnterLocation:
                wizardState.Location = text;
                wizardState.CurrentStep = WizardStep.AddSongs;
                await ShowSongOptions(null, user, wizardState, message);
                break;

            case WizardStep.EnterSongs:
                if (text.ToLower() == "done" || text.ToLower() == "skip")
                {
                    wizardState.CurrentStep = WizardStep.Confirm;
                    await ShowConfirmation(null, user, wizardState, message);
                }
                else
                {
                    // Parse songs with optional key and tempo
                    var songLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var songTitles = new List<string>();
                    wizardState.SelectedSongIds ??= new List<int>();

                    foreach (var line in songLines)
                    {
                        var parts = line.Split('|').Select(p => p.Trim()).ToArray();
                        var title = parts[0];
                        var key = parts.Length > 1 ? parts[1] : null;
                        var tempo = parts.Length > 2 ? parts[2] : null;

                        if (!string.IsNullOrEmpty(title))
                        {
                            var song = await _songManager.CreateOrUpdateSong(title, null, key, tempo);
                            wizardState.SelectedSongIds.Add(song.Id);
                            songTitles.Add(title);
                        }
                    }

                    wizardState.Songs = songTitles;
                    wizardState.CurrentStep = WizardStep.Confirm;
                    await ShowConfirmation(null, user, wizardState, message);
                }
                break;
        }
    }
}

public class EventWizardState
{
    public int UserId { get; set; }
    public WizardStep CurrentStep { get; set; }
    public string? Template { get; set; }
    public string? Title { get; set; }
    public DateTime Date { get; set; }
    public string? Time { get; set; }
    public string? DefaultTime { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public List<string>? Songs { get; set; }
    public List<int>? SelectedSongIds { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum WizardStep
{
    SelectTemplate,
    EnterTitle,
    SelectDate,
    EnterDate,
    SelectTime,
    EnterTime,
    SelectLocation,
    EnterLocation,
    AddSongs,
    EnterSongs,
    Confirm
}