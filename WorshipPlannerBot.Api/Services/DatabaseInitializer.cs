using Microsoft.EntityFrameworkCore;
using WorshipPlannerBot.Api.Data;

namespace WorshipPlannerBot.Api.Services;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<BotDbContext>>();

        try
        {
            // Ensure database exists
            await dbContext.Database.EnsureCreatedAsync();

            // Check if ChordCharts table exists and create if not
            var tableExists = await CheckTableExistsAsync(dbContext, "ChordCharts");

            if (!tableExists)
            {
                logger.LogInformation("Creating ChordCharts table...");
                await CreateChordChartsTableAsync(dbContext);
                logger.LogInformation("ChordCharts table created successfully.");
            }
            else
            {
                logger.LogInformation("ChordCharts table already exists.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing database");
            throw;
        }
    }

    private static async Task<bool> CheckTableExistsAsync(BotDbContext context, string tableName)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";

        var result = await command.ExecuteScalarAsync();
        await connection.CloseAsync();

        return Convert.ToInt32(result) > 0;
    }

    private static async Task CreateChordChartsTableAsync(BotDbContext context)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS ""ChordCharts"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ChordCharts"" PRIMARY KEY AUTOINCREMENT,
                ""SongId"" INTEGER NOT NULL,
                ""Key"" TEXT NOT NULL,
                ""Content"" TEXT NOT NULL,
                ""Capo"" TEXT NULL,
                ""TimeSignature"" TEXT NULL,
                ""Format"" INTEGER NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""UpdatedAt"" TEXT NULL,
                ""CreatedByUserId"" INTEGER NULL,
                CONSTRAINT ""FK_ChordCharts_Songs_SongId"" FOREIGN KEY (""SongId"") REFERENCES ""Songs"" (""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_ChordCharts_Users_CreatedByUserId"" FOREIGN KEY (""CreatedByUserId"") REFERENCES ""Users"" (""Id"") ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS ""IX_ChordCharts_CreatedByUserId"" ON ""ChordCharts"" (""CreatedByUserId"");
            CREATE INDEX IF NOT EXISTS ""IX_ChordCharts_SongId_Key"" ON ""ChordCharts"" (""SongId"", ""Key"");
        ";

        await context.Database.ExecuteSqlRawAsync(sql);
    }
}