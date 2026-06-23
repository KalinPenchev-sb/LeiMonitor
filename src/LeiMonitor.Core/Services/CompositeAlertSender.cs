using LeiMonitor.Core.Interfaces;
using LeiMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace LeiMonitor.Core.Services;

public class CompositeAlertSender : IAlertSender
{
    private readonly IReadOnlyList<INotificationChannel> _channels;
    private readonly ILogger<CompositeAlertSender> _logger;

    public CompositeAlertSender(
        IEnumerable<INotificationChannel> channels,
        ILogger<CompositeAlertSender> logger)
    {
        _channels = channels.ToList().AsReadOnly();
        _logger = logger;
    }

    public async Task SendAsync(IReadOnlyList<LeiIssue> issues, CancellationToken ct = default)
    {
        foreach (var channel in _channels)
        {
            try
            {
                await channel.SendAsync(issues, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Notification channel {Channel} failed. Other channels will still be attempted.",
                    channel.GetType().Name);
            }
        }
    }
}
