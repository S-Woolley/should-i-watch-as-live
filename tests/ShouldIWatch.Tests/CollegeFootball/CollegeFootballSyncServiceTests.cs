using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShouldIWatch.Api.CollegeFootball;
using ShouldIWatch.Api.CollegeFootball.Data;
using ShouldIWatch.Api.CollegeFootball.Sources;
using ShouldIWatch.Tests.Infrastructure;

namespace ShouldIWatch.Tests.CollegeFootball;

/// <summary>
/// Sync rules with a <see cref="FakeTimeProvider"/> and <see cref="FakeCollegeFootballDataSource"/>, asserting on the
/// provider calls the fake records. Cached state lives in the test database; every test uses its own season and
/// provider name.
/// </summary>
public sealed class CollegeFootballSyncServiceTests(AppHostFixture fixture) : IAsyncDisposable
{
    private static readonly WeekKey Week = WeekKey.Regular(6);

    private readonly int season = TestIds.NewSeason();
    private readonly FakeCollegeFootballDataSource fake = new(TestIds.NewProvider());
    private readonly List<SqlConnection> connections = [];
    private FakeTimeProvider time = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // A game day in week 6 of the test season, noon UTC.
    private DateTime Saturday => new(season, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    // ---- No provider calls when no game can have finished ----

    [Fact]
    public async Task FutureWeekMakesNoResultsCall()
    {
        await GivenCachedWeekAsync(kickoffs: [Saturday.AddHours(1), Saturday.AddHours(5)]);

        Assert.Equal(SyncOutcome.UpToDate, await Service().EnsureWeekAsync(season, Week, CancellationToken));

        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task GamesStillInProgressMakeNoResultsCall()
    {
        await GivenCachedWeekAsync(kickoffs: [Saturday.AddHours(-2), Saturday.AddHours(-3).AddSeconds(1)]);

        Assert.Equal(SyncOutcome.UpToDate, await Service().EnsureWeekAsync(season, Week, CancellationToken));

        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task GameThatProbablyFinishedIsFetchedBeforeResponding()
    {
        await GivenCachedWeekAsync(kickoffs: [Saturday.AddHours(-3), Saturday.AddHours(1)]);
        fake.Results[Week] = [new GameResult("g0", true, 31, 28, false)];

        Assert.Equal(SyncOutcome.Fetched, await Service().EnsureWeekAsync(season, Week, CancellationToken));

        Assert.Equivalent(new[] { Call(FakeCollegeFootballDataSource.ResultsCall), Call(FakeCollegeFootballDataSource.ReferenceCall) },
            fake.Calls, strict: true);
        var cached = await CachedWeekAsync();
        Assert.True(cached.Single(game => game.ProviderGameId == "g0").Completed);
        Assert.Equal(31, cached.Single(game => game.ProviderGameId == "g0").HomePoints);
    }

    [Fact]
    public async Task FullyCompletedWeekIsNeverFetchedAgain()
    {
        await GivenCachedWeekAsync(kickoffs: [Saturday.AddHours(-4), Saturday.AddHours(-3)]);
        fake.Results[Week] = [new GameResult("g0", true, 31, 28, false), new GameResult("g1", true, 10, 3, false)];
        await Service().EnsureWeekAsync(season, Week, CancellationToken);
        fake.ClearCalls();

        foreach (var later in new[] { TimeSpan.FromMinutes(16), TimeSpan.FromDays(1), TimeSpan.FromDays(60) })
        {
            time.SetUtcNow(Saturday + later);
            Assert.Equal(SyncOutcome.UpToDate, await Service().EnsureWeekAsync(season, Week, CancellationToken));
        }

        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task KickoffTimeToBeDecidedCountsFromMidnightUtcOnItsDate()
    {
        // TBD games are stored with a placeholder time; the date is what counts.
        await GivenCachedWeekAsync(kickoffs: [Saturday.Date.AddHours(20)], tbd: true, now: Saturday.Date.AddHours(3).AddSeconds(-1));

        await Service().EnsureWeekAsync(season, Week, CancellationToken);
        Assert.Empty(fake.Calls);

        time.SetUtcNow(Saturday.Date.AddHours(3));
        Assert.Equal(SyncOutcome.Fetched, await Service().EnsureWeekAsync(season, Week, CancellationToken));
        Assert.Contains(Call(FakeCollegeFootballDataSource.ResultsCall), fake.Calls);
    }

    // ---- Shared refresh throttle ----

    [Fact]
    public async Task ManyVisitorsAtOnceMakeAtMostOneResultsFetch()
    {
        await GivenCachedWeekAsync(kickoffs: [Saturday.AddHours(-4)]);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => Service().EnsureWeekAsync(season, Week, CancellationToken), CancellationToken)));

        Assert.Single(fake.Calls, call => call.Method == FakeCollegeFootballDataSource.ResultsCall);
        Assert.Single(outcomes, outcome => outcome == SyncOutcome.Fetched);
        Assert.All(outcomes.Where(outcome => outcome != SyncOutcome.Fetched), outcome => Assert.Equal(SyncOutcome.UpToDate, outcome));
    }

    [Fact]
    public async Task ThrottleWindowPassingAllowsTheNextFetch()
    {
        await GivenCachedWeekAsync(kickoffs: [Saturday.AddHours(-4)]);
        fake.Results[Week] = [new GameResult("g0", false, null, null, false)]; // still not final
        await Service().EnsureWeekAsync(season, Week, CancellationToken);
        fake.ClearCalls();

        time.SetUtcNow(Saturday.AddMinutes(14));
        Assert.Equal(SyncOutcome.UpToDate, await Service().EnsureWeekAsync(season, Week, CancellationToken));
        Assert.Empty(fake.Calls);

        time.SetUtcNow(Saturday.AddMinutes(15).AddSeconds(1));
        Assert.Equal(SyncOutcome.Fetched, await Service().EnsureWeekAsync(season, Week, CancellationToken));
        Assert.Equal([Call(FakeCollegeFootballDataSource.ResultsCall)], fake.Calls);
    }

    // ---- Season schedule and weekly reference data ----

    [Fact]
    public async Task ScheduleIsFetchedOnceAndKnownWithoutPerWeekCalls()
    {
        fake.Schedule.Add(Scheduled("s1", WeekKey.Regular(1), Saturday.AddDays(-35)));
        fake.Schedule.Add(Scheduled("s2", Week, Saturday.AddHours(30))); // still in the future after 23 hours
        time = new FakeTimeProvider(Saturday);

        Assert.Equal(SyncOutcome.Fetched, await Service().EnsureSeasonScheduleAsync(season, CancellationToken));
        Assert.Equal([Call(FakeCollegeFootballDataSource.ScheduleCall, week: null)], fake.Calls);
        fake.ClearCalls();

        time.Advance(TimeSpan.FromHours(23));
        Assert.Equal(SyncOutcome.UpToDate, await Service().EnsureSeasonScheduleAsync(season, CancellationToken));
        await Service().EnsureWeekAsync(season, Week, CancellationToken);
        Assert.Empty(fake.Calls);
        Assert.Single(await CachedWeekAsync());
    }

    [Fact]
    public async Task KickoffChangeIsPickedUpWithinADay()
    {
        var game = Scheduled("s1", Week, Saturday.AddDays(5));
        fake.Schedule.Add(game);
        time = new FakeTimeProvider(Saturday);
        await Service().EnsureSeasonScheduleAsync(season, CancellationToken);

        fake.Schedule[0] = game with { KickoffUtc = Saturday.AddDays(5).AddHours(4) };
        time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(SyncOutcome.Fetched, await Service().EnsureSeasonScheduleAsync(season, CancellationToken));

        Assert.Equal(Saturday.AddDays(5).AddHours(4), Assert.Single(await CachedWeekAsync()).StartDate);
    }

    [Fact]
    public async Task ScheduleIsNotRefreshedOnceEveryGameIsFinal()
    {
        await GivenCachedWeekAsync(kickoffs: [Saturday.AddHours(-5)]);
        fake.Results[Week] = [new GameResult("g0", true, 20, 17, false)];
        await Service().EnsureWeekAsync(season, Week, CancellationToken);
        fake.ClearCalls();

        time.Advance(TimeSpan.FromDays(3));
        Assert.Equal(SyncOutcome.UpToDate, await Service().EnsureSeasonScheduleAsync(season, CancellationToken));

        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task RankingsAndLinesAreFetchedOnceThenOnlyResults()
    {
        await GivenCachedWeekAsync(kickoffs: [Saturday.AddHours(-4), Saturday.AddHours(-1)]);
        fake.References[Week] = [new GameReference("g0", -7, 3, null)];

        await Service().EnsureWeekAsync(season, Week, CancellationToken);
        Assert.Contains(Call(FakeCollegeFootballDataSource.ReferenceCall), fake.Calls);
        fake.ClearCalls();

        time.Advance(TimeSpan.FromMinutes(20));
        await Service().EnsureWeekAsync(season, Week, CancellationToken);

        Assert.Equal([Call(FakeCollegeFootballDataSource.ResultsCall)], fake.Calls);
        var cached = (await CachedWeekAsync()).Single(game => game.ProviderGameId == "g0");
        Assert.Equal(-7, cached.HomeSpread);
        Assert.Equal(3, cached.HomeRank);
        Assert.Null(cached.AwayRank);
    }

    // ---- Graceful provider failure ----

    [Fact]
    public async Task ProviderFailureIsReportedAndTheCacheIsKept()
    {
        await GivenCachedWeekAsync(kickoffs: [Saturday.AddHours(-4)]);
        fake.Unavailable = true;

        Assert.Equal(SyncOutcome.Failed, await Service().EnsureWeekAsync(season, Week, CancellationToken));

        Assert.Single(await CachedWeekAsync());
    }

    [Fact]
    public async Task FailureDoesNotTriggerRapidRetries()
    {
        await GivenCachedWeekAsync(kickoffs: [Saturday.AddHours(-4)]);
        fake.Unavailable = true;
        await Service().EnsureWeekAsync(season, Week, CancellationToken);
        fake.ClearCalls();
        fake.Unavailable = false;

        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(SyncOutcome.UpToDate, await Service().EnsureWeekAsync(season, Week, CancellationToken));
        Assert.Empty(fake.Calls);

        time.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(SyncOutcome.Fetched, await Service().EnsureWeekAsync(season, Week, CancellationToken));
        // The failed first fetch never stored reference data, so it is fetched now.
        Assert.Contains(Call(FakeCollegeFootballDataSource.ReferenceCall), fake.Calls);
    }

    [Fact]
    public async Task ScheduleFailureWithNothingCachedIsReportedAndRetriedAfterFifteenMinutes()
    {
        time = new FakeTimeProvider(Saturday);
        fake.Unavailable = true;

        Assert.Equal(SyncOutcome.Failed, await Service().EnsureSeasonScheduleAsync(season, CancellationToken));
        Assert.Empty(await CachedWeekAsync());

        fake.Unavailable = false;
        fake.Schedule.Add(Scheduled("s1", Week, Saturday.AddHours(2)));
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(SyncOutcome.UpToDate, await Service().EnsureSeasonScheduleAsync(season, CancellationToken));

        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(SyncOutcome.Fetched, await Service().EnsureSeasonScheduleAsync(season, CancellationToken));
        Assert.Single(await CachedWeekAsync());
    }

    private CollegeFootballSyncService Service()
    {
        var connection = new SqlConnection(fixture.TestDatabaseConnectionString);
        lock (connections)
        {
            connections.Add(connection);
        }

        return new CollegeFootballSyncService(
            fake,
            new CfbGameRepository(connection),
            new CfbSyncStateRepository(connection),
            time,
            NullLogger<CollegeFootballSyncService>.Instance);
    }

    /// <summary>
    /// Caches games g0, g1, ... for <see cref="Week"/> through a schedule fetch a week earlier, then sets the clock to
    /// <paramref name="now"/> (default: <see cref="Saturday"/>).
    /// </summary>
    private async Task GivenCachedWeekAsync(DateTime[] kickoffs, bool tbd = false, DateTime? now = null)
    {
        for (var i = 0; i < kickoffs.Length; i++)
        {
            fake.Schedule.Add(Scheduled($"g{i}", Week, kickoffs[i], tbd));
        }

        time = new FakeTimeProvider(Saturday.AddDays(-7));
        Assert.Equal(SyncOutcome.Fetched, await Service().EnsureSeasonScheduleAsync(season, CancellationToken));
        time.SetUtcNow(now ?? Saturday);
        fake.ClearCalls();
    }

    private async Task<IReadOnlyList<CachedGame>> CachedWeekAsync()
    {
        await using var connection = new SqlConnection(fixture.TestDatabaseConnectionString);
        return await new CfbGameRepository(connection).GetWeekGamesAsync(fake.ProviderName, season, Week, CancellationToken);
    }

    private ScheduledGame Scheduled(string id, WeekKey week, DateTime kickoff, bool tbd = false) =>
        new(id, season, week, kickoff, tbd, $"Home {id}", $"Away {id}");

    private DataSourceCall Call(string method) => new(method, season, Week);

    private DataSourceCall Call(string method, WeekKey? week) => new(method, season, week);
}
