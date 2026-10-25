using ShouldIWatch.Api.CollegeFootball.Scoring;

namespace ShouldIWatch.Tests.CollegeFootball;

public sealed class WatchabilityScorerTests
{
    // Tier and GameFacts are internal, so test signatures use names and the facts are built inside each test.

    [Theory]
    [InlineData(-2, nameof(Tier.Skip))]
    [InlineData(1, nameof(Tier.Skip))]
    [InlineData(2, nameof(Tier.WorthIt))]
    [InlineData(4, nameof(Tier.WorthIt))]
    [InlineData(5, nameof(Tier.MustWatch))]
    [InlineData(12, nameof(Tier.MustWatch))]
    public void PointsMapToTierBands(int points, string expected) =>
        Assert.Equal(Enum.Parse<Tier>(expected), WatchabilityScorer.TierFor(points));

    [Fact]
    public void OnePointGameIsSkip() =>
        AssertPointsAndTier(new GameFacts(27, 17, WentToOvertime: false), 1, Tier.Skip); // margin 10 (+1)

    [Fact]
    public void TwoPointGameIsWorthIt() =>
        AssertPointsAndTier(new GameFacts(27, 21, WentToOvertime: false), 2, Tier.WorthIt); // margin 6 (+2)

    [Fact]
    public void FourPointGameIsWorthIt() =>
        // Margin 2 (+3), one ranked (+1).
        AssertPointsAndTier(new GameFacts(24, 22, WentToOvertime: false, HomeRank: 10), 4, Tier.WorthIt);

    [Fact]
    public void FivePointGameIsMustWatch() =>
        // Margin 2 (+3), both ranked (+2).
        AssertPointsAndTier(new GameFacts(24, 22, WentToOvertime: false, HomeRank: 10, AwayRank: 12), 5, Tier.MustWatch);

    [Theory]
    [InlineData(31, 28, false)]
    [InlineData(20, 21, false)]
    [InlineData(38, 31, true)]
    public void CloseGameBetweenRankedTeamsIsMustWatch(int homePoints, int awayPoints, bool overtime)
    {
        var facts = new GameFacts(homePoints, awayPoints, overtime, HomeSpread: -3, HomeRank: 5, AwayRank: 9);

        Assert.Equal(Tier.MustWatch, WatchabilityScorer.Score(facts));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(null)]
    public void UnremarkableBlowoutIsSkip(double? excitementIndex)
    {
        // Unranked non-rivals, favorite (home, -20) wins by 35.
        var facts = new GameFacts(45, 10, WentToOvertime: false, HomeSpread: -20, ExcitementIndex: excitementIndex);

        Assert.True(WatchabilityScorer.Points(facts) <= -2);
        Assert.Equal(Tier.Skip, WatchabilityScorer.Score(facts));
    }

    [Fact]
    public void UpsetNeverLowersTheTier()
    {
        foreach (var margin in new[] { 1, 3, 7, 10, 14, 20, 28, 40 })
        foreach (var homeRank in new int?[] { null, 4 })
        foreach (var awayRank in new int?[] { null, 15 })
        foreach (var rivalry in new[] { false, true })
        foreach (var excitement in new double?[] { null, 2.5, 4.5, 7 })
        {
            // Home wins by the same margin in both games; only who was favored differs.
            var favoriteWon = new GameFacts(21 + margin, 21, false, HomeSpread: -6, HomeRank: homeRank,
                AwayRank: awayRank, IsRivalry: rivalry, ExcitementIndex: excitement);
            var underdogWon = favoriteWon with { HomeSpread = 6 };

            Assert.True(
                WatchabilityScorer.Score(underdogWon) >= WatchabilityScorer.Score(favoriteWon),
                $"margin {margin}, ranks {homeRank}/{awayRank}, rivalry {rivalry}, excitement {excitement}");
            Assert.Equal(WatchabilityScorer.Points(favoriteWon) + WatchabilityScorer.UpsetPoints, WatchabilityScorer.Points(underdogWon));
        }
    }

    [Fact]
    public void MissingExcitementIndexStillRatesFromOtherSignals()
    {
        var facts = new GameFacts(24, 21, WentToOvertime: false, ExcitementIndex: null);

        Assert.Equal(3, WatchabilityScorer.Points(facts));
        Assert.Equal(Tier.WorthIt, WatchabilityScorer.Score(facts));
    }

    [Theory]
    [InlineData(1800, 1650, 21, 31, 3)] // home favored by Elo, away wins by 10: +1 margin, +2 upset
    [InlineData(1800, 1650, 31, 21, 1)] // favorite wins: margin only
    [InlineData(1650, 1800, 31, 21, 3)] // home is the Elo underdog and wins
    [InlineData(1700, 1650, 21, 31, 1)] // Elo gap under 100: no favorite, no upset
    [InlineData(null, 1650, 21, 31, 1)] // no Elo: no upset
    public void UsesPregameEloWhenThereIsNoSpread(int? homeElo, int? awayElo, int homePoints, int awayPoints, int expectedPoints)
    {
        var facts = new GameFacts(homePoints, awayPoints, false, HomeSpread: null, HomePregameElo: homeElo, AwayPregameElo: awayElo);

        Assert.Equal(expectedPoints, WatchabilityScorer.Points(facts));
    }

    [Fact]
    public void SpreadTakesPrecedenceOverElo()
    {
        // The spread says home was favored and won, so Elo saying otherwise is ignored.
        var facts = new GameFacts(31, 21, false, HomeSpread: -3, HomePregameElo: 1500, AwayPregameElo: 1800);

        Assert.Equal(1, WatchabilityScorer.Points(facts));
    }

    [Fact]
    public void PickEmSpreadHasNoUpset()
    {
        var facts = new GameFacts(21, 31, false, HomeSpread: 0, HomePregameElo: 1900, AwayPregameElo: 1500);

        Assert.Equal(1, WatchabilityScorer.Points(facts));
    }

    [Theory]
    [InlineData(6.0, 3)]
    [InlineData(5.99, 2)]
    [InlineData(4.0, 2)]
    [InlineData(3.99, 1)]
    [InlineData(2.5, 1)]
    [InlineData(2.49, 0)]
    public void ExcitementIndexBands(double excitementIndex, int expectedExcitementPoints)
    {
        // Margin 20 scores 0, so the total is the excitement points alone.
        var facts = new GameFacts(40, 20, false, ExcitementIndex: excitementIndex);

        Assert.Equal(expectedExcitementPoints, WatchabilityScorer.Points(facts));
    }

    private static void AssertPointsAndTier(GameFacts facts, int expectedPoints, Tier expectedTier)
    {
        Assert.Equal(expectedPoints, WatchabilityScorer.Points(facts));
        Assert.Equal(expectedTier, WatchabilityScorer.Score(facts));
    }

    [Fact]
    public void RivalryAddsAPoint()
    {
        var facts = new GameFacts(40, 20, false);

        Assert.Equal(WatchabilityScorer.Points(facts) + 1, WatchabilityScorer.Points(facts with { IsRivalry = true }));
    }
}
