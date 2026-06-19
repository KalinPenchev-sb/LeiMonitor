using LeiMonitor.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LeiMonitor.Core.Services;

public class LeiExpiryChecker
{
    private readonly ILeiRepository _repository;
    private readonly IAlertSender _alertSender;
    private readonly ILogger<LeiExpiryChecker> _logger;

    public LeiExpiryChecker(
        ILeiRepository repository,
        IAlertSender alertSender,
        ILogger<LeiExpiryChecker> logger)
    {
        _repository = repository;
        _alertSender = alertSender;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting LEI expiry check.");

        var issues = await _repository.GetIssuesAsync(ct);

        if (issues.Count == 0)
        {
            _logger.LogInformation("No LEI issues found.");
            return;
        }

        _logger.LogInformation("Found {Count} LEI issue(s). Sending alert.", issues.Count);
        await _alertSender.SendAsync(issues, ct);
    }
}
