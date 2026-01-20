using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using WorshipPlannerBot.Api.Data;
using WorshipPlannerBot.Api.Handlers;
using WorshipPlannerBot.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure port for Railway/Render/etc
var port = Environment.GetEnvironmentVariable("PORT") ?? "5245";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Add services to the container.
builder.Services.AddControllers().AddNewtonsoftJson();

// Configure Bot
var botConfig = builder.Configuration.GetSection("BotConfiguration").Get<BotConfiguration>();
builder.Services.AddSingleton(botConfig ?? new BotConfiguration());

// Configure Database
builder.Services.AddDbContext<BotDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register Services
builder.Services.AddSingleton<IBotService, BotService>();
builder.Services.AddSingleton<IConversationStateService, ConversationStateService>();
builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
builder.Services.AddScoped<IGroupAnnouncementService, GroupAnnouncementService>();
builder.Services.AddSingleton<IUpdateHandlerService, UpdateHandlerService>();

// Register Handlers
builder.Services.AddScoped<WorshipPlannerBot.Api.Handlers.EventHandler>();
builder.Services.AddScoped<EventCreationWizard>();
builder.Services.AddScoped<RoleHandler>();
builder.Services.AddScoped<RegistrationHandler>();
builder.Services.AddScoped<CallbackHandler>();
builder.Services.AddScoped<InlineQueryHandler>();
builder.Services.AddScoped<SongManager>();

// Register Reminder Service
builder.Services.AddScoped<IReminderService, ReminderService>();

// Add background services
builder.Services.AddHostedService<EventCleanupService>();

// Add hosted service for polling
if (!botConfig?.UseWebhook ?? true)
{
    builder.Services.AddHostedService<PollingService>();
}

builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseRouting();

app.MapControllers();
app.MapHealthChecks("/health");

// Configure webhook endpoint if using webhooks
if (botConfig?.UseWebhook ?? false)
{
    app.MapPost("/bot", async (Update update, IUpdateHandlerService updateHandler) =>
    {
        await updateHandler.HandleUpdateAsync(update);
        return Results.Ok();
    });
}

// Ensure database is created and migrations applied
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
    dbContext.Database.EnsureCreated();
}

app.Run();

// Polling Service for local development
public class PollingService : BackgroundService
{
    private readonly IBotService _botService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PollingService> _logger;

    public PollingService(
        IBotService botService,
        IServiceProvider serviceProvider,
        ILogger<PollingService> logger)
    {
        _botService = botService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = { }
        };

        _logger.LogInformation("Starting bot polling...");

        await _botService.Client.ReceiveAsync(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            stoppingToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var updateHandler = scope.ServiceProvider.GetRequiredService<IUpdateHandlerService>();
        await updateHandler.HandleUpdateAsync(update);
    }

    private Task HandleErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Telegram Bot polling error");
        return Task.CompletedTask;
    }
}
