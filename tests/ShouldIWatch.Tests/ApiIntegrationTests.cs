using System.Net.Http.Json;
using ShouldIWatch.Tests.Infrastructure;

namespace ShouldIWatch.Tests;

/// <summary>Exercises the API started by the AppHost (requires Docker) against a real SQL Server.</summary>
public sealed class ApiIntegrationTests(AppHostFixture fixture)
{
    private sealed record Greeting(int Id, string Message, DateTime CreatedAt);

    [Fact]
    public async Task GreetingEndpointReturnsSeededGreetingFromDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var httpClient = fixture.App.CreateHttpClient("api", "http");
        var greeting = await httpClient.GetFromJsonAsync<Greeting>("/api/greeting", cancellationToken);

        Assert.NotNull(greeting);
        Assert.Equal("Hello from Azure SQL!", greeting.Message);
    }
}
