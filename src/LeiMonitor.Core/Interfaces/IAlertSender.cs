namespace LeiMonitor.Core.Interfaces;

using LeiMonitor.Core.Models;

public interface IAlertSender
{
    Task SendAsync(IReadOnlyList<LeiIssue> issues, CancellationToken ct = default);
}
