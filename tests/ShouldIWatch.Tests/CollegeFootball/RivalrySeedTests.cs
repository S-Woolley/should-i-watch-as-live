using System.Text.Json;
using System.Text.RegularExpressions;
using ShouldIWatch.Database;
using ShouldIWatch.Tests.Infrastructure;

namespace ShouldIWatch.Tests.CollegeFootball;

public sealed partial class RivalrySeedTests
{
    private static readonly (string TeamA, string TeamB)[] SeededPairs = ReadSeededPairs();

    [Fact]
    public void SeedsAboutTwentyFiveRivalriesWithoutDuplicates()
    {
        Assert.InRange(SeededPairs.Length, 25, 40);
        Assert.Equal(SeededPairs.Length, SeededPairs.Distinct().Count());
    }

    [Fact]
    public void PairsAreInAlphabeticalOrder()
    {
        foreach (var (teamA, teamB) in SeededPairs)
        {
            Assert.True(string.CompareOrdinal(teamA, teamB) < 0, $"{teamA} should sort before {teamB}");
        }
    }

    [Fact]
    public void EverySeededSchoolNameMatchesCfbdSpelling()
    {
        using var sample = JsonDocument.Parse(Fixtures.Read("games-2024-rivalry-sample.json"));
        var cfbdNames = sample.RootElement.EnumerateArray()
            .SelectMany(game => new[] { game.GetProperty("homeTeam").GetString()!, game.GetProperty("awayTeam").GetString()! })
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in SeededPairs.SelectMany(pair => new[] { pair.TeamA, pair.TeamB }))
        {
            Assert.Contains(name, cfbdNames);
        }
    }

    private static (string, string)[] ReadSeededPairs()
    {
        var resource = DatabaseMigrator.ScriptsAssembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("0003_create_cfb_rivalry.sql", StringComparison.Ordinal));
        using var stream = DatabaseMigrator.ScriptsAssembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd();

        return SeedRow().Matches(script)
            .Select(match => (match.Groups["a"].Value, match.Groups["b"].Value))
            .ToArray();
    }

    [GeneratedRegex(@"\(N'(?<a>[^']+)', N'(?<b>[^']+)', N'")]
    private static partial Regex SeedRow();
}
