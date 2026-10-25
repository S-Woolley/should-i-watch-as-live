using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ShouldIWatch.Api.CollegeFootball.Sources;

/// <summary>
/// A provider-neutral week of a season: either a regular-season week number or "postseason" (all postseason games).
/// Its string form ("8", "postseason") is what appears in URLs.
/// </summary>
internal readonly record struct WeekKey : IComparable<WeekKey>
{
    public const string PostseasonKey = "postseason";

    private WeekKey(int? regularWeek) => RegularWeek = regularWeek;

    public static WeekKey Postseason { get; } = new(null);

    /// <summary>The regular-season week number (college football has a "week 0"), or null for the postseason.</summary>
    public int? RegularWeek { get; }

    public bool IsPostseason => RegularWeek is null;

    public string Label => IsPostseason ? "Postseason" : $"Week {RegularWeek}";

    public static WeekKey Regular(int week)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(week);
        return new(week);
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out WeekKey? week)
    {
        week = null;
        if (string.Equals(value, PostseasonKey, StringComparison.Ordinal))
        {
            week = Postseason;
        }
        else if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            week = Regular(number);
        }

        return week is not null;
    }

    /// <summary>Regular weeks in number order, then the postseason.</summary>
    public int CompareTo(WeekKey other) => (RegularWeek ?? int.MaxValue).CompareTo(other.RegularWeek ?? int.MaxValue);

    public override string ToString() =>
        IsPostseason ? PostseasonKey : RegularWeek!.Value.ToString(CultureInfo.InvariantCulture);
}
