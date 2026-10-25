extern alias api;

using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using ShouldIWatch.Api.CollegeFootball.Sources;
using ShouldIWatch.Tests.Infrastructure;

namespace ShouldIWatch.Tests.CollegeFootball;

/// <summary>
/// The watchability endpoints in-process, against the test database, with a fake data source and clock. Each test
/// sets the clock in its own season, so tests never share cached data.
/// </summary>
public sealed class CollegeFootballEndpointTests(AppHostFixture fixture) : IAsyncDisposable
{
    private const string ApiKey = "endpoint-test-secret-key";

    private static readonly string[] ResponsePropertyNames = ["season", "week", "updateFailed", "games"];
    private static readonly string[] WeekPropertyNames = ["key", "label"];
    private static readonly string[] GamePropertyNames = ["id", "homeTeam", "awayTeam", "kickoff", "startTimeTbd", "status", "tier"];

    private readonly int season = TestIds.NewSeason();
    private readonly FakeCollegeFootballDataSource fake = new(TestIds.NewProvider());
    private readonly List<WebApplicationFactory<api::Program>> factories = [];
    private FakeTimeProvider time = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private DateTime Week5Saturday => new(season, 9, 28, 16, 0, 0, DateTimeKind.Utc);

    public async ValueTask DisposeAsync()
    {
        foreach (var factory in factories)
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task WeeksListsRegularWeeksThenPostseasonWithTheMostRecentCompletedWeekAsDefault()
    {
        GivenSeasonSchedule();
        fake.Results[WeekKey.Regular(1)] = [new GameResult("w1", true, 52, 3, false)];
        time = new FakeTimeProvider(Week5Saturday.AddDays(-20)); // week 1 is over
        using var client = Client();
        await client.GetAsync("/api/college-football/weeks/1", CancellationToken);

        using var weeks = await GetJsonAsync(client, "/api/college-football/weeks");

        Assert.Equal(season, weeks.RootElement.GetProperty("season").GetInt32());
        Assert.Equal("1", weeks.RootElement.GetProperty("defaultWeek").GetString());
        Assert.Equal(
            [("1", "Week 1"), ("5", "Week 5"), ("postseason", "Postseason")],
            weeks.RootElement.GetProperty("weeks").EnumerateArray()
                .Select(week => (week.GetProperty("key").GetString(), week.GetProperty("label").GetString())));
    }

    [Fact]
    public async Task DefaultWeekIsTheLatestFinishedWeekEvenBeforeItsResultsAreFetched()
    {
        GivenSeasonSchedule();
        time = new FakeTimeProvider(Week5Saturday.AddHours(3)); // week 5's early game has finished
        using var client = Client();

        using var weeks = await GetJsonAsync(client, "/api/college-football/weeks");

        Assert.Equal("5", weeks.RootElement.GetProperty("defaultWeek").GetString());
        // Only the schedule was fetched; listing weeks never fetches results.
        Assert.All(fake.Calls, call => Assert.Equal(FakeCollegeFootballDataSource.ScheduleCall, call.Method));
    }

    [Fact]
    public async Task WeeksDefaultsToTheFirstWeekBeforeAnyGameHasCompleted()
    {
        GivenSeasonSchedule();
        time = new FakeTimeProvider(Week5Saturday.AddDays(-40));
        using var client = Client();

        using var weeks = await GetJsonAsync(client, "/api/college-football/weeks");

        Assert.Equal("1", weeks.RootElement.GetProperty("defaultWeek").GetString());
    }

    [Fact]
    public async Task WeekReturnsGamesInKickoffOrderWithTiersAndInternalIdsOnly()
    {
        GivenSeasonSchedule();
        GivenWeek5Results();
        time = new FakeTimeProvider(Week5Saturday.AddHours(12));
        using var client = Client();

        using var response = await client.GetAsync("/api/college-football/weeks/5", CancellationToken);
        response.EnsureSuccessStatusCode();
        using var week = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        var games = week.RootElement.GetProperty("games").EnumerateArray().ToList();
        Assert.Equal(["Early", "Middle", "Late", "Night"], games.Select(game => game.GetProperty("homeTeam").GetString()));
        Assert.Equal(
            ["skip", "mustWatch", "worthIt", null],
            games.Select(game => game.GetProperty("tier").GetString()));
        Assert.Equal(["final", "final", "final", "notFinished"], games.Select(game => game.GetProperty("status").GetString()));
        Assert.Equal(Week5Saturday, games[0].GetProperty("kickoff").GetDateTimeOffset().UtcDateTime);
        Assert.All(games, game => Assert.DoesNotContain("w5", game.GetProperty("id").GetRawText(), StringComparison.Ordinal));
        Assert.Equal(games.Count, games.Select(game => game.GetProperty("id").GetInt32()).Distinct().Count());
        Assert.False(week.RootElement.GetProperty("updateFailed").GetBoolean());
        Assert.Equal("5", week.RootElement.GetProperty("week").GetProperty("key").GetString());
        Assert.Equal("public, max-age=60", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task WeekResponseContainsOnlyIdentityTeamKickoffAndTierFields()
    {
        GivenSeasonSchedule();
        GivenWeek5Results();
        time = new FakeTimeProvider(Week5Saturday.AddHours(12));
        using var client = Client();

        var body = await client.GetStringAsync("/api/college-football/weeks/5", CancellationToken);
        using var week = JsonDocument.Parse(body);

        Assert.Equal(ResponsePropertyNames.Order(), PropertyNames(week.RootElement).Order());
        Assert.Equal(WeekPropertyNames.Order(), PropertyNames(week.RootElement.GetProperty("week")).Order());
        var games = week.RootElement.GetProperty("games").EnumerateArray().ToList();
        Assert.NotEmpty(games);
        Assert.All(games, game => Assert.Equal(GamePropertyNames.Order(), PropertyNames(game).Order()));

        // No provider ids, provider name or API key anywhere in the body.
        foreach (var leaked in new[] { "w5-", fake.ProviderName, ApiKey })
        {
            Assert.DoesNotContain(leaked, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CompletedWeekIsCacheableForADay()
    {
        GivenSeasonSchedule();
        fake.Results[WeekKey.Regular(1)] = [new GameResult("w1", true, 52, 3, false)];
        time = new FakeTimeProvider(Week5Saturday.AddDays(-20));
        using var client = Client();

        using var response = await client.GetAsync("/api/college-football/weeks/1", CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal("public, max-age=86400", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task FutureWeekIsCacheableForSixtySecondsAndMakesNoResultsCall()
    {
        GivenSeasonSchedule();
        time = new FakeTimeProvider(Week5Saturday.AddDays(-2));
        using var client = Client();

        using var response = await client.GetAsync("/api/college-football/weeks/5", CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal("public, max-age=60", response.Headers.CacheControl?.ToString());
        Assert.DoesNotContain(fake.Calls, call => call.Method != FakeCollegeFootballDataSource.ScheduleCall);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    [InlineData("week5")]
    [InlineData("Postseason")]
    public async Task UnknownWeekIsNotFound(string key)
    {
        GivenSeasonSchedule();
        time = new FakeTimeProvider(Week5Saturday);
        using var client = Client();

        using var response = await client.GetAsync($"/api/college-football/weeks/{key}", CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProviderFailureAnswersFromTheCacheWithUpdateFailed()
    {
        GivenSeasonSchedule();
        GivenWeek5Results(nightGameFinal: false);
        time = new FakeTimeProvider(Week5Saturday.AddHours(8));
        using var client = Client();
        await client.GetAsync("/api/college-football/weeks/5", CancellationToken);

        fake.Unavailable = true;
        time.Advance(TimeSpan.FromHours(4)); // night game is now due, and the throttle has passed
        using var response = await client.GetAsync("/api/college-football/weeks/5", CancellationToken);
        response.EnsureSuccessStatusCode();
        using var week = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        Assert.True(week.RootElement.GetProperty("updateFailed").GetBoolean());
        Assert.Equal(
            ["skip", "mustWatch", "worthIt", null],
            week.RootElement.GetProperty("games").EnumerateArray().Select(game => game.GetProperty("tier").GetString()));
        Assert.Equal("public, max-age=60", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task ProviderFailureWithNothingCachedReturnsNoWeeks()
    {
        fake.Unavailable = true;
        time = new FakeTimeProvider(Week5Saturday);
        using var client = Client();

        using var weeks = await GetJsonAsync(client, "/api/college-football/weeks");

        Assert.Empty(weeks.RootElement.GetProperty("weeks").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, weeks.RootElement.GetProperty("defaultWeek").ValueKind);
        using var week = await client.GetAsync("/api/college-football/weeks/5", CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, week.StatusCode);
    }

    private void GivenSeasonSchedule()
    {
        var week1 = new DateTime(season, 8, 31, 16, 0, 0, DateTimeKind.Utc);
        fake.Schedule.AddRange(
        [
            new ScheduledGame("w1", season, WeekKey.Regular(1), week1, false, "Opener", "Visitor"),
            // Deliberately out of kickoff order.
            new ScheduledGame("w5-night", season, WeekKey.Regular(5), Week5Saturday.AddHours(8), false, "Night", "Owls"),
            new ScheduledGame("w5-middle", season, WeekKey.Regular(5), Week5Saturday.AddHours(3.5), false, "Middle", "Rival"),
            new ScheduledGame("w5-early", season, WeekKey.Regular(5), Week5Saturday, false, "Early", "Visitor"),
            new ScheduledGame("w5-late", season, WeekKey.Regular(5), Week5Saturday.AddHours(4), false, "Late", "Visitor"),
            new ScheduledGame("bowl", season, WeekKey.Postseason, new DateTime(season + 1, 1, 1, 17, 0, 0, DateTimeKind.Utc), false, "Bowl", "Game"),
        ]);
    }

    private void GivenWeek5Results(bool nightGameFinal = false)
    {
        var week = WeekKey.Regular(5);
        fake.Results[week] =
        [
            new GameResult("w5-early", true, 45, 10, false, ExcitementIndex: 0.5),  // blowout: skip
            new GameResult("w5-middle", true, 31, 28, true, ExcitementIndex: 6.5),  // OT thriller: must watch
            new GameResult("w5-late", true, 24, 18, false),                         // one-score game: worth it
            new GameResult("w5-night", nightGameFinal, null, null, false),
        ];
        fake.References[week] = [new GameReference("w5-middle", -3, 4, 11)];
    }

    private HttpClient Client()
    {
        var factory = new WebApplicationFactory<api::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:appdb", fixture.TestDatabaseConnectionString);
            builder.UseSetting("Cfbd:ApiKey", ApiKey);
            builder.UseSetting("Cfbd:BaseUrl", "http://127.0.0.1:9/");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICollegeFootballDataSource>();
                services.AddSingleton<ICollegeFootballDataSource>(fake);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(time);
            });
        });
        factories.Add(factory);
        return factory.CreateClient();
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path) =>
        JsonDocument.Parse(await client.GetStringAsync(path, CancellationToken));

    private static IEnumerable<string> PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name);
}
