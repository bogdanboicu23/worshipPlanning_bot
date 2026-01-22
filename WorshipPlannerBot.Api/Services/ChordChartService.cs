using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WorshipPlannerBot.Api.Data;
using WorshipPlannerBot.Api.Models.Setlist;

namespace WorshipPlannerBot.Api.Services;

public class ChordChartService
{
    private readonly IBotService _botService;
    private readonly BotDbContext _dbContext;
    private readonly IConversationStateService _conversationState;
    private readonly ILogger<ChordChartService> _logger;

    public ChordChartService(
        IBotService botService,
        BotDbContext dbContext,
        IConversationStateService conversationState,
        ILogger<ChordChartService> logger)
    {
        _botService = botService;
        _dbContext = dbContext;
        _conversationState = conversationState;
        _logger = logger;
    }

    public async Task ShowChordChartMenu(Message message, Models.User user)
    {
        var songs = await _dbContext.Songs
            .Include(s => s.ChordCharts)
            .OrderBy(s => s.Title)
            .ToListAsync();

        if (!songs.Any())
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                "📭 No songs found. Add songs first using /songs command.");
            return;
        }

        var buttons = new List<InlineKeyboardButton[]>();

        // Show songs with chord chart indicators
        foreach (var song in songs.Take(10))
        {
            var hasChords = song.ChordCharts.Any() ? "🎸" : "➕";
            var buttonText = $"{hasChords} {song.Title}";
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(buttonText, $"chord_song_{song.Id}")
            });
        }

        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("❌ Cancel", "chord_cancel")
        });

        var keyboard = new InlineKeyboardMarkup(buttons);

        await _botService.Client.SendMessage(
            message.Chat.Id,
            "🎸 *Chord Charts Manager*\n\n" +
            "Select a song to view/add chord charts:\n" +
            "🎸 = Has chord charts | ➕ = No chord charts",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);
    }

    public async Task HandleChordSongSelection(CallbackQuery callbackQuery, int songId)
    {
        var song = await _dbContext.Songs
            .Include(s => s.ChordCharts)
            .FirstOrDefaultAsync(s => s.Id == songId);

        if (song == null)
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "Song not found.");
            return;
        }

        var buttons = new List<InlineKeyboardButton[]>();

        // Show existing chord charts by key
        if (song.ChordCharts.Any())
        {
            foreach (var chart in song.ChordCharts.OrderBy(c => c.Key))
            {
                buttons.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        $"🎵 Key: {chart.Key} {(chart.Capo != null ? $"({chart.Capo})" : "")}",
                        $"chord_view_{chart.Id}")
                });
            }
        }

        // Add new chord chart button
        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("➕ Add New Chord Chart", $"chord_add_{songId}")
        });

        // Add lyrics button if song has lyrics
        if (!string.IsNullOrEmpty(song.Lyrics))
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData("📝 View Lyrics", $"chord_lyrics_{songId}")
            });
        }
        else
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData("➕ Add Lyrics", $"chord_lyrics_add_{songId}")
            });
        }

        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("🔙 Back", "chord_back"),
            InlineKeyboardButton.WithCallbackData("❌ Cancel", "chord_cancel")
        });

        var keyboard = new InlineKeyboardMarkup(buttons);

        var messageText = $"🎵 *{EscapeMarkdown(song.Title)}*\n";
        if (!string.IsNullOrEmpty(song.Artist))
            messageText += $"👤 Artist: {EscapeMarkdown(song.Artist)}\n";
        if (!string.IsNullOrEmpty(song.Key))
            messageText += $"🎸 Original Key: {EscapeMarkdown(song.Key)}\n";

        messageText += $"\nChord Charts: {song.ChordCharts.Count}\n";
        messageText += "\nSelect an option:";

        await _botService.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            messageText,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);
    }

    public async Task ShowChordChart(CallbackQuery callbackQuery, int chartId)
    {
        var chart = await _dbContext.ChordCharts
            .Include(c => c.Song)
            .FirstOrDefaultAsync(c => c.Id == chartId);

        if (chart == null)
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "Chord chart not found.");
            return;
        }

        var formattedContent = FormatChordChart(chart);

        var buttons = new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔄 Transpose", $"chord_transpose_{chartId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✏️ Edit", $"chord_edit_{chartId}"),
                InlineKeyboardButton.WithCallbackData("🗑️ Delete", $"chord_delete_{chartId}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔙 Back", $"chord_song_{chart.SongId}"),
                InlineKeyboardButton.WithCallbackData("❌ Cancel", "chord_cancel")
            }
        };

        var keyboard = new InlineKeyboardMarkup(buttons);

        var messageText = $"🎸 **{chart.Song.Title}**\n";
        messageText += $"🎵 Key: **{chart.Key}**";
        if (!string.IsNullOrEmpty(chart.Capo))
            messageText += $" | {chart.Capo}";
        if (!string.IsNullOrEmpty(chart.TimeSignature))
            messageText += $" | {chart.TimeSignature}";
        messageText += "\n";
        messageText += "━━━━━━━━━━━━━━━━━━━━\n\n";
        messageText += formattedContent;

        // Telegram message limit is 4096 characters
        if (messageText.Length > 4000)
        {
            messageText = messageText.Substring(0, 3997) + "...";
        }

        await _botService.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            messageText,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);
    }

    public async Task StartAddChordChart(CallbackQuery callbackQuery, int songId)
    {
        var song = await _dbContext.Songs.FindAsync(songId);
        if (song == null)
        {
            await _botService.Client.AnswerCallbackQuery(
                callbackQuery.Id,
                "Song not found.");
            return;
        }

        _conversationState.SetUserState(callbackQuery.From.Id, "chord_add", new
        {
            SongId = songId,
            MessageId = callbackQuery.Message!.MessageId
        });

        await _botService.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            $"🎸 *Adding Chord Chart for: {EscapeMarkdown(song.Title)}*\n\n" +
            "Please send the chord chart in ChordPro format:\n" +
            "Example:\n```\n" +
            "[C]Amazing [C/E]grace, how [F]sweet the [C]sound\n" +
            "That [C]saved a [G]wretch like [Am]me\n" +
            "```\n\n" +
            "First line should include: KEY:C (or any key)\n" +
            "Optional: CAPO:3, TIME:4/4",
            parseMode: ParseMode.Markdown);

        await _botService.Client.AnswerCallbackQuery(
            callbackQuery.Id,
            "Send the chord chart now.");
    }

    public async Task ProcessChordChartInput(Message message, Models.User user)
    {
        var state = _conversationState.GetUserState(message.From!.Id);
        if (state?.State != "chord_add" || state.Data == null)
            return;

        dynamic data = state.Data;
        int songId = data.SongId;
        int messageId = data.MessageId;

        var content = message.Text ?? "";

        // Parse key, capo, and time signature from the first few lines
        var lines = content.Split('\n');
        string key = "C"; // Default key
        string? capo = null;
        string? timeSignature = null;
        int contentStartIndex = 0;

        foreach (var line in lines.Take(3))
        {
            var upperLine = line.ToUpper();
            if (upperLine.StartsWith("KEY:"))
            {
                key = line.Substring(4).Trim();
                contentStartIndex++;
            }
            else if (upperLine.StartsWith("CAPO:"))
            {
                capo = "Capo " + line.Substring(5).Trim();
                contentStartIndex++;
            }
            else if (upperLine.StartsWith("TIME:"))
            {
                timeSignature = line.Substring(5).Trim();
                contentStartIndex++;
            }
            else
            {
                break;
            }
        }

        // Get the actual chord content
        var chordContent = string.Join("\n", lines.Skip(contentStartIndex));

        // Create the chord chart
        var chordChart = new ChordChart
        {
            SongId = songId,
            Key = key,
            Content = chordContent,
            Capo = capo,
            TimeSignature = timeSignature,
            Format = ChordChartFormat.ChordPro,
            CreatedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ChordCharts.Add(chordChart);
        await _dbContext.SaveChangesAsync();

        _conversationState.ClearUserState(message.From.Id);

        await _botService.Client.SendMessage(
            message.Chat.Id,
            $"✅ Chord chart added successfully in key: *{key}*",
            parseMode: ParseMode.Markdown);
    }

    public async Task TransposeChordChart(CallbackQuery callbackQuery, int chartId)
    {
        var buttons = new List<InlineKeyboardButton[]>();

        var keys = new[] { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };

        // Create 3 rows of 4 keys each
        for (int i = 0; i < keys.Length; i += 4)
        {
            var row = keys.Skip(i).Take(4)
                .Select(k => InlineKeyboardButton.WithCallbackData(k, $"chord_transpose_to_{chartId}_{k}"))
                .ToArray();
            buttons.Add(row);
        }

        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("🔙 Back", $"chord_view_{chartId}"),
            InlineKeyboardButton.WithCallbackData("❌ Cancel", "chord_cancel")
        });

        var keyboard = new InlineKeyboardMarkup(buttons);

        await _botService.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            "🔄 *Transpose Chord Chart*\n\nSelect the new key:",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);
    }

    private string FormatChordChart(ChordChart chart)
    {
        if (chart.Format == ChordChartFormat.ChordPro)
        {
            // Format ChordPro style for better readability in Telegram
            var lines = chart.Content.Split('\n');
            var formatted = new System.Text.StringBuilder();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    formatted.AppendLine();
                    continue;
                }

                // Handle ChordPro directives
                if (line.Trim().StartsWith("{") && line.Trim().EndsWith("}"))
                {
                    var directive = line.Trim().Trim('{', '}');

                    // Handle section markers with better spacing
                    if (directive == "verse")
                    {
                        formatted.AppendLine("\n═════════════════");
                        formatted.AppendLine("**[VERSE]**");
                        formatted.AppendLine();
                    }
                    else if (directive == "chorus")
                    {
                        formatted.AppendLine("\n═════════════════");
                        formatted.AppendLine("**[CHORUS]**");
                        formatted.AppendLine();
                    }
                    else if (directive == "bridge")
                    {
                        formatted.AppendLine("\n═════════════════");
                        formatted.AppendLine("**[BRIDGE]**");
                        formatted.AppendLine();
                    }
                    else if (directive == "interlude")
                    {
                        formatted.AppendLine("\n═════════════════");
                        formatted.AppendLine("**[INTERLUDE]**");
                        formatted.AppendLine();
                    }
                    else if (directive.StartsWith("title:"))
                    {
                        formatted.AppendLine($"🎵 **{directive.Replace("title:", "").Trim()}**\n");
                    }
                    else if (directive.StartsWith("artist:"))
                    {
                        formatted.AppendLine($"👤 {directive.Replace("artist:", "").Trim()}\n");
                    }
                    continue;
                }

                // Process chord lines
                var processedLine = ProcessChordProLine(line);
                formatted.AppendLine(processedLine);
            }

            return formatted.ToString();
        }

        return EscapeMarkdown(chart.Content);
    }

    private string ProcessChordProLine(string line)
    {
        // If the line contains chords in brackets, format them nicely
        if (line.Contains('[') && line.Contains(']'))
        {
            // Replace chord brackets with bold formatting and add spacing
            var result = line;

            // Find all chords and replace [chord] with bold chord with spacing
            var chordPattern = @"\[([^\]]+)\]";

            // Add a space after each chord for better readability
            result = System.Text.RegularExpressions.Regex.Replace(result, chordPattern, "*$1* ");

            // Clean up any double spaces
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");

            // Trim the line
            result = result.Trim();

            return result;
        }

        // No chords in this line, return as is
        return line;
    }

    private string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return text.Replace("_", "\\_")
                   .Replace("*", "\\*")
                   .Replace("[", "\\[")
                   .Replace("]", "\\]")
                   .Replace("(", "\\(")
                   .Replace(")", "\\)")
                   .Replace("~", "\\~")
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
}