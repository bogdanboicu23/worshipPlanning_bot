using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WorshipPlannerBot.Api.Data;
using WorshipPlannerBot.Api.Services;

namespace WorshipPlannerBot.Api.Handlers;

public class RegistrationHandler
{
    private readonly IBotService _botService;
    private readonly BotDbContext _dbContext;
    private readonly RoleHandler _roleHandler;
    private readonly ILogger<RegistrationHandler> _logger;
    private readonly ILocalizationService _localization;

    public RegistrationHandler(
        IBotService botService,
        BotDbContext dbContext,
        RoleHandler roleHandler,
        ILogger<RegistrationHandler> logger,
        ILocalizationService localization)
    {
        _botService = botService;
        _dbContext = dbContext;
        _roleHandler = roleHandler;
        _logger = logger;
        _localization = localization;
    }

    public async Task StartRegistrationAsync(Message message, Models.User user)
    {
        var text = "🎵Alegeți rolul pe care îl aveți în echipa de închinare!";

        var keyboard = await _roleHandler.CreateRoleSelectionKeyboard(user.Id);

        await _botService.Client.SendMessage(
            message.Chat.Id,
            text,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard);
    }

    public async Task CompleteRegistrationAsync(CallbackQuery callbackQuery, Models.User user)
    {
        await _dbContext.Entry(user)
            .Collection(u => u.UserRoles)
            .Query()
            .Include(ur => ur.Role)
            .LoadAsync();

        var rolesText = user.UserRoles.Any()
            ? string.Join("\n", user.UserRoles
                .OrderBy(ur => ur.Role.DisplayOrder)
                .Select(ur =>
                {
                    var roleName = _localization.GetString($"Role.{ur.Role.Name.Replace(" ", "")}", user.LanguageCode) ?? ur.Role.Name;
                    return $"{ur.Role.Icon} {roleName}";
                }))
            : "No roles selected";

        var confirmationText = $"✅ *Registrare cu succes!*\n\n" +
                              $"Rolurile selectate:\n{rolesText}\n\n" +
                              $"Va puteti schimba oricand rolurile folosind /register\n" +
                              $"Utilizati /events ca sa vedeti evenimentele urmatoare.";

        await _botService.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            confirmationText,
            parseMode: ParseMode.Markdown);
    }
}