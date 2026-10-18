using System.Net.Http.Json;

namespace ShouldIWatch.Tests;

/// <summary>Starts the whole AppHost (requires Docker) and exercises the API against a real SQL Server.</summary>
public sealed class ApiIntegrationTests
{
    // Generous: the first run pulls the SQL Server image (~1.5 GB).
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private sealed record Greeting(int Id, string Message, DateTime CreatedAt);

    [Fact]
    public async Task GreetingEndpointReturnsSeededGreetingFromDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.ShouldIWatch_AppHost>(cancellationToken);
        appHost.Services.ConfigureHttpClientDefaults(client => client.AddStandardResilienceHandler());

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellationToken).WaitAsync(StartupTimeout, cancellationToken);

        using var httpClient = app.CreateHttpClient("api", "http");
        var greeting = await httpClient.GetFromJsonAsync<Greeting>("/api/greeting", cancellationToken);

        Assert.NotNull(greeting);
        Assert.Equal("Hello from Azure SQL!", greeting.Message);
    }
}
