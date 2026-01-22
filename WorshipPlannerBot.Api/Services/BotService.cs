using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace WorshipPlannerBot.Api.Services;

public class BotService : IBotService
{
    private readonly TelegramBotClient _botClient;
    private readonly ILogger<BotService> _logger;

    public BotService(BotConfiguration configuration, ILogger<BotService> logger)
    {
        _botClient = new TelegramBotClient(configuration.BotToken);
        _logger = logger;
    }

    public ITelegramBotClient Client => _botClient;

    public async Task<User> GetBotInfoAsync()
    {
        return await _botClient.GetMe();
    }

    public async Task<bool> SetWebhookAsync(string webhookUrl)
    {
        try
        {
            await _botClient.SetWebhook(webhookUrl);
            _logger.LogInformation($"Webhook set to {webhookUrl}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set webhook");
            return false;
        }
    }

    public async Task<bool> DeleteWebhookAsync()
    {
        try
        {
            await _botClient.DeleteWebhook();
            _logger.LogInformation("Webhook deleted");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete webhook");
            return false;
        }
    }

    public async Task SetBotCommandsAsync()
    {
        try
        {
            var commands = new List<BotCommand>
            {
                new BotCommand { Command = "start", Description = "Start the bot" },
                new BotCommand { Command = "help", Description = "Show available commands" },
                new BotCommand { Command = "register", Description = "Choose your roles" },
                new BotCommand { Command = "myroles", Description = "View your profile and roles" },
                new BotCommand { Command = "events", Description = "View upcoming events" },
                new BotCommand { Command = "songs", Description = "Browse song library" },
                new BotCommand { Command = "language", Description = "Change language" },
                new BotCommand { Command = "newevent", Description = "Create new event (Admin)" },
                new BotCommand { Command = "deleteevent", Description = "Delete an event (Admin)" },
                new BotCommand { Command = "remind", Description = "Send reminders (Admin)" },
                new BotCommand { Command = "admin", Description = "Admin panel (Admin)" }
            };

            await _botClient.SetMyCommands(commands);
            _logger.LogInformation("Bot commands have been set successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set bot commands");
        }
    }
}

public interface IBotService
{
    ITelegramBotClient Client { get; }
    Task<User> GetBotInfoAsync();
    Task<bool> SetWebhookAsync(string webhookUrl);
    Task<bool> DeleteWebhookAsync();
    Task SetBotCommandsAsync();
}