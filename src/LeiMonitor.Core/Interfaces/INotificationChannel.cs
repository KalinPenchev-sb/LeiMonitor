namespace LeiMonitor.Core.Interfaces;

using LeiMonitor.Core.Models;

public interface INotificationChannel
{
    Task SendAsync(IReadOnlyList<LeiIssue> issues, CancellationToken ct = default);
}
