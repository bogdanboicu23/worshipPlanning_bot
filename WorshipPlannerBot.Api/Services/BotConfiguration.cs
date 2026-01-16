namespace WorshipPlannerBot.Api.Services;

public class BotConfiguration
{
    public string BotToken { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public bool UseWebhook { get; set; }
}