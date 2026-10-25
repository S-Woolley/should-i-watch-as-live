using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using ShouldIWatch.Api.CollegeFootball.Sources;

namespace ShouldIWatch.Api.CollegeFootball.Data;

/// <summary>A cached game row. Holds result data, so it must never be serialized to a client.</summary>
internal sealed record CachedGame(
    int Id,
    string ProviderGameId,
    DateTime StartDate,
    bool StartTimeTbd,
    string HomeTeam,
    string AwayTeam,
    bool Completed,
    int? HomePoints,
    int? AwayPoints,
    bool WentToOvertime,
    int? HomePregameElo,
    int? AwayPregameElo,
    double? ExcitementIndex,
    double? HomeSpread,
    int? HomeRank,
    int? AwayRank);

/// <param name="HasCompletedGame">
/// A game in the week is final in the cache, or kicked off before the caller's finished-before time (when given). Results are only
/// fetched for weeks someone opens, so kickoff time is what tells which past weeks have finished games.
/// </param>
internal sealed record CachedWeek(WeekKey Week, bool HasCompletedGame);

internal sealed record SeasonCacheSummary(int GameCount, int UnfinishedCount);

// Batches are sent as one JSON parameter and applied with a single set-based statement.
internal sealed class CfbGameRepository(SqlConnection connection)
{
    public Task<SeasonCacheSummary> GetSeasonSummaryAsync(string provider, int season, CancellationToken cancellationToken) =>
        connection.QuerySingleAsync<SeasonCacheSummary>(new CommandDefinition(
            """
            SELECT COUNT(*) AS GameCount,
                   COUNT(CASE WHEN completed = 0 THEN 1 END) AS UnfinishedCount
            FROM dbo.cfb_game
            WHERE provider = @provider AND season = @season
            """,
            new { provider, season },
            cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<CachedWeek>> GetWeeksAsync(
        string provider, int season, DateTime? finishedBefore, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<(string SeasonType, int Week, bool HasCompletedGame)>(new CommandDefinition(
            """
            SELECT season_type, week,
                   CAST(MAX(CASE WHEN completed = 1 OR start_date <= @finishedBefore THEN 1 ELSE 0 END) AS bit)
            FROM dbo.cfb_game
            WHERE provider = @provider AND season = @season
            GROUP BY season_type, week
            """,
            new { provider, season, finishedBefore },
            cancellationToken: cancellationToken));

        return rows
            .Select(row => new CachedWeek(WeekStorage.ToWeekKey(row.SeasonType, row.Week), row.HasCompletedGame))
            .OrderBy(week => week.Week)
            .ToList();
    }

    public async Task<IReadOnlyList<CachedGame>> GetWeekGamesAsync(
        string provider, int season, WeekKey week, CancellationToken cancellationToken)
    {
        var (seasonType, weekNumber) = WeekStorage.FromWeekKey(week);
        var games = await connection.QueryAsync<CachedGame>(new CommandDefinition(
            """
            SELECT id, provider_game_id AS ProviderGameId, start_date AS StartDate, start_time_tbd AS StartTimeTbd,
                   home_team AS HomeTeam, away_team AS AwayTeam, completed, home_points AS HomePoints,
                   away_points AS AwayPoints, went_to_overtime AS WentToOvertime, home_pregame_elo AS HomePregameElo,
                   away_pregame_elo AS AwayPregameElo, excitement_index AS ExcitementIndex, home_spread AS HomeSpread,
                   home_rank AS HomeRank, away_rank AS AwayRank
            FROM dbo.cfb_game
            WHERE provider = @provider AND season = @season AND season_type = @seasonType AND week = @weekNumber
            ORDER BY start_date, id
            """,
            new { provider, season, seasonType, weekNumber },
            cancellationToken: cancellationToken));
        return games.AsList();
    }

    /// <summary>Inserts new games and updates schedule fields of existing ones, keyed on (provider, provider_game_id).</summary>
    public Task UpsertScheduleAsync(
        string provider, IReadOnlyList<ScheduledGame> games, DateTime now, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(games.Select(game =>
        {
            var (seasonType, week) = WeekStorage.FromWeekKey(game.Week);
            return new
            {
                game.ProviderGameId,
                game.Season,
                SeasonType = seasonType,
                Week = week,
                StartDate = AsSqlDateTime(game.KickoffUtc),
                game.StartTimeTbd,
                game.HomeTeam,
                game.AwayTeam,
            };
        }));

        return connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE dbo.cfb_game WITH (HOLDLOCK) AS target
            USING (
                SELECT *
                FROM OPENJSON(@json) WITH (
                    provider_game_id nvarchar(64)  '$.ProviderGameId',
                    season           int           '$.Season',
                    season_type      varchar(16)   '$.SeasonType',
                    week             int           '$.Week',
                    start_date       datetime2     '$.StartDate',
                    start_time_tbd   bit           '$.StartTimeTbd',
                    home_team        nvarchar(100) '$.HomeTeam',
                    away_team        nvarchar(100) '$.AwayTeam')
            ) AS source
            ON target.provider = @provider AND target.provider_game_id = source.provider_game_id
            WHEN MATCHED THEN UPDATE SET
                season = source.season, season_type = source.season_type, week = source.week,
                start_date = source.start_date, start_time_tbd = source.start_time_tbd,
                home_team = source.home_team, away_team = source.away_team, updated_at = @now
            WHEN NOT MATCHED THEN
                INSERT (provider, provider_game_id, season, season_type, week, start_date, start_time_tbd,
                        home_team, away_team, updated_at)
                VALUES (@provider, source.provider_game_id, source.season, source.season_type, source.week,
                        source.start_date, source.start_time_tbd, source.home_team, source.away_team, @now);
            """,
            new { provider, json, now },
            cancellationToken: cancellationToken));
    }

    /// <summary>Applies results to games already in the cache; results for unknown games are ignored.</summary>
    public Task ApplyResultsAsync(
        string provider, IReadOnlyList<GameResult> results, DateTime now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game SET
                completed = source.completed, home_points = source.home_points, away_points = source.away_points,
                went_to_overtime = source.went_to_overtime, home_pregame_elo = source.home_pregame_elo,
                away_pregame_elo = source.away_pregame_elo, excitement_index = source.excitement_index,
                updated_at = @now
            FROM dbo.cfb_game AS game
            JOIN OPENJSON(@json) WITH (
                provider_game_id nvarchar(64) '$.ProviderGameId',
                completed        bit          '$.Completed',
                home_points      int          '$.HomePoints',
                away_points      int          '$.AwayPoints',
                went_to_overtime bit          '$.WentToOvertime',
                home_pregame_elo int          '$.HomePregameElo',
                away_pregame_elo int          '$.AwayPregameElo',
                excitement_index float        '$.ExcitementIndex'
            ) AS source ON game.provider = @provider AND game.provider_game_id = source.provider_game_id
            """,
            new { provider, json = JsonSerializer.Serialize(results), now },
            cancellationToken: cancellationToken));

    public Task ApplyReferenceDataAsync(
        string provider, IReadOnlyList<GameReference> references, DateTime now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game SET
                home_spread = source.home_spread, home_rank = source.home_rank, away_rank = source.away_rank,
                updated_at = @now
            FROM dbo.cfb_game AS game
            JOIN OPENJSON(@json) WITH (
                provider_game_id nvarchar(64) '$.ProviderGameId',
                home_spread      float        '$.HomeSpread',
                home_rank        int          '$.HomeRank',
                away_rank        int          '$.AwayRank'
            ) AS source ON game.provider = @provider AND game.provider_game_id = source.provider_game_id
            """,
            new { provider, json = JsonSerializer.Serialize(references), now },
            cancellationToken: cancellationToken));

    // Without a Kind the JSON has no "Z" suffix, which OPENJSON converts to datetime2 reliably.
    private static DateTime AsSqlDateTime(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
}
