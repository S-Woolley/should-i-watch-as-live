namespace ShouldIWatch.Tests.Infrastructure;

/// <summary>Answers requests by path and query from a route table, and records every request it receives.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> routes = new(StringComparer.Ordinal);
    private readonly List<HttpRequestMessage> requests = [];
    private readonly Lock gate = new();

    public IReadOnlyList<HttpRequestMessage> Requests
    {
        get
        {
            lock (gate)
            {
                return [.. requests];
            }
        }
    }

    /// <summary>Serves a fixture file from <c>Fixtures/Cfbd</c> for <paramref name="pathAndQuery"/>.</summary>
    public StubHttpMessageHandler WithFixture(string pathAndQuery, string fixtureName) =>
        WithResponse(pathAndQuery, () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixtures.Read(fixtureName), System.Text.Encoding.UTF8, "application/json"),
        });

    public StubHttpMessageHandler WithResponse(string pathAndQuery, Func<HttpResponseMessage> response)
    {
        routes[pathAndQuery] = response;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            requests.Add(request);
        }

        var pathAndQuery = request.RequestUri!.PathAndQuery;
        return Task.FromResult(routes.TryGetValue(pathAndQuery, out var response)
            ? response()
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"No stub for {pathAndQuery}") });
    }
}

internal static class Fixtures
{
    public static string Read(string fixtureName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Cfbd", fixtureName));
}
