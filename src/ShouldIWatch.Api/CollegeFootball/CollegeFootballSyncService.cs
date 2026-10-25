using ShouldIWatch.Api.CollegeFootball.Data;
using ShouldIWatch.Api.CollegeFootball.Sources;

namespace ShouldIWatch.Api.CollegeFootball;

internal enum SyncOutcome
{
    /// <summary>No provider call was due, or another request holds the throttle.</summary>
    UpToDate,

    Fetched,

    /// <summary>A provider call failed; the cache is unchanged and the throttle still applies.</summary>
    Failed,
}

/// <summary>
/// Decides from cached data and the current time whether a provider call is due, and if so fetches and stores it.
/// The API calls it on demand; a scheduled job could call it too. Callers then read only from SQL.
/// </summary>
internal sealed partial class CollegeFootballSyncService(
    ICollegeFootballDataSource dataSource,
    CfbGameRepository games,
    CfbSyncStateRepository syncState,
    TimeProvider timeProvider,
    ILogger<CollegeFootballSyncService> logger)
{
    internal static readonly TimeSpan ScheduleRefreshInterval = TimeSpan.FromDays(1);
    internal static readonly TimeSpan EmptyScheduleRetryInterval = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan ResultsThrottle = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan FinishedAfter = TimeSpan.FromHours(3);

    /// <summary>Fetches the season's schedule at most once a day, and only while the season has unfinished games.</summary>
    public async Task<SyncOutcome> EnsureSeasonScheduleAsync(int season, CancellationToken cancellationToken)
    {
        var provider = dataSource.ProviderName;
        var summary = await games.GetSeasonSummaryAsync(provider, season, cancellationToken);
        if (summary.GameCount > 0 && summary.UnfinishedCount == 0)
        {
            return SyncOutcome.UpToDate;
        }

        var now = UtcNow();
        // With nothing cached there is nothing to show, so a failed first fetch is retried sooner than a day.
        var window = summary.GameCount == 0 ? EmptyScheduleRetryInterval : ScheduleRefreshInterval;
        await syncState.EnsureSeasonAsync(season, cancellationToken);
        if (!await syncState.TryClaimScheduleAsync(season, now, window, cancellationToken))
        {
            return SyncOutcome.UpToDate;
        }

        try
        {
            var schedule = await dataSource.GetSeasonScheduleAsync(season, cancellationToken);
            await games.UpsertScheduleAsync(provider, schedule, now, cancellationToken);
            LogScheduleFetched(season, schedule.Count);
            return SyncOutcome.Fetched;
        }
        catch (DataSourceUnavailableException ex)
        {
            LogFetchFailed(ex, season, "schedule");
            return SyncOutcome.Failed;
        }
    }

    /// <summary>
    /// Fetches a week's results when a cached game is not final and kicked off at least 3 hours ago, at most once per
    /// 15 minutes across all callers. Rankings and lines are fetched with the first results fetch that succeeds.
    /// </summary>
    public async Task<SyncOutcome> EnsureWeekAsync(int season, WeekKey week, CancellationToken cancellationToken)
    {
        var provider = dataSource.ProviderName;
        var now = UtcNow();
        var cached = await games.GetWeekGamesAsync(provider, season, week, cancellationToken);
        if (!cached.Any(game => IsDue(game, now)))
        {
            return SyncOutcome.UpToDate;
        }

        await syncState.EnsureWeekAsync(season, week, cancellationToken);
        var (claimed, needsReferenceData) =
            await syncState.TryClaimWeekResultsAsync(season, week, now, ResultsThrottle, cancellationToken);
        if (!claimed)
        {
            return SyncOutcome.UpToDate;
        }

        var resultsTask = dataSource.GetWeekResultsAsync(season, week, cancellationToken);
        var referenceTask = needsReferenceData
            ? dataSource.GetWeekReferenceDataAsync(season, week, cancellationToken)
            : null;

        var failed = false;
        try
        {
            await games.ApplyResultsAsync(provider, await resultsTask, now, cancellationToken);
        }
        catch (DataSourceUnavailableException ex)
        {
            LogFetchFailed(ex, season, $"week {week} results");
            failed = true;
        }

        if (referenceTask is not null)
        {
            try
            {
                await games.ApplyReferenceDataAsync(provider, await referenceTask, now, cancellationToken);
                await syncState.MarkWeekReferenceFetchedAsync(season, week, now, cancellationToken);
            }
            catch (DataSourceUnavailableException ex)
            {
                LogFetchFailed(ex, season, $"week {week} reference data");
                failed = true;
            }
        }

        return failed ? SyncOutcome.Failed : SyncOutcome.Fetched;
    }

    /// <summary>
    /// Not final and kicked off at least 3 hours ago. A kickoff time to be decided counts from 00:00 UTC on its date.
    /// </summary>
    internal static bool IsDue(CachedGame game, DateTime now)
    {
        var kickoff = game.StartTimeTbd ? game.StartDate.Date : game.StartDate;
        return !game.Completed && kickoff <= now - FinishedAfter;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    [LoggerMessage(Level = LogLevel.Information, Message = "Fetched the {Season} schedule: {GameCount} games")]
    private partial void LogScheduleFetched(int season, int gameCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not fetch the {Season} {What}; answering from the cache")]
    private partial void LogFetchFailed(Exception exception, int season, string what);
}
