using KCAS.Admin.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;

namespace KCAS.Admin.Tests;

public sealed class KcasWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string ConnectionString { get; } = TestConfiguration.GetConnectionString();
    private string? previousConnectionStringOverride;
    private string? previousMigrateOnStartupOverride;

    public async Task InitializeAsync()
    {
        await RecreateDatabaseAsync();
        previousConnectionStringOverride = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        previousMigrateOnStartupOverride = Environment.GetEnvironmentVariable("Database__MigrateOnStartup");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", ConnectionString);
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "true");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.CanConnectAsync();
    }

    public new Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", previousConnectionStringOverride);
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", previousMigrateOnStartupOverride);
        Dispose();
        return Task.CompletedTask;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["Database:MigrateOnStartup"] = "true"
            });
        });
    }

    private async Task RecreateDatabaseAsync()
    {
        var builder = new MySqlConnectionStringBuilder(ConnectionString);
        var databaseName = builder.Database;
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("The test connection string must include a database name.");
        }

        builder.Database = "";
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DROP DATABASE IF EXISTS `{databaseName.Replace("`", "``")}`;
            CREATE DATABASE `{databaseName.Replace("`", "``")}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
            """;
        await command.ExecuteNonQueryAsync();
    }
}
