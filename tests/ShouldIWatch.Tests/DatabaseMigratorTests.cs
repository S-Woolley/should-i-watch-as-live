using ShouldIWatch.Database;

namespace ShouldIWatch.Tests;

public sealed class DatabaseMigratorTests
{
    private static readonly string[] Scripts = DatabaseMigrator.ScriptsAssembly
        .GetManifestResourceNames()
        .Where(DatabaseMigrator.IsMigrationScript)
        .ToArray();

    [Fact]
    public void MigrationScriptsAreEmbedded()
    {
        Assert.Contains(Scripts, name => name.EndsWith("0001_create_greeting.sql", StringComparison.Ordinal));
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
