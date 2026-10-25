using ShouldIWatch.Api.CollegeFootball.Data;
using ShouldIWatch.Api.CollegeFootball.Scoring;
using ShouldIWatch.Api.CollegeFootball.Sources;

namespace ShouldIWatch.Api.CollegeFootball;

// These response types are the spoiler boundary: they are the only college football types serialized to clients and
// have no fields for points, margins, spreads, ranks, Elo, overtime, excitement or provider ids.

internal sealed record WeeksResponse(int Season, string? DefaultWeek, IReadOnlyList<WeekOption> Weeks);

internal sealed record WeekOption(string Key, string Label);

internal sealed record WeekGamesResponse(int Season, WeekOption Week, bool UpdateFailed, IReadOnlyList<GameSummary> Games);

/// <param name="Status">"final" or "notFinished".</param>
/// <param name="Tier">"mustWatch", "worthIt" or "skip" for final games; null otherwise.</param>
internal sealed record GameSummary(
    int Id,
    string HomeTeam,
    string AwayTeam,
    DateTimeOffset Kickoff,
    bool StartTimeTbd,
    string Status,
    string? Tier);

internal static class CollegeFootballEndpoints
{
    private const string CompletedWeekCacheControl = "public, max-age=86400";
    private const string ChangingWeekCacheControl = "public, max-age=60";

    public static IEndpointRouteBuilder MapCollegeFootball(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/college-football");

        group.MapGet("weeks", GetWeeksAsync).WithName("GetCollegeFootballWeeks");
        group.MapGet("weeks/{key}", GetWeekAsync).WithName("GetCollegeFootballWeek");

        return endpoints;
    }

    private static async Task<WeeksResponse> GetWeeksAsync(
        CollegeFootballSyncService sync,
        CfbGameRepository games,
        ICollegeFootballDataSource dataSource,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var season = SeasonCalendar.CurrentSeason(now);
        await sync.EnsureSeasonScheduleAsync(season, cancellationToken);

        // Games that kicked off long enough ago have probably finished, even if their week hasn't been fetched yet.
        var finishedBefore = now.UtcDateTime - CollegeFootballSyncService.FinishedAfter;
        var weeks = await games.GetWeeksAsync(dataSource.ProviderName, season, finishedBefore, cancellationToken);
        return new WeeksResponse(
            season,
            SeasonCalendar.DefaultWeek(weeks)?.ToString(),
            weeks.Select(week => ToOption(week.Week)).ToList());
    }

    private static async Task<IResult> GetWeekAsync(
        string key,
        HttpContext httpContext,
        CollegeFootballSyncService sync,
        CfbGameRepository games,
        CfbRivalryRepository rivalries,
        ICollegeFootballDataSource dataSource,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!WeekKey.TryParse(key, out var parsed))
        {
            return Results.NotFound();
        }

        var week = parsed.Value;
        var provider = dataSource.ProviderName;
        var season = SeasonCalendar.CurrentSeason(timeProvider.GetUtcNow());

        var scheduleOutcome = await sync.EnsureSeasonScheduleAsync(season, cancellationToken);
        var knownWeeks = await games.GetWeeksAsync(provider, season, finishedBefore: null, cancellationToken);
        if (!knownWeeks.Any(known => known.Week == week))
        {
            return Results.NotFound();
        }

        var weekOutcome = await sync.EnsureWeekAsync(season, week, cancellationToken);
        var cached = await games.GetWeekGamesAsync(provider, season, week, cancellationToken);
        var rivalryPairs = RivalryPairs(await rivalries.GetAllAsync(cancellationToken));

        var summaries = cached.Select(game => ToSummary(game, rivalryPairs)).ToList();
        httpContext.Response.Headers.CacheControl = summaries.Count > 0 && summaries.All(game => game.Status == "final")
            ? CompletedWeekCacheControl
            : ChangingWeekCacheControl;

        return Results.Ok(new WeekGamesResponse(
            season,
            ToOption(week),
            scheduleOutcome == SyncOutcome.Failed || weekOutcome == SyncOutcome.Failed,
            summaries));
    }

    private static WeekOption ToOption(WeekKey week) => new(week.ToString(), week.Label);

    private static GameSummary ToSummary(CachedGame game, HashSet<(string, string)> rivalryPairs)
    {
        // A game is only rated once it is final and both scores are known.
        var tier = game is { Completed: true, HomePoints: { } homePoints, AwayPoints: { } awayPoints }
            ? WatchabilityScorer.Score(new GameFacts(
                homePoints,
                awayPoints,
                game.WentToOvertime,
                game.HomeSpread,
                game.HomePregameElo,
                game.AwayPregameElo,
                game.HomeRank,
                game.AwayRank,
                rivalryPairs.Contains(OrderedPair(game.HomeTeam, game.AwayTeam)),
                game.ExcitementIndex))
            : (Tier?)null;

        return new GameSummary(
            game.Id,
            game.HomeTeam,
            game.AwayTeam,
            new DateTimeOffset(DateTime.SpecifyKind(game.StartDate, DateTimeKind.Utc)),
            game.StartTimeTbd,
            tier is null ? "notFinished" : "final",
            tier switch
            {
                Tier.MustWatch => "mustWatch",
                Tier.WorthIt => "worthIt",
                Tier.Skip => "skip",
                _ => null,
            });
    }

    private static HashSet<(string, string)> RivalryPairs(IEnumerable<Rivalry> rivalries) =>
        rivalries.Select(rivalry => OrderedPair(rivalry.TeamA, rivalry.TeamB)).ToHashSet();

    private static (string, string) OrderedPair(string a, string b) =>
        string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
}
