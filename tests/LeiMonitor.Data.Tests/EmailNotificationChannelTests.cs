using System.Text.Json;
using Azure.Messaging.ServiceBus;
using LeiMonitor.Core.Models;
using LeiMonitor.Data.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LeiMonitor.Data.Tests;

public class EmailNotificationChannelTests
{
    private readonly Mock<ServiceBusSender> _senderMock = new();
    private readonly EmailNotificationChannel _sut;

    public EmailNotificationChannelTests()
    {
        _sut = new EmailNotificationChannel(
            _senderMock.Object,
            NullLogger<EmailNotificationChannel>.Instance);
    }

    [Fact]
    public async Task SendAsync_PublishesOneMessagePerIssue()
    {
        var issues = new List<LeiIssue>
        {
            new() { CustomerId = Guid.NewGuid(), LeiCode = "AAAAAAAAAAAAAAAAAAAA", LegalName = "Alpha Ltd",
                    ExpirationDate = DateTime.UtcNow.AddDays(-5), IsExpired = true },
            new() { CustomerId = Guid.NewGuid(), LeiCode = "BBBBBBBBBBBBBBBBBBBB", LegalName = "Beta Corp",
                    ExpirationDate = DateTime.UtcNow.AddDays(-1), IsExpired = true }
        }.AsReadOnly();

        IEnumerable<ServiceBusMessage>? captured = null;
        _senderMock
            .Setup(s => s.SendMessagesAsync(
                It.IsAny<IEnumerable<ServiceBusMessage>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ServiceBusMessage>, CancellationToken>((msgs, _) => captured = msgs.ToList())
            .Returns(Task.CompletedTask);

        await _sut.SendAsync(issues);

        _senderMock.Verify(
            s => s.SendMessagesAsync(
                It.IsAny<IEnumerable<ServiceBusMessage>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.NotNull(captured);
        Assert.Equal(issues.Count, captured!.Count());
    }

    [Fact]
    public async Task SendAsync_MessagePayload_ContainsExpectedFields()
    {
        var customerId = Guid.NewGuid();
        var expiry = new DateTime(2024, 3, 15);
        var issues = new List<LeiIssue>
        {
            new() { CustomerId = customerId, LeiCode = "CCCCCCCCCCCCCCCCCCCC",
                    LegalName = "Gamma Plc", ExpirationDate = expiry, IsExpired = true }
        }.AsReadOnly();

        IEnumerable<ServiceBusMessage>? captured = null;
        _senderMock
            .Setup(s => s.SendMessagesAsync(
                It.IsAny<IEnumerable<ServiceBusMessage>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ServiceBusMessage>, CancellationToken>((msgs, _) => captured = msgs.ToList())
            .Returns(Task.CompletedTask);

        await _sut.SendAsync(issues);

        var msg = Assert.Single(captured!);
        Assert.Equal("application/json", msg.ContentType);

        var payload = JsonSerializer.Deserialize<EmailAlertMessage>(
            msg.Body.ToString(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(payload);
        Assert.Equal(customerId, payload!.CustomerId);
        Assert.Equal("CCCCCCCCCCCCCCCCCCCC", payload.LeiCode);
        Assert.Equal("Gamma Plc", payload.LegalName);
        Assert.Equal(expiry, payload.RenewalExpiryDate);
        Assert.Equal("LeiExpiry", payload.TemplateName);
        Assert.Equal("LeiAlerts", payload.RecipientGroup);
        Assert.NotEqual(Guid.Empty, payload.CorrelationId);
        Assert.NotEqual(Guid.Empty, payload.NotificationId);
    }

    [Fact]
    public async Task SendAsync_SetsContentTypeToApplicationJson()
    {
        var issues = new List<LeiIssue>
        {
            new() { CustomerId = Guid.NewGuid(), LeiCode = "DDDDDDDDDDDDDDDDDDDD",
                    LegalName = "Delta SA", ExpirationDate = DateTime.UtcNow.AddDays(-2), IsExpired = true }
        }.AsReadOnly();

        IEnumerable<ServiceBusMessage>? captured = null;
        _senderMock
            .Setup(s => s.SendMessagesAsync(
                It.IsAny<IEnumerable<ServiceBusMessage>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ServiceBusMessage>, CancellationToken>((msgs, _) => captured = msgs.ToList())
            .Returns(Task.CompletedTask);

        await _sut.SendAsync(issues);

        foreach (var msg in captured!)
            Assert.Equal("application/json", msg.ContentType);
    }

    [Fact]
    public async Task SendAsync_EmptyList_CallsSenderWithNoMessages()
    {
        var issues = Array.Empty<LeiIssue>().ToList().AsReadOnly();

        _senderMock
            .Setup(s => s.SendMessagesAsync(
                It.IsAny<IEnumerable<ServiceBusMessage>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.SendAsync(issues);

        _senderMock.Verify(
            s => s.SendMessagesAsync(
                It.Is<IEnumerable<ServiceBusMessage>>(m => !m.Any()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_PropagatesServiceBusException()
    {
        var issues = new List<LeiIssue>
        {
            new() { CustomerId = Guid.NewGuid(), LeiCode = "EEEEEEEEEEEEEEEEEEEE",
                    LegalName = "Epsilon Inc", ExpirationDate = DateTime.UtcNow.AddDays(-3), IsExpired = true }
        }.AsReadOnly();

        _senderMock
            .Setup(s => s.SendMessagesAsync(
                It.IsAny<IEnumerable<ServiceBusMessage>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("Simulated failure", ServiceBusFailureReason.ServiceCommunicationProblem));

        await Assert.ThrowsAsync<ServiceBusException>(() => _sut.SendAsync(issues));
    }
}
