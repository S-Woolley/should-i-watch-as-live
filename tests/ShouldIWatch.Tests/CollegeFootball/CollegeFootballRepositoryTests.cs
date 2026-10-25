using Dapper;
using Microsoft.Data.SqlClient;
using ShouldIWatch.Api.CollegeFootball.Data;
using ShouldIWatch.Api.CollegeFootball.Sources;
using ShouldIWatch.Tests.Infrastructure;

namespace ShouldIWatch.Tests.CollegeFootball;

/// <summary>Runs against the test database on the AppHost's SQL Server.</summary>
public sealed class CollegeFootballRepositoryTests(AppHostFixture fixture)
{
    private static readonly DateTime Now = new(2030, 10, 4, 18, 0, 0, DateTimeKind.Utc);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnlyOneOfTwoConcurrentWeekClaimsSucceeds()
    {
        var season = TestIds.NewSeason();
        for (var weekNumber = 1; weekNumber <= 10; weekNumber++)
        {
            var week = WeekKey.Regular(weekNumber);
            await using (var setup = Connection())
            {
                await new CfbSyncStateRepository(setup).EnsureWeekAsync(season, week, CancellationToken);
            }

            var claims = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var connection = Connection();
                return await new CfbSyncStateRepository(connection)
                    .TryClaimWeekResultsAsync(season, week, Now, TimeSpan.FromMinutes(15), CancellationToken);
            }));

            Assert.Single(claims, claim => claim.Claimed);
        }
    }

    [Fact]
    public async Task OnlyOneOfTwoConcurrentScheduleClaimsSucceeds()
    {
        for (var i = 0; i < 10; i++)
        {
            var season = TestIds.NewSeason();
            var claims = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var connection = Connection();
                var repository = new CfbSyncStateRepository(connection);
                await repository.EnsureSeasonAsync(season, CancellationToken);
                return await repository.TryClaimScheduleAsync(season, Now, TimeSpan.FromDays(1), CancellationToken);
            }));

            Assert.Single(claims, claimed => claimed);
            await using var check = Connection();
            Assert.Equal(1, await check.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.cfb_season_sync WHERE season = @season", new { season }));
        }
    }

    [Fact]
    public async Task WeekClaimIsRefusedWithinTheWindowAndGrantedAfterIt()
    {
        var season = TestIds.NewSeason();
        var week = WeekKey.Postseason;
        await using var connection = Connection();
        var repository = new CfbSyncStateRepository(connection);
        var window = TimeSpan.FromMinutes(15);
        await repository.EnsureWeekAsync(season, week, CancellationToken);
        await repository.EnsureWeekAsync(season, week, CancellationToken);

        Assert.Equal((true, true), await repository.TryClaimWeekResultsAsync(season, week, Now, window, CancellationToken));
        Assert.False((await repository.TryClaimWeekResultsAsync(season, week, Now.AddMinutes(14), window, CancellationToken)).Claimed);

        await repository.MarkWeekReferenceFetchedAsync(season, week, Now, CancellationToken);
        Assert.Equal((true, false), await repository.TryClaimWeekResultsAsync(season, week, Now.AddMinutes(16), window, CancellationToken));
    }

    [Fact]
    public async Task ReupsertingTheSameProviderGameUpdatesRatherThanDuplicates()
    {
        var provider = TestIds.NewProvider();
        var season = TestIds.NewSeason();
        var week = WeekKey.Regular(6);
        await using var connection = Connection();
        var games = new CfbGameRepository(connection);

        var original = new ScheduledGame("777", season, week, Now, true, "Oregon", "Oregon State");
        await games.UpsertScheduleAsync(provider, [original], Now, CancellationToken);
        var moved = original with { KickoffUtc = Now.AddHours(3.5), StartTimeTbd = false };
        await games.UpsertScheduleAsync(provider, [moved, new ScheduledGame("778", season, week, Now, false, "Iowa", "Iowa State")],
            Now.AddDays(1), CancellationToken);

        var cached = await games.GetWeekGamesAsync(provider, season, week, CancellationToken);
        Assert.Equal(2, cached.Count);
        var game = Assert.Single(cached, g => g.ProviderGameId == "777");
        Assert.Equal(Now.AddHours(3.5), game.StartDate);
        Assert.False(game.StartTimeTbd);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.cfb_game WHERE provider = @provider AND provider_game_id = '777'", new { provider }));
    }

    [Fact]
    public async Task SameProviderGameIdFromAnotherProviderIsASeparateGame()
    {
        var season = TestIds.NewSeason();
        var week = WeekKey.Regular(2);
        var (providerA, providerB) = (TestIds.NewProvider(), TestIds.NewProvider());
        await using var connection = Connection();
        var games = new CfbGameRepository(connection);

        await games.UpsertScheduleAsync(providerA, [new ScheduledGame("1", season, week, Now, false, "A", "B")], Now, CancellationToken);
        await games.UpsertScheduleAsync(providerB, [new ScheduledGame("1", season, week, Now, false, "C", "D")], Now, CancellationToken);

        Assert.Equal("A", Assert.Single(await games.GetWeekGamesAsync(providerA, season, week, CancellationToken)).HomeTeam);
        Assert.Equal("C", Assert.Single(await games.GetWeekGamesAsync(providerB, season, week, CancellationToken)).HomeTeam);
    }

    [Fact]
    public async Task ResultsAndReferenceDataAreAppliedToCachedGames()
    {
        var provider = TestIds.NewProvider();
        var season = TestIds.NewSeason();
        var week = WeekKey.Regular(9);
        await using var connection = Connection();
        var games = new CfbGameRepository(connection);
        await games.UpsertScheduleAsync(provider,
        [
            new ScheduledGame("1", season, week, Now.AddHours(2), false, "Home2", "Away2"),
            new ScheduledGame("2", season, week, Now, false, "Home1", "Away1"),
            new ScheduledGame("3", season, WeekKey.Postseason, Now, false, "Bowl", "Game"),
        ], Now, CancellationToken);

        await games.ApplyResultsAsync(provider,
        [
            new GameResult("2", true, 24, 21, true, 1700, 1650, 4.25),
            new GameResult("not-cached", true, 1, 0, false),
        ], Now, CancellationToken);
        await games.ApplyReferenceDataAsync(provider, [new GameReference("2", -3.5, 7, null)], Now, CancellationToken);

        var cached = await games.GetWeekGamesAsync(provider, season, week, CancellationToken);
        Assert.Equal(["2", "1"], cached.Select(game => game.ProviderGameId)); // kickoff order
        var first = cached[0];
        Assert.Equal(
            first with
            {
                Completed = true, HomePoints = 24, AwayPoints = 21, WentToOvertime = true, HomePregameElo = 1700,
                AwayPregameElo = 1650, ExcitementIndex = 4.25, HomeSpread = -3.5, HomeRank = 7, AwayRank = null,
            },
            first);
        Assert.False(cached[1].Completed);
        Assert.Null(cached[1].HomePoints);

        Assert.Equal(new SeasonCacheSummary(3, 2), await games.GetSeasonSummaryAsync(provider, season, CancellationToken));
        Assert.Equal(
            [new CachedWeek(week, true), new CachedWeek(WeekKey.Postseason, false)],
            await games.GetWeeksAsync(provider, season, finishedBefore: null, CancellationToken));
        // The unfetched postseason game kicked off at Now, so it counts once Now is before the cutoff.
        Assert.Equal(
            [new CachedWeek(week, true), new CachedWeek(WeekKey.Postseason, true)],
            await games.GetWeeksAsync(provider, season, Now, CancellationToken));
        // Results for games the schedule doesn't know are ignored.
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.cfb_game WHERE provider = @provider AND provider_game_id = 'not-cached'", new { provider }));
    }

    [Fact]
    public async Task RivalriesAreSeeded()
    {
        await using var connection = Connection();

        var rivalries = await new CfbRivalryRepository(connection).GetAllAsync(CancellationToken);

        Assert.Contains(new Rivalry("Michigan", "Ohio State", "The Game"), rivalries);
        Assert.InRange(rivalries.Count, 25, 40);
    }

    private SqlConnection Connection() => new(fixture.TestDatabaseConnectionString);
}
