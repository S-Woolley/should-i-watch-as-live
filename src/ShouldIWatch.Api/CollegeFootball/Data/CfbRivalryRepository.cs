using Dapper;
using Microsoft.Data.SqlClient;

namespace ShouldIWatch.Api.CollegeFootball.Data;

internal sealed record Rivalry(string TeamA, string TeamB, string Name);

internal sealed class CfbRivalryRepository(SqlConnection connection)
{
    public async Task<IReadOnlyList<Rivalry>> GetAllAsync(CancellationToken cancellationToken) =>
        (await connection.QueryAsync<Rivalry>(new CommandDefinition(
            "SELECT team_a AS TeamA, team_b AS TeamB, name FROM dbo.cfb_rivalry",
            cancellationToken: cancellationToken))).AsList();
}
