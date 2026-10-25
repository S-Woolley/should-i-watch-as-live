using Microsoft.Extensions.Configuration;
using ShouldIWatch.Api.CollegeFootball;
using ShouldIWatch.Api.CollegeFootball.Cfbd;
using ShouldIWatch.Api.CollegeFootball.Sources;
using ShouldIWatch.Tests.Infrastructure;

namespace ShouldIWatch.Tests.CollegeFootball;

public sealed class CfbdDataSourceTests : IDisposable
{
    private const string ApiKey = "test-cfbd-key";

    private const string SchedulePath = "/games?year=2024&seasonType=both&classification=fbs";
    private const string Week5GamesPath = "/games?year=2024&seasonType=regular&week=5&classification=fbs";
    private const string Week5LinesPath = "/lines?year=2024&seasonType=regular&week=5";
    private const string Week5RankingsPath = "/rankings?year=2024&seasonType=regular&week=5";
    private const string PostseasonGamesPath = "/games?year=2024&seasonType=postseason&classification=fbs";
    private const string PostseasonLinesPath = "/lines?year=2024&seasonType=postseason";
    private const string RegularRankingsPath = "/rankings?year=2024&seasonType=regular";

    private readonly StubHttpMessageHandler handler = new();
    private readonly ServiceProvider services;
    private readonly ICollegeFootballDataSource dataSource;

