using Aspire.Hosting;
using Dapper;
using Microsoft.Data.SqlClient;
using ShouldIWatch.Database;
using ShouldIWatch.Tests.Infrastructure;

[assembly: AssemblyFixture(typeof(AppHostFixture))]

namespace ShouldIWatch.Tests.Infrastructure;

/// <summary>
/// Starts the whole AppHost once for the test run (requires Docker). The API gets a dummy CFBD key and an
/// unreachable CFBD base URL, so nothing contacts CollegeFootballData.com. Database tests use their own freshly
/// migrated database on the same SQL Server, so they never touch the app's (volume-backed) database.
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    // Generous: the first run pulls the SQL Server image (~1.5 GB).
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private string? masterConnectionString;
    private string? testDatabaseName;

    public DistributedApplication App { get; private set; } = null!;

    /// <summary>Connection string for a migrated database used only by this test run.</summary>
    public string TestDatabaseConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.ShouldIWatch_AppHost>(cancellationToken);
        appHost.Configuration["Parameters:cfbd-api-key"] = "integration-test-dummy-key";
        appHost.CreateResourceBuilder<ProjectResource>("api")
            .WithEnvironment("Cfbd__BaseUrl", "http://127.0.0.1:9/");
        appHost.Services.ConfigureHttpClientDefaults(client => client.AddStandardResilienceHandler());

        App = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await App.StartAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellationToken)
            .WaitAsync(StartupTimeout, cancellationToken);

        var appDbConnectionString = await App.GetConnectionStringAsync("appdb", cancellationToken)
            ?? throw new InvalidOperationException("No connection string for appdb.");
        await CreateTestDatabaseAsync(appDbConnectionString, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (masterConnectionString is not null && testDatabaseName is not null)
        {
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(masterConnectionString);
            await master.ExecuteAsync(
                $"ALTER DATABASE [{testDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{testDatabaseName}];");
        }

        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }

    private async Task CreateTestDatabaseAsync(string appDbConnectionString, CancellationToken cancellationToken)
    {
        testDatabaseName = $"shouldiwatch_tests_{Guid.NewGuid():N}";
        masterConnectionString = new SqlConnectionStringBuilder(appDbConnectionString) { InitialCatalog = "master" }.ConnectionString;

        await using (var master = new SqlConnection(masterConnectionString))
        {
            await master.ExecuteAsync(new CommandDefinition($"CREATE DATABASE [{testDatabaseName}];", cancellationToken: cancellationToken));
        }

        TestDatabaseConnectionString =
            new SqlConnectionStringBuilder(appDbConnectionString) { InitialCatalog = testDatabaseName }.ConnectionString;

        var result = DatabaseMigrator.Migrate(TestDatabaseConnectionString, new DbUp.Engine.Output.NoOpUpgradeLog());
        if (!result.Successful)
        {
            throw new InvalidOperationException("Test database migration failed.", result.Error);
        }
    }
}
