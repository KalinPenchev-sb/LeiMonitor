using Dapper;
using LeiMonitor.Core.Interfaces;
using LeiMonitor.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace LeiMonitor.Data.Repositories;

public class LeiRepository : ILeiRepository
{
    private readonly string _connectionString;

    public LeiRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("CustomerStorage")
            ?? throw new InvalidOperationException(
                "Connection string 'CustomerStorage' is not configured.");
    }

    public async Task<IReadOnlyList<LeiIssue>> GetIssuesAsync(CancellationToken ct = default)
    {
        const string sql = @"
            SELECT [CustomerId], [LeiCode], [LegalName], [ExpirationDate], [IsExpired]
            FROM   [dbo].[CustomerLeiCode]
            WHERE  [IsExpired] = 1
            AND    [IsActive] = 1";

        using var conn = new SqlConnection(_connectionString);
        var results = await conn.QueryAsync<LeiIssue>(new CommandDefinition(sql, cancellationToken: ct));
        return results.ToList().AsReadOnly();
    }
}
