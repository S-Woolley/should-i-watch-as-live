using System.Reflection;
using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.Postgresql;
using Npgsql;

namespace ShouldIWatch.Database;

/// <summary>
/// Applies the SQL scripts embedded in this assembly (Scripts/NNNN_description.sql) using DbUp.
/// Scripts run once each, in name order, and are journaled in the <c>schemaversions</c> table.
/// </summary>
public static class DatabaseMigrator
{
    public static Assembly ScriptsAssembly => typeof(DatabaseMigrator).Assembly;

    public static DatabaseUpgradeResult Migrate(NpgsqlDataSource dataSource, IUpgradeLog log)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(new PostgresqlConnectionManager(dataSource))
            .WithScriptsEmbeddedInAssembly(ScriptsAssembly, IsMigrationScript)
            .WithTransactionPerScript()
            .LogTo(log)
            .Build();

        return upgrader.PerformUpgrade();
    }

    public static bool IsMigrationScript(string resourceName) =>
        resourceName.Contains(".Scripts.", StringComparison.Ordinal)
        && resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);
}
