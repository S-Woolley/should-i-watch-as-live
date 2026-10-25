using ShouldIWatch.Api.CollegeFootball;
using ShouldIWatch.Api.CollegeFootball.Data;
using ShouldIWatch.Api.CollegeFootball.Sources;

namespace ShouldIWatch.Tests.CollegeFootball;

public sealed class SeasonCalendarTests
{
    [Theory]
    [InlineData("2026-01-12T20:00:00Z", 2025)] // January: postseason of the previous season
    [InlineData("2026-07-31T23:59:59Z", 2025)] // July: still the previous season
    [InlineData("2026-08-01T00:00:00Z", 2026)] // August cutover
    [InlineData("2026-10-18T12:00:00Z", 2026)] // mid-season
    [InlineData("2026-12-31T23:59:59Z", 2026)]
    public void CurrentSeasonCutsOverInAugust(string now, int expectedSeason) =>
        Assert.Equal(expectedSeason, SeasonCalendar.CurrentSeason(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void DefaultWeekIsTheMostRecentWeekWithACompletedGame()
    {
        CachedWeek[] weeks =
        [
            new(WeekKey.Postseason, false),
            new(WeekKey.Regular(8), false),
            new(WeekKey.Regular(1), true),
            new(WeekKey.Regular(7), true),
        ];

        Assert.Equal(WeekKey.Regular(7), SeasonCalendar.DefaultWeek(weeks));
    }

    [Fact]
    public void DefaultWeekIsThePostseasonOnceItHasACompletedGame()
    {
        CachedWeek[] weeks = [new(WeekKey.Regular(15), true), new(WeekKey.Postseason, true)];

        Assert.Equal(WeekKey.Postseason, SeasonCalendar.DefaultWeek(weeks));
    }

    [Fact]
    public void DefaultWeekIsTheFirstWeekBeforeAnyGameHasCompleted()
    {
        CachedWeek[] weeks = [new(WeekKey.Regular(2), false), new(WeekKey.Regular(0), false), new(WeekKey.Regular(1), false)];

        Assert.Equal(WeekKey.Regular(0), SeasonCalendar.DefaultWeek(weeks));
    }

    [Fact]
    public void DefaultWeekIsNullWithNoWeeks() => Assert.Null(SeasonCalendar.DefaultWeek([]));

    [Theory]
    [InlineData("8", 8)]
    [InlineData("0", 0)]
    [InlineData("15", 15)]
    public void ParsesRegularWeekKeys(string key, int expectedWeek)
    {
        Assert.True(WeekKey.TryParse(key, out var week));
        Assert.Equal(WeekKey.Regular(expectedWeek), week);
        Assert.Equal(key, week.Value.ToString());
    }

    [Fact]
    public void ParsesThePostseasonKey()
    {
        Assert.True(WeekKey.TryParse("postseason", out var week));
        Assert.True(week.Value.IsPostseason);
        Assert.Equal("postseason", week.Value.ToString());
        Assert.Equal("Postseason", week.Value.Label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("+3")]
    [InlineData("week8")]
    [InlineData("Postseason")]
    [InlineData("8.5")]
    public void RejectsInvalidWeekKeys(string? key) => Assert.False(WeekKey.TryParse(key, out _));

    [Fact]
    public void WeeksSortWithThePostseasonLast()
    {
        WeekKey[] weeks = [WeekKey.Postseason, WeekKey.Regular(10), WeekKey.Regular(0), WeekKey.Regular(2)];

        Assert.Equal([WeekKey.Regular(0), WeekKey.Regular(2), WeekKey.Regular(10), WeekKey.Postseason], weeks.Order());
    }
}
