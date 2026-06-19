namespace LeiMonitor.Core.Interfaces;

using LeiMonitor.Core.Models;

public interface ILeiRepository
{
    Task<IReadOnlyList<LeiIssue>> GetIssuesAsync(CancellationToken ct = default);
}
