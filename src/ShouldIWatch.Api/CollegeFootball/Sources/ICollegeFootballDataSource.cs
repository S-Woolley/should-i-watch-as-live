namespace ShouldIWatch.Api.CollegeFootball.Sources;

/// <summary>
/// The only boundary between the app and a college football data provider. Implementations return the app's own
/// types; every provider-specific decision (overtime detection, which poll gives a rank, how lines map to a home
/// spread) lives inside the implementation. Failures are reported as <see cref="DataSourceUnavailableException"/>.
/// </summary>
internal interface ICollegeFootballDataSource
{
    /// <summary>A short provider name, stamped into <c>cfb_game.provider</c> so cached games are keyed by provider.</summary>
    string ProviderName { get; }

    /// <summary>Every regular-season and postseason game in the season with its kickoff, but no results.</summary>
    Task<IReadOnlyList<ScheduledGame>> GetSeasonScheduleAsync(int season, CancellationToken cancellationToken);

    Task<IReadOnlyList<GameResult>> GetWeekResultsAsync(int season, WeekKey week, CancellationToken cancellationToken);

    /// <summary>Betting lines and rankings for a week's games.</summary>
    Task<IReadOnlyList<GameReference>> GetWeekReferenceDataAsync(int season, WeekKey week, CancellationToken cancellationToken);
}

internal sealed record ScheduledGame(
    string ProviderGameId,
    int Season,
    WeekKey Week,
    DateTime KickoffUtc,
    bool StartTimeTbd,
    string HomeTeam,
    string AwayTeam);

/// <summary>Final-state facts for a game. Points are null until known; Elo and excitement index are optional signals.</summary>
internal sealed record GameResult(
    string ProviderGameId,
    bool Completed,
    int? HomePoints,
    int? AwayPoints,
    bool WentToOvertime,
    int? HomePregameElo = null,
    int? AwayPregameElo = null,
    double? ExcitementIndex = null);

/// <summary>Reference data fetched once per week. <see cref="HomeSpread"/> is negative when the home team is favored.</summary>
internal sealed record GameReference(
    string ProviderGameId,
    double? HomeSpread,
    int? HomeRank,
    int? AwayRank);

/// <summary>The data provider could not be reached, failed, timed out or rate-limited the request.</summary>
internal sealed class DataSourceUnavailableException : Exception
{
    public DataSourceUnavailableException()
    {
    }

    public DataSourceUnavailableException(string message)
        : base(message)
    {
    }

    public DataSourceUnavailableException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
