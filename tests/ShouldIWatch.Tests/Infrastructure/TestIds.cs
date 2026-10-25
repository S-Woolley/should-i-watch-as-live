namespace ShouldIWatch.Tests.Infrastructure;

/// <summary>
/// Unique seasons and provider names, so tests sharing the test database (and running in parallel) never see each
/// other's games or throttle rows.
/// </summary>
internal static class TestIds
{
    private static int lastSeason = 2100 + Random.Shared.Next(0, 5000);
    private static int lastProvider;

    public static int NewSeason() => Interlocked.Increment(ref lastSeason);

    public static string NewProvider() => $"fake{Interlocked.Increment(ref lastProvider)}_{Random.Shared.Next(100_000)}";
}
