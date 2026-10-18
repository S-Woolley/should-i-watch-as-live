using System.Reflection;
using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;

namespace ShouldIWatch.Database;

/// <summary>
/// Applies the SQL scripts embedded in this assembly (Scripts/NNNN_description.sql) using DbUp.
/// Scripts run once each, in name order, and are journaled in the <c>schemaversions</c> table.
/// </summary>
public static class DatabaseMigrator
{
    public static Assembly ScriptsAssembly => typeof(DatabaseMigrator).Assembly;

    /// <param name="connectionString">
    /// SQL Server connection string. In Azure it uses Entra ID (managed identity) authentication,
    /// which Microsoft.Data.SqlClient handles, so no password is involved.
    /// </param>
    public static DatabaseUpgradeResult Migrate(string connectionString, IUpgradeLog log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
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
