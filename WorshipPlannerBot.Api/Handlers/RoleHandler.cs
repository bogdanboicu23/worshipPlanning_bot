using System.Text;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WorshipPlannerBot.Api.Data;
using WorshipPlannerBot.Api.Models;
using WorshipPlannerBot.Api.Services;

namespace WorshipPlannerBot.Api.Handlers;

public class RoleHandler
{
    private readonly IBotService _botService;
    private readonly BotDbContext _dbContext;
    private readonly ILogger<RoleHandler> _logger;

    public RoleHandler(IBotService botService, BotDbContext dbContext, ILogger<RoleHandler> logger)
    {
        _botService = botService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task ShowUserRolesAsync(Message message, Models.User user)
    {
        await _dbContext.Entry(user)
            .Collection(u => u.UserRoles)
            .Query()
            .Include(ur => ur.Role)
            .LoadAsync();

        var sb = new StringBuilder();
        sb.AppendLine("👤 *Your Profile*\n");
        sb.AppendLine($"Name: {user.FirstName} {user.LastName ?? ""}".Trim());
        sb.AppendLine($"Username: {(string.IsNullOrEmpty(user.Username) ? "not set" : $"@{user.Username}")}");
        sb.AppendLine($"Admin: {(user.IsAdmin ? "Yes ✅" : "No")}");
        sb.AppendLine("\n*Your Roles:*");

        if (user.UserRoles.Any())
        {
            foreach (var userRole in user.UserRoles.OrderBy(ur => ur.Role.DisplayOrder))
            {
                sb.AppendLine($"{userRole.Role.Icon} {userRole.Role.Name}");
            }
        }
        else
        {
            sb.AppendLine("No roles assigned yet.");
        }

        sb.AppendLine("\nUse /register to update your roles.");

        await _botService.Client.SendMessage(
            message.Chat.Id,
            sb.ToString(),
            parseMode: ParseMode.Markdown);
    }

    public async Task<InlineKeyboardMarkup> CreateRoleSelectionKeyboard(int userId)
    {
        var allRoles = await _dbContext.Roles.OrderBy(r => r.DisplayOrder).ToListAsync();
        var userRoleIds = await _dbContext.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        var buttons = new List<List<InlineKeyboardButton>>();

        foreach (var role in allRoles)
        {
            var isSelected = userRoleIds.Contains(role.Id);
            var buttonText = isSelected
                ? $"✅ {role.Icon} {role.Name}"
                : $"{role.Icon} {role.Name}";

            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData(buttonText, $"role_{role.Id}")
            });
        }

        buttons.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("✅ Done", "role_done")
        });

        return new InlineKeyboardMarkup(buttons);
    }

    public async Task ToggleUserRoleAsync(int userId, int roleId)
    {
        var userRole = await _dbContext.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId);

        if (userRole != null)
        {
            _dbContext.UserRoles.Remove(userRole);
        }
        else
        {
            _dbContext.UserRoles.Add(new UserRole
            {
                UserId = userId,
                RoleId = roleId,
                AssignedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task AssignRoleToUserAsync(Models.User admin, string username, string roleName)
    {
        username = username.TrimStart('@');

        var targetUser = await _dbContext.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Username == username);

        if (targetUser == null)
        {
            await _botService.Client.SendMessage(
                admin.TelegramId,
                $"❌ User @{username} not found in the system.");
            return;
        }

        var role = await _dbContext.Roles
            .FirstOrDefaultAsync(r => r.Name.ToLower() == roleName.ToLower());

        if (role == null)
        {
            var availableRoles = await _dbContext.Roles.Select(r => r.Name).ToListAsync();
            await _botService.Client.SendMessage(
                admin.TelegramId,
                $"❌ Role '{roleName}' not found.\n\nAvailable roles: {string.Join(", ", availableRoles)}");
            return;
        }

        var existingRole = targetUser.UserRoles.FirstOrDefault(ur => ur.RoleId == role.Id);

        if (existingRole != null)
        {
            await _botService.Client.SendMessage(
                admin.TelegramId,
                $"ℹ️ User @{username} already has the {role.Name} role.");
            return;
        }

        _dbContext.UserRoles.Add(new UserRole
        {
            UserId = targetUser.Id,
            RoleId = role.Id,
            AssignedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();

        await _botService.Client.SendMessage(
            admin.TelegramId,
            $"✅ Successfully assigned {role.Icon} {role.Name} role to @{username}");
    }
}