    public CfbdDataSourceTests()
    {
        // Resolved through the app's registration, so base address, bearer key and typed client wiring are covered.
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cfbd:ApiKey"] = ApiKey,
                ["Cfbd:BaseUrl"] = "https://cfbd.test",
            })
            .Build());
        serviceCollection.AddLogging();
        // As AddServiceDefaults does for every client; the CFBD client must opt out of its retries.
        serviceCollection.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        serviceCollection.AddCollegeFootball();
        serviceCollection.AddHttpClient<ICollegeFootballDataSource, CfbdDataSource>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        services = serviceCollection.BuildServiceProvider();
        dataSource = services.GetRequiredService<ICollegeFootballDataSource>();
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        services.Dispose();
        handler.Dispose();
    }

    [Fact]
    public void IsTheRegisteredDataSourceAndNamesItsProvider()
    {
        Assert.IsType<CfbdDataSource>(dataSource);
        Assert.Equal("cfbd", dataSource.ProviderName);
    }

    [Fact]
    public async Task SeasonScheduleRequestsBothSeasonTypesWithTheBearerKey()
    {
        handler.WithFixture(SchedulePath, "games-2024-both.json");

        var schedule = await dataSource.GetSeasonScheduleAsync(2024, CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://cfbd.test" + SchedulePath, request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(ApiKey, request.Headers.Authorization?.Parameter);

        Assert.Equal(7, schedule.Count);
        var alabamaGeorgia = schedule.Single(game => game.ProviderGameId == "401001");
        Assert.Equal(new ScheduledGame("401001", 2024, WeekKey.Regular(5), new DateTime(2024, 9, 28, 23, 30, 0, DateTimeKind.Utc),
            false, "Alabama", "Georgia"), alabamaGeorgia);
        Assert.Equal(DateTimeKind.Utc, alabamaGeorgia.KickoffUtc.Kind);
        Assert.Equal(WeekKey.Regular(0), schedule.Single(game => game.ProviderGameId == "400099").Week);
        Assert.Equal(WeekKey.Postseason, schedule.Single(game => game.ProviderGameId == "402001").Week);
        Assert.True(schedule.Single(game => game.ProviderGameId == "401004").StartTimeTbd);
    }

    [Fact]
    public async Task WeekResultsMapRegularWeekAndDeriveOvertimeFromLineScores()
    {
        handler.WithFixture(Week5GamesPath, "games-2024-regular-week5.json");

        var results = (await dataSource.GetWeekResultsAsync(2024, WeekKey.Regular(5), CancellationToken))
            .ToDictionary(result => result.ProviderGameId);

        Assert.Equal("https://cfbd.test" + Week5GamesPath, Assert.Single(handler.Requests).RequestUri!.ToString());
        Assert.Equal(new GameResult("401001", true, 41, 34, false, 1900, 1880, 8.2), results["401001"]);
        Assert.False(results["401002"].WentToOvertime);

        // Five periods means overtime; excitementIndex is absent from this game's JSON.
        Assert.True(results["401003"].WentToOvertime);
        Assert.Null(results["401003"].ExcitementIndex);

        // Not yet played (TBD kickoff): no points.
        Assert.Equal(new GameResult("401004", false, null, null, false), results["401004"]);
    }

    [Fact]
    public async Task PostseasonResultsRequestThePostseasonSeasonType()
    {
        handler.WithFixture(PostseasonGamesPath, "games-2024-postseason.json");

        var result = Assert.Single(await dataSource.GetWeekResultsAsync(2024, WeekKey.Postseason, CancellationToken));

        Assert.Equal("https://cfbd.test" + PostseasonGamesPath, Assert.Single(handler.Requests).RequestUri!.ToString());
        Assert.Equal("402001", result.ProviderGameId);
    }

    [Fact]
    public async Task ReferenceDataJoinsLinesOnGameIdAndUsesApRanksWithoutACfpPoll()
    {
        handler
            .WithFixture(Week5GamesPath, "games-2024-regular-week5.json")
            .WithFixture(Week5LinesPath, "lines-2024-regular-week5.json")
            .WithFixture(Week5RankingsPath, "rankings-2024-regular-week5.json");

        var references = (await dataSource.GetWeekReferenceDataAsync(2024, WeekKey.Regular(5), CancellationToken))
            .ToDictionary(reference => reference.ProviderGameId);

        Assert.Equal(
            [Week5GamesPath, Week5LinesPath, Week5RankingsPath],
            handler.Requests.Select(request => request.RequestUri!.PathAndQuery).Order(StringComparer.Ordinal));
        Assert.All(handler.Requests, request => Assert.Equal(ApiKey, request.Headers.Authorization?.Parameter));

        // DraftKings is preferred over Bovada; AP ranks are used, not the Coaches Poll.
        Assert.Equal(new GameReference("401001", -2.5, 4, 2), references["401001"]);
        // ESPN Bet has no spread, so the next line with one is used; Iowa is unranked.
        Assert.Equal(new GameReference("401002", -27.5, 3, null), references["401002"]);
        // No lines at all, and a game missing from the lines response.
        Assert.Equal(new GameReference("401003", null, null, null), references["401003"]);
        Assert.Equal(new GameReference("401004", null, null, null), references["401004"]);
    }

    [Fact]
    public async Task PostseasonReferenceDataUsesTheLastRegularSeasonCfpPoll()
    {
        handler
            .WithFixture(PostseasonGamesPath, "games-2024-postseason.json")
            .WithResponse(PostseasonLinesPath, () => Json("[]"))
            .WithFixture(RegularRankingsPath, "rankings-2024-regular.json");

        var reference = Assert.Single(await dataSource.GetWeekReferenceDataAsync(2024, WeekKey.Postseason, CancellationToken));

        Assert.Contains(handler.Requests, request => request.RequestUri!.PathAndQuery == RegularRankingsPath);
        // Week 15 is the latest poll week; its CFP ranking wins over AP. Texas is home, Ohio State away.
        Assert.Equal(new GameReference("402001", null, 3, 8), reference);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HttpErrorsBecomeDataSourceUnavailable(HttpStatusCode statusCode)
    {
        handler.WithResponse(Week5GamesPath, () => new HttpResponseMessage(statusCode));

        var exception = await Assert.ThrowsAsync<DataSourceUnavailableException>(
            () => dataSource.GetWeekResultsAsync(2024, WeekKey.Regular(5), CancellationToken));

        Assert.Contains(((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeoutsBecomeDataSourceUnavailable()
    {
        handler.WithResponse(Week5GamesPath, () => throw new TaskCanceledException("timed out", new TimeoutException()));

        await Assert.ThrowsAsync<DataSourceUnavailableException>(
            () => dataSource.GetWeekResultsAsync(2024, WeekKey.Regular(5), CancellationToken));
    }

    [Fact]
    public async Task ConnectionFailuresBecomeDataSourceUnavailable()
    {
        handler.WithResponse(SchedulePath, () => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<DataSourceUnavailableException>(() => dataSource.GetSeasonScheduleAsync(2024, CancellationToken));
    }

    [Fact]
    public async Task UnreadableJsonBecomesDataSourceUnavailable()
    {
        handler.WithResponse(SchedulePath, () => Json("{ not json"));

        await Assert.ThrowsAsync<DataSourceUnavailableException>(() => dataSource.GetSeasonScheduleAsync(2024, CancellationToken));
    }

    [Fact]
    public async Task ReferenceDataFailsWhenAnyCallFails()
    {
        handler
            .WithFixture(Week5GamesPath, "games-2024-regular-week5.json")
            .WithResponse(Week5LinesPath, () => new HttpResponseMessage(HttpStatusCode.TooManyRequests))
            .WithFixture(Week5RankingsPath, "rankings-2024-regular-week5.json");

        await Assert.ThrowsAsync<DataSourceUnavailableException>(
            () => dataSource.GetWeekReferenceDataAsync(2024, WeekKey.Regular(5), CancellationToken));
    }

    [Fact]
    public async Task DoesNotRetryFailedCalls()
    {
        handler.WithResponse(Week5GamesPath, () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<DataSourceUnavailableException>(
            () => dataSource.GetWeekResultsAsync(2024, WeekKey.Regular(5), CancellationToken));

        Assert.Single(handler.Requests);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}
