using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WorshipPlannerBot.Api.Data;
using WorshipPlannerBot.Api.Models.Setlist;
using WorshipPlannerBot.Api.Services;

namespace WorshipPlannerBot.Api.Handlers;

public class SongManager
{
    private readonly IBotService _botService;
    private readonly BotDbContext _dbContext;
    private readonly ILocalizationService _localization;
    private readonly ILogger<SongManager> _logger;

    public SongManager(
        IBotService botService,
        BotDbContext dbContext,
        ILocalizationService localization,
        ILogger<SongManager> logger)
    {
        _botService = botService;
        _dbContext = dbContext;
        _localization = localization;
        _logger = logger;
    }

    public async Task<List<Song>> SearchSongs(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            // Return recent/popular songs if no query
            return await _dbContext.Songs
                .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
                .Take(10)
                .ToListAsync();
        }

        // Search by title or artist
        var normalizedQuery = query.ToLower();
        return await _dbContext.Songs
            .Where(s => s.Title.ToLower().Contains(normalizedQuery) ||
                       s.Artist.ToLower().Contains(normalizedQuery))
            .OrderBy(s => s.Title)
            .Take(20)
            .ToListAsync();
    }

    public async Task<Song> CreateOrUpdateSong(string title, string? artist = null, string? key = null, string? tempo = null)
    {
        // Check if song exists
        var existingSong = await _dbContext.Songs
            .FirstOrDefaultAsync(s => s.Title.ToLower() == title.ToLower());

        if (existingSong != null)
        {
            // Update existing song if new info provided
            if (!string.IsNullOrEmpty(artist) && string.IsNullOrEmpty(existingSong.Artist))
                existingSong.Artist = artist;
            if (!string.IsNullOrEmpty(key) && string.IsNullOrEmpty(existingSong.Key))
                existingSong.Key = key;
            if (!string.IsNullOrEmpty(tempo) && string.IsNullOrEmpty(existingSong.Tempo))
                existingSong.Tempo = tempo;

            existingSong.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return existingSong;
        }

        // Create new song
        var newSong = new Song
        {
            Title = title,
            Artist = artist ?? "",
            Key = key ?? "",
            Tempo = tempo ?? "",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Songs.Add(newSong);
        await _dbContext.SaveChangesAsync();
        return newSong;
    }

    public async Task ShowSongLibrary(Message message, Models.User user, int page = 0)
    {
        const int songsPerPage = 10;
        var totalSongs = await _dbContext.Songs.CountAsync();
        var totalPages = (int)Math.Ceiling(totalSongs / (double)songsPerPage);

        if (totalSongs == 0)
        {
            var emptyKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("➕ Adaugă prima cântare", "song_add_new") }
            });

            await _botService.Client.SendMessage(
                message.Chat.Id,
                "📚 *Biblioteca de Cântări*\n\n" +
                "Nu există cântări în bibliotecă încă.\n\n" +
                "Cântările sunt adăugate automat când creați evenimente, " +
                "sau le puteți adăuga manual folosind butonul de mai jos.",
                parseMode: ParseMode.Markdown,
                replyMarkup: emptyKeyboard);
            return;
        }

        var songs = await _dbContext.Songs
            .OrderBy(s => s.Title)
            .Skip(page * songsPerPage)
            .Take(songsPerPage)
            .ToListAsync();

        var text = $"📚 *Biblioteca de Cântări* (Pagina {page + 1}/{totalPages})\n\n";

        var buttons = new List<List<InlineKeyboardButton>>();

        foreach (var song in songs)
        {
            var songDisplay = FormatSongButton(song);
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData(
                    $"🎵 {songDisplay}",
                    $"song_view_{song.Id}")
            });
        }

        // Add navigation buttons
        var navButtons = new List<InlineKeyboardButton>();
        if (page > 0)
            navButtons.Add(InlineKeyboardButton.WithCallbackData("◀️ Înapoi", $"songs_page_{page - 1}"));

        navButtons.Add(InlineKeyboardButton.WithCallbackData("➕ Adaugă", "song_add_new"));

        if (page < totalPages - 1)
            navButtons.Add(InlineKeyboardButton.WithCallbackData("Înainte ▶️", $"songs_page_{page + 1}"));

        buttons.Add(navButtons);

        // Add search button
        buttons.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithSwitchInlineQueryCurrentChat("🔍 Caută cântări", "songs ")
        });

        var keyboard = new InlineKeyboardMarkup(buttons);

        text += $"\n_Total: {totalSongs} cântări_\n";
        text += "_Apasă pe o cântare pentru a vedea/edita detaliile_";

        await _botService.Client.SendMessage(
            message.Chat.Id,
            text,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);
    }

    public async Task<InlineKeyboardMarkup> CreateSongSelectionKeyboard(List<int> selectedSongIds, string searchQuery = "")
    {
        var songs = await SearchSongs(searchQuery);
        var buttons = new List<List<InlineKeyboardButton>>();

        // Add search instruction if no query
        if (string.IsNullOrEmpty(searchQuery))
        {
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithSwitchInlineQueryCurrentChat("🔍 Caută cântări...", "songs ")
            });
        }

        // Add song buttons
        foreach (var song in songs.Take(10))
        {
            var isSelected = selectedSongIds.Contains(song.Id);
            var checkmark = isSelected ? "✅ " : "";
            var songDisplay = FormatSongButton(song);

            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData(
                    checkmark + songDisplay,
                    $"song_toggle_{song.Id}")
            });
        }

        // Add control buttons
        var controlButtons = new List<InlineKeyboardButton>();

        if (selectedSongIds.Any())
        {
            controlButtons.Add(InlineKeyboardButton.WithCallbackData(
                $"✅ Continuă ({selectedSongIds.Count} selectate)",
                "songs_done"));
        }

        controlButtons.Add(InlineKeyboardButton.WithCallbackData(
            "⏭️ Sari peste",
            "songs_skip"));

        buttons.Add(controlButtons);

        // Add back button
        buttons.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("◀️ Înapoi", "wizard_back")
        });

        return new InlineKeyboardMarkup(buttons);
    }

    public async Task ShowSongDetails(CallbackQuery callbackQuery, int songId, Models.User user)
    {
        var song = await _dbContext.Songs.FindAsync(songId);
        if (song == null)
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "Cântarea nu a fost găsită");
            return;
        }

        var text = $"🎵 *{song.Title}*\n\n";

        text += "📋 *Detalii:*\n";
        text += $"👤 Artist: {(string.IsNullOrEmpty(song.Artist) ? "_necompletat_" : song.Artist)}\n";
        text += $"🎹 Tonalitate: {(string.IsNullOrEmpty(song.Key) ? "_necompletat_" : song.Key)}\n";
        text += $"⏱ Tempo: {(string.IsNullOrEmpty(song.Tempo) ? "_necompletat_" : song.Tempo + " BPM")}\n";

        if (!string.IsNullOrEmpty(song.YoutubeUrl))
            text += $"\n🎥 [YouTube]({song.YoutubeUrl})\n";
        if (!string.IsNullOrEmpty(song.ChordSheetUrl))
            text += $"📄 [Acorduri]({song.ChordSheetUrl})\n";

        text += $"\n_Adăugată: {song.CreatedAt.ToLocalTime():dd/MM/yyyy}_";
        if (song.UpdatedAt != null)
            text += $"\n_Modificată: {song.UpdatedAt.Value.ToLocalTime():dd/MM/yyyy}_";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✏️ Editează", $"song_edit_{songId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🗑 Șterge", $"song_delete_{songId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("◀️ Înapoi la listă", "songs_list")
            }
        });

        await _botService.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            text,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            linkPreviewOptions: new Telegram.Bot.Types.LinkPreviewOptions { IsDisabled = true });
    }

    public async Task StartSongEdit(CallbackQuery callbackQuery, int songId)
    {
        var song = await _dbContext.Songs.FindAsync(songId);
        if (song == null) return;

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"📝 Titlu: {song.Title}", $"song_edit_title_{songId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"👤 Artist: {song.Artist ?? "necompletat"}", $"song_edit_artist_{songId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"🎹 Tonalitate: {song.Key ?? "necompletat"}", $"song_edit_key_{songId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"⏱ Tempo: {song.Tempo ?? "necompletat"}", $"song_edit_tempo_{songId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🎥 YouTube Link", $"song_edit_youtube_{songId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📄 Acorduri Link", $"song_edit_chords_{songId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("◀️ Înapoi", $"song_view_{songId}")
            }
        });

        var text = $"✏️ *Editare: {song.Title}*\n\n" +
                  "Selectează ce dorești să editezi:\n\n" +
                  "_Apasă pe un câmp pentru a-l modifica_";

        await _botService.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            text,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);
    }

    public async Task PromptSongFieldEdit(CallbackQuery callbackQuery, int songId, string field)
    {
        var song = await _dbContext.Songs.FindAsync(songId);
        if (song == null) return;

        var promptText = field switch
        {
            "title" => $"📝 *Editare Titlu*\n\nTitlu actual: {song.Title}\n\nTrimite noul titlu:",
            "artist" => $"👤 *Editare Artist*\n\nArtist actual: {song.Artist ?? "necompletat"}\n\nTrimite numele artistului:",
            "key" => $"🎹 *Editare Tonalitate*\n\nTonalitate actuală: {song.Key ?? "necompletat"}\n\nTrimite tonalitatea (ex: C, G, Am, D#):",
            "tempo" => $"⏱ *Editare Tempo*\n\nTempo actual: {song.Tempo ?? "necompletat"}\n\nTrimite tempo-ul în BPM (ex: 120):",
            "youtube" => $"🎥 *Editare YouTube Link*\n\nLink actual: {song.YoutubeUrl ?? "necompletat"}\n\nTrimite link-ul YouTube:",
            "chords" => $"📄 *Editare Link Acorduri*\n\nLink actual: {song.ChordSheetUrl ?? "necompletat"}\n\nTrimite link-ul pentru acorduri:",
            _ => null
        };

        if (promptText != null)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("❌ Anulează", $"song_edit_{songId}") }
            });

            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                promptText,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);

            // Store edit state in conversation
            var editState = new { SongId = songId, Field = field, MessageId = callbackQuery.Message.MessageId };
            // This would need IConversationStateService injection and usage
        }
    }

    public async Task DeleteSong(CallbackQuery callbackQuery, int songId)
    {
        var song = await _dbContext.Songs
            .Include(s => s.SetListItems)
            .FirstOrDefaultAsync(s => s.Id == songId);

        if (song == null)
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "Cântarea nu a fost găsită");
            return;
        }

        // Check if song is used in any setlists
        if (song.SetListItems.Any())
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⚠️ Șterge oricum", $"song_delete_confirm_{songId}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("◀️ Anulează", $"song_view_{songId}")
                }
            });

            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                $"⚠️ *Atenție!*\n\n" +
                $"Cântarea *{song.Title}* este folosită în {song.SetListItems.Count} evenimente.\n\n" +
                $"Ești sigur că vrei să o ștergi?",
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
        else
        {
            _dbContext.Songs.Remove(song);
            await _dbContext.SaveChangesAsync();

            await _botService.Client.EditMessageText(
                callbackQuery.Message!.Chat.Id,
                callbackQuery.Message.MessageId,
                $"✅ Cântarea *{song.Title}* a fost ștearsă.",
                parseMode: ParseMode.Markdown);

            // Return to song list after a moment
            await Task.Delay(2000);
            await ShowSongLibraryInline(callbackQuery, 0);
        }
    }

    private async Task ShowSongLibraryInline(CallbackQuery callbackQuery, int page)
    {
        const int songsPerPage = 10;
        var totalSongs = await _dbContext.Songs.CountAsync();
        var totalPages = (int)Math.Ceiling(totalSongs / (double)songsPerPage);

        var songs = await _dbContext.Songs
            .OrderBy(s => s.Title)
            .Skip(page * songsPerPage)
            .Take(songsPerPage)
            .ToListAsync();

        var text = $"📚 *Biblioteca de Cântări* (Pagina {page + 1}/{totalPages})\n\n";

        var buttons = new List<List<InlineKeyboardButton>>();

        foreach (var song in songs)
        {
            var songDisplay = FormatSongButton(song);
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData(
                    $"🎵 {songDisplay}",
                    $"song_view_{song.Id}")
            });
        }

        // Add navigation buttons
        var navButtons = new List<InlineKeyboardButton>();
        if (page > 0)
            navButtons.Add(InlineKeyboardButton.WithCallbackData("◀️ Înapoi", $"songs_page_{page - 1}"));

        navButtons.Add(InlineKeyboardButton.WithCallbackData("➕ Adaugă", "song_add_new"));

        if (page < totalPages - 1)
            navButtons.Add(InlineKeyboardButton.WithCallbackData("Înainte ▶️", $"songs_page_{page + 1}"));

        buttons.Add(navButtons);

        var keyboard = new InlineKeyboardMarkup(buttons);

        text += $"\n_Total: {totalSongs} cântări_\n";
        text += "_Apasă pe o cântare pentru a vedea/edita detaliile_";

        await _botService.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            text,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);
    }

    private string FormatSongInfo(Song song)
    {
        var info = $"• *{song.Title}*";
        var details = new List<string>();

        if (!string.IsNullOrEmpty(song.Artist))
            details.Add(song.Artist);
        if (!string.IsNullOrEmpty(song.Key))
            details.Add(song.Key);
        if (!string.IsNullOrEmpty(song.Tempo))
            details.Add($"{song.Tempo} BPM");

        if (details.Any())
            info += $" ({string.Join(", ", details)})";

        return info;
    }

    private string FormatSongButton(Song song)
    {
        var display = song.Title;
        if (!string.IsNullOrEmpty(song.Key))
            display += $" ({song.Key})";
        return display;
    }

    public async Task HandleNewSongInput(Message message, Models.User user)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                await _botService.Client.SendMessage(
                    message.Chat.Id,
                    "❌ Textul nu poate fi gol.");
                return;
            }

            // Parse input: Title | Key | Tempo
            var parts = message.Text.Split('|').Select(p => p.Trim()).ToArray();
            var title = parts[0];

            if (string.IsNullOrWhiteSpace(title))
            {
                await _botService.Client.SendMessage(
                    message.Chat.Id,
                    "❌ Titlul cântării este obligatoriu.");
                return;
            }

            string? key = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : null;
            string? tempo = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : null;

            // Create the song
            var song = await CreateOrUpdateSong(title, null, key, tempo);

            // Show success message
            var successMessage = $"✅ *Cântare adăugată cu succes!*\n\n" +
                               $"🎵 *{song.Title}*";

            if (!string.IsNullOrEmpty(song.Key))
                successMessage += $"\n🎹 Tonalitate: {song.Key}";

            if (!string.IsNullOrEmpty(song.Tempo))
                successMessage += $"\n⏱ Tempo: {song.Tempo} BPM";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📚 Vezi biblioteca", "songs_list"),
                    InlineKeyboardButton.WithCallbackData("➕ Adaugă altă cântare", "song_add_new")
                }
            });

            await _botService.Client.SendMessage(
                message.Chat.Id,
                successMessage,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling new song input");
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "❌ A apărut o eroare la adăugarea cântării. Vă rugăm încercați din nou.");
        }
    }

    public async Task HandleSongEditInput(Message message, Models.User user, ConversationState state)
    {
        try
        {
            if (state.Data == null || string.IsNullOrWhiteSpace(message.Text))
            {
                await _botService.Client.SendMessage(
                    message.Chat.Id,
                    "❌ Date invalide. Vă rugăm încercați din nou.");
                return;
            }

            // Extract data from state
            dynamic stateData = state.Data;
            int songId = stateData.SongId;
            string field = stateData.Field;
            int messageId = stateData.MessageId;

            var song = await _dbContext.Songs.FindAsync(songId);
            if (song == null)
            {
                await _botService.Client.SendMessage(
                    message.Chat.Id,
                    "❌ Cântarea nu a fost găsită.");
                return;
            }

            // Update the appropriate field
            switch (field)
            {
                case "title":
                    song.Title = message.Text.Trim();
                    break;
                case "artist":
                    song.Artist = string.IsNullOrWhiteSpace(message.Text) ? null : message.Text.Trim();
                    break;
                case "key":
                    song.Key = string.IsNullOrWhiteSpace(message.Text) ? null : message.Text.Trim();
                    break;
                case "tempo":
                    song.Tempo = string.IsNullOrWhiteSpace(message.Text) ? null : message.Text.Trim();
                    break;
            }

            song.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // Delete the user's message
            try
            {
                await _botService.Client.DeleteMessage(message.Chat.Id, message.MessageId);
            }
            catch (Exception deleteEx)
            {
                _logger.LogWarning(deleteEx, "Failed to delete user message");
            }

            // Update the original message with song details
            var updatedMessage = await BuildSongDetailsMessage(song);
            var updatedKeyboard = CreateSongDetailsKeyboard(song.Id);

            try
            {
                await _botService.Client.EditMessageText(
                    message.Chat.Id,
                    messageId,
                    updatedMessage,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: updatedKeyboard);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating message after song edit");
                // If edit fails, send a new message
                await _botService.Client.SendMessage(
                    message.Chat.Id,
                    updatedMessage,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: updatedKeyboard);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling song edit input");
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "❌ A apărut o eroare la editarea cântării. Vă rugăm încercați din nou.");
        }
    }

    private Task<string> BuildSongDetailsMessage(Song song)
    {
        var message = $"🎵 *{song.Title}*\n\n";

        if (!string.IsNullOrEmpty(song.Artist))
            message += $"👤 Artist: {song.Artist}\n";
        if (!string.IsNullOrEmpty(song.Key))
            message += $"🎹 Tonalitate: {song.Key}\n";
        if (!string.IsNullOrEmpty(song.Tempo))
            message += $"⏱ Tempo: {song.Tempo} BPM\n";

        message += $"\n📅 Creată: {song.CreatedAt:dd.MM.yyyy}";
        if (song.UpdatedAt.HasValue)
            message += $"\n✏️ Modificată: {song.UpdatedAt.Value:dd.MM.yyyy}";

        return Task.FromResult(message);
    }

    private InlineKeyboardMarkup CreateSongDetailsKeyboard(int songId)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✏️ Editează", $"song_edit_{songId}"),
                InlineKeyboardButton.WithCallbackData("🗑 Șterge", $"song_delete_{songId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("« Înapoi", "songs_list")
            }
        });
    }
}