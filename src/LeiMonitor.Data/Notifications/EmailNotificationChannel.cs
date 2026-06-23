using System.Text.Json;
using Azure.Messaging.ServiceBus;
using LeiMonitor.Core.Interfaces;
using LeiMonitor.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LeiMonitor.Data.Notifications;

public class EmailNotificationChannel : INotificationChannel
{
    internal const string QueueName = "q-con-sav-email-send";

    private readonly ServiceBusSender _sender;
    private readonly ILogger<EmailNotificationChannel> _logger;

    public EmailNotificationChannel(IConfiguration config, ILogger<EmailNotificationChannel> logger)
        : this(
            new ServiceBusClient(
                config["ServiceBus:ConnectionString"]
                    ?? throw new InvalidOperationException("'ServiceBus:ConnectionString' is not configured."))
                .CreateSender(QueueName),
            logger)
    { }

    internal EmailNotificationChannel(ServiceBusSender sender, ILogger<EmailNotificationChannel> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task SendAsync(IReadOnlyList<LeiIssue> issues, CancellationToken ct = default)
    {
        var messages = issues.Select(issue => new ServiceBusMessage(
            JsonSerializer.Serialize(BuildMessage(issue)))
        {
            ContentType = "application/json",
            CorrelationId = Guid.NewGuid().ToString()
        });

        await _sender.SendMessagesAsync(messages, ct);

        _logger.LogInformation(
            "Published {Count} email alert message(s) to Service Bus queue {Queue}.",
            issues.Count,
            QueueName);
    }

    private static EmailAlertMessage BuildMessage(LeiIssue issue) =>
        new()
        {
            CorrelationId = Guid.NewGuid(),
            NotificationId = Guid.NewGuid(),
            CustomerId = issue.CustomerId,
            LeiCode = issue.LeiCode,
            LegalName = issue.LegalName,
            RenewalExpiryDate = issue.ExpirationDate,
            TemplateName = "LeiExpiry",
            RecipientGroup = "LeiAlerts"
        };
}
