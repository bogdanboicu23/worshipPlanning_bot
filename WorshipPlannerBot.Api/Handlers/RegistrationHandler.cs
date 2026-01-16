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

    public RegistrationHandler(
        IBotService botService,
        BotDbContext dbContext,
        RoleHandler roleHandler,
        ILogger<RegistrationHandler> logger)
    {
        _botService = botService;
        _dbContext = dbContext;
        _roleHandler = roleHandler;
        _logger = logger;
    }

    public async Task StartRegistrationAsync(Message message, Models.User user)
    {
        var text = "🎵 *Registration & Role Selection*\n\n" +
                  "Select the roles that describe your involvement in worship services.\n" +
                  "You can select multiple roles by tapping on them.\n" +
                  "Press ✅ Done when finished.";

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
                .Select(ur => $"{ur.Role.Icon} {ur.Role.Name}"))
            : "No roles selected";

        var confirmationText = $"✅ *Registration Complete!*\n\n" +
                              $"Your selected roles:\n{rolesText}\n\n" +
                              $"You can update your roles anytime using /register\n" +
                              $"Use /events to see upcoming worship services.";

        await _botService.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            confirmationText,
            parseMode: ParseMode.Markdown);
    }
}