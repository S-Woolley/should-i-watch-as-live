using Dapper;
using Microsoft.Data.SqlClient;
using ShouldIWatch.Api.CollegeFootball.Sources;

namespace ShouldIWatch.Api.CollegeFootball.Data;

/// <summary>
/// Throttle state for provider calls. A caller <em>claims</em> a fetch with one conditional UPDATE; only the caller
/// whose UPDATE changed a row fetches, which holds across requests and API replicas.
/// </summary>
internal sealed class CfbSyncStateRepository(SqlConnection connection)
{
    public Task EnsureSeasonAsync(int season, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE dbo.cfb_season_sync WITH (HOLDLOCK) AS target
            USING (SELECT @season AS season) AS source ON target.season = source.season
            WHEN NOT MATCHED THEN INSERT (season) VALUES (source.season);
            """,
            new { season },
            cancellationToken: cancellationToken));

    /// <summary>Claims the season schedule fetch unless one was claimed within <paramref name="window"/>.</summary>
    public async Task<bool> TryClaimScheduleAsync(int season, DateTime now, TimeSpan window, CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.cfb_season_sync
            SET schedule_fetched_at = @now
            WHERE season = @season
              AND (schedule_fetched_at IS NULL OR schedule_fetched_at <= DATEADD(second, -@windowSeconds, @now))
            """,
            new { season, now, windowSeconds = (int)window.TotalSeconds },
            cancellationToken: cancellationToken)) == 1;

    public Task EnsureWeekAsync(int season, WeekKey week, CancellationToken cancellationToken)
    {
        var (seasonType, weekNumber) = WeekStorage.FromWeekKey(week);
        return connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE dbo.cfb_week_sync WITH (HOLDLOCK) AS target
            USING (SELECT @season AS season, @seasonType AS season_type, @weekNumber AS week) AS source
            ON target.season = source.season AND target.season_type = source.season_type AND target.week = source.week
            WHEN NOT MATCHED THEN INSERT (season, season_type, week) VALUES (source.season, source.season_type, source.week);
            """,
            new { season, seasonType, weekNumber },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Claims a results fetch for the week unless one was claimed within <paramref name="window"/>. Returns whether
    /// the claim succeeded and whether the week's reference data is still to be fetched.
    /// </summary>
    public async Task<(bool Claimed, bool NeedsReferenceData)> TryClaimWeekResultsAsync(
        int season, WeekKey week, DateTime now, TimeSpan window, CancellationToken cancellationToken)
    {
        var (seasonType, weekNumber) = WeekStorage.FromWeekKey(week);
        var needsReference = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            """
            UPDATE dbo.cfb_week_sync
            SET results_fetched_at = @now
            OUTPUT CAST(CASE WHEN inserted.reference_fetched_at IS NULL THEN 1 ELSE 0 END AS bit)
            WHERE season = @season AND season_type = @seasonType AND week = @weekNumber
              AND (results_fetched_at IS NULL OR results_fetched_at <= DATEADD(second, -@windowSeconds, @now))
            """,
            new { season, seasonType, weekNumber, now, windowSeconds = (int)window.TotalSeconds },
            cancellationToken: cancellationToken));
        return (needsReference is not null, needsReference ?? false);
    }

    public Task MarkWeekReferenceFetchedAsync(int season, WeekKey week, DateTime now, CancellationToken cancellationToken)
    {
        var (seasonType, weekNumber) = WeekStorage.FromWeekKey(week);
        return connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.cfb_week_sync
            SET reference_fetched_at = @now
            WHERE season = @season AND season_type = @seasonType AND week = @weekNumber
            """,
            new { season, seasonType, weekNumber, now },
            cancellationToken: cancellationToken));
    }
}
