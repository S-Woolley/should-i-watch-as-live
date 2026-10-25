using ShouldIWatch.Api.CollegeFootball.Sources;

namespace ShouldIWatch.Api.CollegeFootball.Data;

/// <summary>Maps a <see cref="WeekKey"/> to the (season_type, week) columns. The postseason is stored as week 0.</summary>
internal static class WeekStorage
{
    private const string Regular = "regular";
    private const string Postseason = "postseason";

    public static (string SeasonType, int Week) FromWeekKey(WeekKey week) =>
        week.IsPostseason ? (Postseason, 0) : (Regular, week.RegularWeek!.Value);

    public static WeekKey ToWeekKey(string seasonType, int week) =>
        seasonType == Postseason ? WeekKey.Postseason : WeekKey.Regular(week);
}
