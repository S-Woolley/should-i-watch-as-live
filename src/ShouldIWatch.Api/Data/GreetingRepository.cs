using Dapper;
using Npgsql;

namespace ShouldIWatch.Api.Data;

// Npgsql maps timestamptz to a UTC DateTime.
internal sealed record Greeting(int Id, string Message, DateTime CreatedAt);

internal sealed class GreetingRepository(NpgsqlDataSource dataSource)
{
    public async Task<Greeting?> GetLatestAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<Greeting>(new CommandDefinition(
            """
            SELECT id, message, created_at AS CreatedAt
            FROM greeting
            ORDER BY id DESC
            LIMIT 1
            """,
            cancellationToken: cancellationToken));
    }
}
