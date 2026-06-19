using LeiMonitor.Core.Interfaces;
using LeiMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace LeiMonitor.Data.Notifications;

public class EmailAlertSender : IAlertSender
{
    private readonly ILogger<EmailAlertSender> _logger;

    public EmailAlertSender(ILogger<EmailAlertSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(IReadOnlyList<LeiIssue> issues, CancellationToken ct = default)
    {
        foreach (var issue in issues)
            _logger.LogWarning(
                "LEI issue: {LegalName} ({LeiCode}), expires {Date}, expired {IsExpired}",
                issue.LegalName,
                issue.LeiCode,
                issue.ExpirationDate,
                issue.IsExpired);

        return Task.CompletedTask;
    }
}
