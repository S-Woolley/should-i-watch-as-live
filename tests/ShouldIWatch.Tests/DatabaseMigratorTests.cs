using ShouldIWatch.Database;

namespace ShouldIWatch.Tests;

public sealed class DatabaseMigratorTests
{
    private static readonly string[] Scripts = DatabaseMigrator.ScriptsAssembly
        .GetManifestResourceNames()
        .Where(DatabaseMigrator.IsMigrationScript)
        .ToArray();

    [Theory]
    [InlineData("0001_create_greeting.sql")]
    [InlineData("0002_create_cfb_games_and_sync_state.sql")]
    [InlineData("0003_create_cfb_rivalry.sql")]
    public void MigrationScriptsAreEmbedded(string fileName)
    {
        Assert.Contains(Scripts, name => name.EndsWith(fileName, StringComparison.Ordinal));
    }

    [Fact]
    public void MigrationScriptsHaveNumericPrefixSoTheyRunInOrder()
    {
        foreach (var script in Scripts)
        {
            var fileName = script[(script.IndexOf(".Scripts.", StringComparison.Ordinal) + ".Scripts.".Length)..];
            Assert.Matches(@"^\d{4}_[a-z0-9_]+\.sql$", fileName);
        }
    }
}
