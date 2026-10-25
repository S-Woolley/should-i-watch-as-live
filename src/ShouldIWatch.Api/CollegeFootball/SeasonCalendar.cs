using ShouldIWatch.Api.CollegeFootball.Data;
using ShouldIWatch.Api.CollegeFootball.Sources;

namespace ShouldIWatch.Api.CollegeFootball;

internal static class SeasonCalendar
{
    private const int SeasonStartMonth = 8;

    /// <summary>The calendar year from August onward; the previous year from January to July.</summary>
    public static int CurrentSeason(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        return utc.Month >= SeasonStartMonth ? utc.Year : utc.Year - 1;
    }

    /// <summary>The most recent week with a completed game, else the first week; null when no weeks are known.</summary>
    public static WeekKey? DefaultWeek(IEnumerable<CachedWeek> weeks)
    {
        var ordered = weeks.OrderBy(week => week.Week).ToList();
        return ordered.LastOrDefault(week => week.HasCompletedGame)?.Week ?? ordered.FirstOrDefault()?.Week;
    }
}
