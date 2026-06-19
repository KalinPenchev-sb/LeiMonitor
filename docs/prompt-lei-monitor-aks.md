Create a new .NET 9 solution called LeiMonitor for a Kubernetes CronJob that monitors ISO 20022 LEI expiry data in a SQL Server database called CustomerStorage.

Context

This is a greenfield project, separate from the legacy Savings-Legacy .NET Framework 4.8 solution. It must target .NET 9 (net9.0) and run as a containerised console application scheduled by an AKS CronJob. There is no Azure Functions dependency. The scheduling concern is owned entirely by Kubernetes. The application starts, executes the LEI expiry check, and exits. Configuration is supplied exclusively via environment variables so the image is portable across environments without rebuild.

Solution structure

LeiMonitor.sln
├── src/
│   ├── LeiMonitor.Core/           (.NET 9 class library)
│   ├── LeiMonitor.Data/           (.NET 9 class library)
│   └── LeiMonitor.Worker/         (.NET 9 console app, Generic Host)
├── tests/
│   └── LeiMonitor.Core.Tests/     (.NET 9 xUnit test project)
└── k8s/
    ├── namespace.yaml
    ├── serviceaccount.yaml
    ├── secret.yaml
    ├── configmap.yaml
    └── cronjob.yaml

LeiMonitor.Core

Contains all business logic. No Azure SDK references. No data access references.

Models:

public class LeiIssue
{
    public Guid CustomerId { get; set; }
    public string LeiCode { get; set; }
    public string LegalName { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public bool IsExpired { get; set; }
}

Interfaces:

public interface ILeiRepository
{
    Task<IReadOnlyList<LeiIssue>> GetIssuesAsync(CancellationToken ct = default);
}

public interface IAlertSender
{
    Task SendAsync(IReadOnlyList<LeiIssue> issues, CancellationToken ct = default);
}

Main orchestrator:

public class LeiExpiryChecker
{
    private readonly ILeiRepository _repository;
    private readonly IAlertSender _alertSender;
    private readonly ILogger<LeiExpiryChecker> _logger;

    public LeiExpiryChecker(ILeiRepository repository, IAlertSender alertSender,
        ILogger<LeiExpiryChecker> logger) { ... }

    public async Task RunAsync(CancellationToken ct = default)
    {
        // 1. Query issues
        // 2. If none found, log and return
        // 3. If issues found, send alert
    }
}

LeiMonitor.Data

Contains the Dapper-based repository and the stub alert sender. References LeiMonitor.Core.

NuGet packages:
- Dapper
- Microsoft.Data.SqlClient
- Microsoft.Extensions.Configuration.Abstractions
- Microsoft.Extensions.Logging.Abstractions

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

        using var conn = new SqlConnection(_connectionString);
        var results = await conn.QueryAsync<LeiIssue>(new CommandDefinition(sql,
            cancellationToken: ct));
        return results.ToList().AsReadOnly();
    }
}

EmailAlertSender (stub):

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
            _logger.LogWarning("LEI issue: {LegalName} ({LeiCode}), expires {Date}, expired {IsExpired}",
                issue.LegalName, issue.LeiCode, issue.ExpirationDate, issue.IsExpired);
        return Task.CompletedTask;
    }
}

LeiMonitor.Worker

.NET 9 console application using Generic Host. References LeiMonitor.Core and LeiMonitor.Data. No Azure Functions packages. The application resolves LeiExpiryChecker, calls RunAsync, then exits. Exit code must be 0 on success and non-zero on unhandled exception so Kubernetes can detect job failure.

NuGet packages:
- Microsoft.Extensions.Hosting
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging
- Microsoft.Extensions.Configuration.EnvironmentVariables

Program.cs:

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config =>
    {
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        services.AddSingleton<ILeiRepository, LeiRepository>();
        services.AddSingleton<IAlertSender, EmailAlertSender>();
        services.AddSingleton<LeiExpiryChecker>();
    })
    .Build();

var checker = host.Services.GetRequiredService<LeiExpiryChecker>();
await checker.RunAsync();

The connection string is read from the environment variable ConnectionStrings__CustomerStorage, which is the standard .NET environment variable convention for IConfiguration connection strings.

Dockerfile

Multi-stage build. Use mcr.microsoft.com/dotnet/sdk:9.0 for the build stage and mcr.microsoft.com/dotnet/runtime:9.0 for the final stage. No Windows-specific base images. Publish to /app. Entrypoint is the LeiMonitor.Worker assembly. The image must run as a non-root user.

Kubernetes manifests (k8s/)

namespace.yaml
Create a dedicated namespace called lei-monitor.

serviceaccount.yaml
Create a ServiceAccount called lei-monitor-sa in the lei-monitor namespace. Add the annotation azure.workload.identity/client-id as a placeholder for the Azure Managed Identity client ID so Workload Identity can be wired up later.

secret.yaml
Create a Kubernetes Secret called lei-monitor-secret in the lei-monitor namespace of type Opaque. Include a placeholder key CustomerStorage for the SQL Server connection string. Add a comment stating that in production this secret should be managed via the Azure Key Vault Secrets Store CSI driver and not stored in plain YAML.

configmap.yaml
Create a ConfigMap called lei-monitor-config in the lei-monitor namespace. Include a key DOTNET_ENVIRONMENT with value Production as a baseline.

cronjob.yaml
Create a CronJob called lei-expiry-check in the lei-monitor namespace with the following specification:
- Schedule: "0 8 * * *" (daily at 08:00 UTC)
- Concurrency policy: Forbid
- Successful jobs history limit: 3
- Failed jobs history limit: 3
- Restart policy: Never
- Backoff limit: 2
- The container image placeholder is REGISTRY/lei-monitor:TAG
- Mount the CustomerStorage connection string from lei-monitor-secret as the environment variable ConnectionStrings__CustomerStorage
- Load all keys from lei-monitor-config as environment variables
- Set resource requests (cpu: 100m, memory: 128Mi) and limits (cpu: 250m, memory: 256Mi)
- Use the lei-monitor-sa service account

LeiMonitor.Core.Tests

xUnit test project targeting .NET 9. Test LeiExpiryChecker using mocked ILeiRepository and IAlertSender.

NuGet packages:
- xunit
- Moq
- Microsoft.Extensions.Logging.Abstractions

Test cases to scaffold:
- RunAsync_NoIssues_DoesNotCallAlertSender
- RunAsync_WithExpiredLeis_CallsAlertSender
- RunAsync_WithExpiredLeis_IsExpiredTrue_CallsAlertSender
- RunAsync_RepositoryThrows_DoesNotSwallowException

Additional notes

- Config via environment variables only — no appsettings.json secrets, no local.settings.json
- All async throughout — no sync-over-async
- Container-ready: non-root user in Dockerfile, no Windows-specific dependencies, no registry access
- Do NOT reference Shawbrook.Core.Utilities or any .NET Framework packages
- Do NOT use DataLoader<T> — use Dapper directly
- Do NOT use Autofac — use Microsoft.Extensions.DependencyInjection
- Do NOT include any Azure Functions packages
- Exit code behaviour: the Worker must return exit code 0 on clean completion and propagate a non-zero exit code on unhandled exception so the CronJob backoff and failure history work correctly
