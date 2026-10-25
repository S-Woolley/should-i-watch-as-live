namespace ShouldIWatch.Api.CollegeFootball.Scoring;

internal enum Tier
{
    Skip,
    WorthIt,
    MustWatch,
}

/// <summary>Final-state signals for one completed game. Optional signals are null when the provider has none.</summary>
internal sealed record GameFacts(
    int HomePoints,
    int AwayPoints,
    bool WentToOvertime,
    double? HomeSpread = null,
    int? HomePregameElo = null,
    int? AwayPregameElo = null,
    int? HomeRank = null,
    int? AwayRank = null,
    bool IsRivalry = false,
    double? ExcitementIndex = null);

/// <summary>
/// Rates a completed game by adding points for each signal and bucketing the total into a <see cref="Tier"/>.
/// Pure and side-effect free; tiers are computed when read, so changing a weight re-rates every cached game.
/// </summary>
internal static class WatchabilityScorer
{
    internal const int CloseGamePoints = 3;          // overtime, or margin <= 3
    internal const int OneScoreGamePoints = 2;       // margin <= 8
    internal const int TwoScoreGamePoints = 1;       // margin <= 16
    internal const int BlowoutPoints = -2;           // margin > 24
    internal const int UpsetPoints = 2;
    internal const int BothRankedPoints = 2;
    internal const int OneRankedPoints = 1;
    internal const int RivalryPoints = 1;
    internal const int EloUpsetGap = 100;            // pregame Elo gap that makes a favorite when there is no spread

    internal const int MustWatchMinimum = 5;
    internal const int WorthItMinimum = 2;

    public static Tier Score(GameFacts facts) => TierFor(Points(facts));

    internal static Tier TierFor(int points) => points switch
    {
        >= MustWatchMinimum => Tier.MustWatch,
        >= WorthItMinimum => Tier.WorthIt,
        _ => Tier.Skip,
    };

    internal static int Points(GameFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return MarginPoints(facts)
            + (UnderdogWon(facts) ? UpsetPoints : 0)
            + RankPoints(facts)
            + (facts.IsRivalry ? RivalryPoints : 0)
            + ExcitementPoints(facts.ExcitementIndex);
    }

    private static int MarginPoints(GameFacts facts)
    {
        var margin = Math.Abs(facts.HomePoints - facts.AwayPoints);
        return margin switch
        {
            _ when facts.WentToOvertime || margin <= 3 => CloseGamePoints,
            <= 8 => OneScoreGamePoints,
            <= 16 => TwoScoreGamePoints,
            > 24 => BlowoutPoints,
            _ => 0,
        };
    }

    private static bool UnderdogWon(GameFacts facts)
    {
        var homeWon = facts.HomePoints > facts.AwayPoints;
        if (facts.HomePoints == facts.AwayPoints)
        {
            return false;
        }

        if (facts.HomeSpread is { } spread)
        {
            // Negative spread: home favored.
            return spread < 0 ? !homeWon : spread > 0 && homeWon;
        }

        if (facts is { HomePregameElo: { } homeElo, AwayPregameElo: { } awayElo }
            && Math.Abs(homeElo - awayElo) >= EloUpsetGap)
        {
            return homeElo > awayElo ? !homeWon : homeWon;
        }

        return false;
    }

    private static int RankPoints(GameFacts facts) => (facts.HomeRank, facts.AwayRank) switch
    {
        (not null, not null) => BothRankedPoints,
        (not null, null) or (null, not null) => OneRankedPoints,
        _ => 0,
    };

    private static int ExcitementPoints(double? excitementIndex) => excitementIndex switch
    {
        >= 6 => 3,
        >= 4 => 2,
        >= 2.5 => 1,
        _ => 0,
    };
}
