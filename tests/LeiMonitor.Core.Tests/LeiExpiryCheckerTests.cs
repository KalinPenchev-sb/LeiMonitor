using LeiMonitor.Core.Interfaces;
using LeiMonitor.Core.Models;
using LeiMonitor.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LeiMonitor.Core.Tests;

public class LeiExpiryCheckerTests
{
    private readonly Mock<ILeiRepository> _repositoryMock = new();
    private readonly Mock<IAlertSender> _alertSenderMock = new();
    private readonly LeiExpiryChecker _sut;

    public LeiExpiryCheckerTests()
    {
        _sut = new LeiExpiryChecker(
            _repositoryMock.Object,
            _alertSenderMock.Object,
            NullLogger<LeiExpiryChecker>.Instance);
    }

    [Fact]
    public async Task RunAsync_NoIssues_DoesNotCallAlertSender()
    {
        _repositoryMock
            .Setup(r => r.GetIssuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeiIssue>().ToList().AsReadOnly());

        await _sut.RunAsync();

        _alertSenderMock.Verify(
            a => a.SendAsync(It.IsAny<IReadOnlyList<LeiIssue>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WithExpiredLeis_CallsAlertSender()
    {
        var issues = new List<LeiIssue>
        {
            new() { CustomerId = Guid.NewGuid(), LeiCode = "ABC123", LegalName = "Acme Ltd",
                    ExpirationDate = DateTime.UtcNow.AddDays(-10), IsExpired = true }
        }.AsReadOnly();

        _repositoryMock
            .Setup(r => r.GetIssuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(issues);

        _alertSenderMock
            .Setup(a => a.SendAsync(It.IsAny<IReadOnlyList<LeiIssue>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.RunAsync();

        _alertSenderMock.Verify(
            a => a.SendAsync(issues, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithExpiredLeis_IsExpiredTrue_CallsAlertSender()
    {
        var issues = new List<LeiIssue>
        {
            new() { CustomerId = Guid.NewGuid(), LeiCode = "DEF456", LegalName = "Beta Corp",
                    ExpirationDate = DateTime.UtcNow.AddDays(-5), IsExpired = true }
        }.AsReadOnly();

        _repositoryMock
            .Setup(r => r.GetIssuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(issues);

        _alertSenderMock
            .Setup(a => a.SendAsync(It.IsAny<IReadOnlyList<LeiIssue>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.RunAsync();

        _alertSenderMock.Verify(
            a => a.SendAsync(
                It.Is<IReadOnlyList<LeiIssue>>(l => l.All(i => i.IsExpired)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_RepositoryThrows_DoesNotSwallowException()
    {
        _repositoryMock
            .Setup(r => r.GetIssuesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RunAsync());

        _alertSenderMock.Verify(
            a => a.SendAsync(It.IsAny<IReadOnlyList<LeiIssue>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
