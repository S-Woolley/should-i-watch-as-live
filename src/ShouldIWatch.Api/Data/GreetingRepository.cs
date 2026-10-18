using Dapper;
using Microsoft.Data.SqlClient;

namespace ShouldIWatch.Api.Data;

internal sealed record Greeting(int Id, string Message, DateTime CreatedAt);

// SqlConnection is registered (scoped) by Aspire's AddSqlServerClient; Dapper opens and closes it per query.
internal sealed class GreetingRepository(SqlConnection connection)
{
    public Task<Greeting?> GetLatestAsync(CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<Greeting>(new CommandDefinition(
            """
            SELECT TOP (1) id, message, created_at AS CreatedAt
            FROM dbo.greeting
            ORDER BY id DESC
            """,
            cancellationToken: cancellationToken));
}
