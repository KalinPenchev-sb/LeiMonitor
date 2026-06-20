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

        var result = new List<LeiIssue>();
         result.Add(new LeiIssue
        {
            CustomerId = Guid.NewGuid(),
            LeiCode = "1234567890",
            LegalName = "Test Company",
            ExpirationDate = DateTime.UtcNow.AddDays(-1),
            IsExpired = true
        });

        return result.AsReadOnly();
        // using var conn = new SqlConnection(_connectionString);
        // var results = await conn.QueryAsync<LeiIssue>(new CommandDefinition(sql, cancellationToken: ct));
        // return results.ToList().AsReadOnly();
    }
}
