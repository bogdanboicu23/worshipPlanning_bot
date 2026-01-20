using Microsoft.EntityFrameworkCore;
using WorshipPlannerBot.Api.Data;

namespace WorshipPlannerBot.Api.Services;

public class EventCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1); // Run every hour
    private readonly TimeSpan _eventRetentionPeriod = TimeSpan.FromDays(7); // Keep events for 7 days after they pass

    public EventCleanupService(IServiceProvider serviceProvider, ILogger<EventCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Event Cleanup Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOldEventsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during event cleanup");
            }

            await Task.Delay(_cleanupInterval, stoppingToken);
        }

        _logger.LogInformation("Event Cleanup Service stopped");
    }

    private async Task CleanupOldEventsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        // Calculate the cutoff date
        var cutoffDate = DateTime.UtcNow.Subtract(_eventRetentionPeriod);

        // Find events that are past the retention period
        var oldEvents = await dbContext.Events
            .Where(e => e.DateTime < cutoffDate && !e.IsCancelled)
            .Include(e => e.Attendances)
            .Include(e => e.SetListItems)
            .ToListAsync();

        if (oldEvents.Any())
        {
            _logger.LogInformation($"Found {oldEvents.Count} old events to clean up");

            foreach (var evt in oldEvents)
            {
                _logger.LogInformation($"Deleting event: {evt.Title} from {evt.DateTime:yyyy-MM-dd}");
                dbContext.Events.Remove(evt);
            }

            await dbContext.SaveChangesAsync();
            _logger.LogInformation($"Successfully deleted {oldEvents.Count} old events");
        }
        else
        {
            _logger.LogDebug("No old events to clean up");
        }
    }
}