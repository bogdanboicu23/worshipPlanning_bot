using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.Enums;
using WorshipPlannerBot.Api.Data;
using WorshipPlannerBot.Api.Models;
using WorshipPlannerBot.Api.Services;

namespace WorshipPlannerBot.Api.Handlers;

public class InlineQueryHandler
{
    private readonly IBotService _botService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILocalizationService _localization;
    private readonly ILogger<InlineQueryHandler> _logger;
    private readonly SongManager _songManager;

    public InlineQueryHandler(
        IBotService botService,
        IServiceProvider serviceProvider,
        ILocalizationService localization,
        ILogger<InlineQueryHandler> logger,
        SongManager songManager)
    {
        _botService = botService;
        _serviceProvider = serviceProvider;
        _localization = localization;
        _logger = logger;
        _songManager = songManager;
    }

    public async Task HandleInlineQueryAsync(InlineQuery inlineQuery)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

            // Get user to determine language
            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.TelegramId == inlineQuery.From.Id);

            var lang = user?.LanguageCode ?? "en";
            var query = inlineQuery.Query ?? "";
            var results = new List<InlineQueryResult>();

            // Check if this is a song search
            if (query.StartsWith("songs ", StringComparison.OrdinalIgnoreCase) ||
                query.StartsWith("song ", StringComparison.OrdinalIgnoreCase) ||
                query.StartsWith("cantari ", StringComparison.OrdinalIgnoreCase))
            {
                await HandleSongSearch(inlineQuery, query, results);
            }
            else
            {
                await HandleEventSearch(inlineQuery, query.ToLower(), dbContext, lang, results);
            }

            await _botService.Client.AnswerInlineQuery(
                inlineQuery.Id,
                results,
                cacheTime: 60,
                isPersonal: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling inline query");
        }
    }

    private async Task HandleSongSearch(InlineQuery inlineQuery, string query, List<InlineQueryResult> results)
    {
        var searchTerm = query
            .Replace("songs ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("song ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("cantari ", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        var songs = await _songManager.SearchSongs(searchTerm);

        foreach (var song in songs.Take(20))
        {
            var title = song.Title;
            var description = new List<string>();

            if (!string.IsNullOrEmpty(song.Artist))
                description.Add($"Artist: {song.Artist}");
            if (!string.IsNullOrEmpty(song.Key))
                description.Add($"Tonalitate: {song.Key}");
            if (!string.IsNullOrEmpty(song.Tempo))
                description.Add($"Tempo: {song.Tempo} BPM");

            var descText = description.Any() ? string.Join(" | ", description) : "Apasă pentru a selecta";

            results.Add(new InlineQueryResultArticle(
                $"song_{song.Id}",
                $"🎵 {title}",
                new InputTextMessageContent($"🎵 {title}"))
            {
                Description = descText,
                ThumbnailUrl = "https://cdn-icons-png.flaticon.com/512/651/651799.png"
            });
        }

        // Add option to create new song if not found
        if (!songs.Any() && !string.IsNullOrEmpty(searchTerm))
        {
            results.Add(new InlineQueryResultArticle(
                "new_song",
                $"Creează: {searchTerm}",
                new InputTextMessageContent($"🎵 {searchTerm}"))
            {
                Description = "Apasă pentru a crea cântare nouă",
                ThumbnailUrl = "https://cdn-icons-png.flaticon.com/512/1237/1237946.png"
            });
        }
    }

    private async Task HandleEventSearch(InlineQuery inlineQuery, string query, BotDbContext dbContext, string lang, List<InlineQueryResult> results)
    {
        // Get upcoming events
        var events = await dbContext.Events
            .Where(e => e.DateTime > DateTime.UtcNow && !e.IsCancelled)
            .OrderBy(e => e.DateTime)
            .Take(10)
            .Include(e => e.Attendances)
                .ThenInclude(a => a.User)
                    .ThenInclude(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
            .ToListAsync();

        // Filter by query if provided
        if (!string.IsNullOrEmpty(query))
        {
            events = events.Where(e =>
                e.Title.ToLower().Contains(query) ||
                (e.Location != null && e.Location.ToLower().Contains(query)) ||
                (e.Description != null && e.Description.ToLower().Contains(query))
            ).ToList();
        }

        foreach (var evt in events.Take(5)) // Limit to 5 results for inline display
        {
            var confirmedCount = evt.Attendances.Count(a => a.Status == AttendanceStatus.Yes);
            var maybeCount = evt.Attendances.Count(a => a.Status == AttendanceStatus.Maybe);

            // Build role coverage summary
            var roleGroups = evt.Attendances
                .Where(a => a.Status == AttendanceStatus.Yes)
                .SelectMany(a => a.User.UserRoles.Select(ur => ur.Role))
                .GroupBy(r => r.Name)
                .Select(g => $"{g.First().Icon} {g.Key}: {g.Count()}")
                .ToList();

            var roleCoverage = roleGroups.Any()
                ? string.Join(", ", roleGroups)
                : _localization.GetString("events.noroles", lang);

            var messageText = $"🎵 *{evt.Title}*\n" +
                            $"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy}\n" +
                            $"🕐 {evt.DateTime.ToLocalTime():HH:mm}\n" +
                            $"📍 {evt.Location}\n" +
                            $"\n✅ {_localization.GetString("events.confirmed", lang)}: {confirmedCount}\n" +
                            $"🤔 {_localization.GetString("events.maybe", lang)}: {maybeCount}\n" +
                            $"\n📋 {_localization.GetString("events.roles", lang)}:\n{roleCoverage}";

            if (!string.IsNullOrEmpty(evt.Description))
            {
                messageText = $"🎵 *{evt.Title}*\n" +
                            $"📅 {evt.DateTime.ToLocalTime():dddd, dd MMMM yyyy}\n" +
                            $"🕐 {evt.DateTime.ToLocalTime():HH:mm}\n" +
                            $"📍 {evt.Location}\n" +
                            $"📝 {evt.Description}\n" +
                            $"\n✅ {_localization.GetString("events.confirmed", lang)}: {confirmedCount}\n" +
                            $"🤔 {_localization.GetString("events.maybe", lang)}: {maybeCount}\n" +
                            $"\n📋 {_localization.GetString("events.roles", lang)}:\n{roleCoverage}";
            }

            var result = new InlineQueryResultArticle(
                id: evt.Id.ToString(),
                title: $"📅 {evt.Title}",
                inputMessageContent: new InputTextMessageContent(messageText)
                {
                    ParseMode = ParseMode.Markdown
                })
            {
                Description = $"{evt.DateTime.ToLocalTime():dddd, dd MMM HH:mm} • {evt.Location}\n" +
                             $"✅ {confirmedCount} confirmed",
                ThumbnailUrl = "https://cdn-icons-png.flaticon.com/512/2693/2693507.png" // Calendar icon
            };

            results.Add(result);
        }

        // Add a "no events" result if empty
        if (!results.Any())
        {
            var noEventsText = _localization.GetString("events.noupcoming", lang);
            results.Add(new InlineQueryResultArticle(
                id: "no-events",
                title: "📭 " + noEventsText,
                inputMessageContent: new InputTextMessageContent(noEventsText))
            {
                Description = _localization.GetString("events.empty", lang)
            });
        }
    }
}