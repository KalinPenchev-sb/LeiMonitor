# Create LeiMonitor.Function – Azure Functions v4 Isolated Timer Trigger

## Context

The `LeiMonitor` solution already exists. The following projects are already in place and must not be modified:

- `src/LeiMonitor.Core` — business logic, interfaces, models
- `src/LeiMonitor.Data` — Dapper repository and stub alert sender
- `tests/LeiMonitor.Core.Tests` — xUnit tests for `LeiExpiryChecker`

The task is to add a new project `src/LeiMonitor.Function` that hosts the LEI expiry check as a managed Azure Functions v4 isolated-process timer trigger. This is Option A from the spike. The scheduling concern is owned entirely by the Azure Functions runtime via a CRON expression. The application runs as a long-lived host, not a run-and-exit console app.

---

## Existing code to be aware of

### `LeiMonitor.Core` types (do not recreate)

```csharp
// src/LeiMonitor.Core/Models/LeiIssue.cs
namespace LeiMonitor.Core.Models;

public class LeiIssue
{
    public Guid CustomerId { get; set; }
    public string LeiCode { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public DateTime? ExpirationDate { get; set; }
    public bool IsExpired { get; set; }
}

// src/LeiMonitor.Core/Interfaces/ILeiRepository.cs
namespace LeiMonitor.Core.Interfaces;

public interface ILeiRepository
{
    Task<IReadOnlyList<LeiIssue>> GetIssuesAsync(CancellationToken ct = default);
}

// src/LeiMonitor.Core/Interfaces/IAlertSender.cs
namespace LeiMonitor.Core.Interfaces;

public interface IAlertSender
{
    Task SendAsync(IReadOnlyList<LeiIssue> issues, CancellationToken ct = default);
}

// src/LeiMonitor.Core/Services/LeiExpiryChecker.cs
namespace LeiMonitor.Core.Services;

public class LeiExpiryChecker
{
    public LeiExpiryChecker(ILeiRepository repository, IAlertSender alertSender,
        ILogger<LeiExpiryChecker> logger) { ... }

    public async Task RunAsync(CancellationToken ct = default) { ... }
}
```

### `LeiMonitor.Data` types (do not recreate)

```csharp
// src/LeiMonitor.Data/Repositories/LeiRepository.cs
// Implements ILeiRepository. Reads connection string "CustomerStorage" from IConfiguration.
// Uses Dapper + Microsoft.Data.SqlClient.

// src/LeiMonitor.Data/Notifications/EmailAlertSender.cs
// Implements IAlertSender. Stub — logs a warning per issue. No real email transport.
```

---

## Project to create: `LeiMonitor.Function`

### Target framework and runtime

- Target framework: `net9.0`
- Azure Functions version: v4 isolated process (`dotnet-isolated`)
- Output type: `Exe`
- Nullable and implicit usings: enabled

### Project file: `src/LeiMonitor.Function/LeiMonitor.Function.csproj`

NuGet packages:

| Package | Version |
|---|---|
| `Microsoft.Azure.Functions.Worker` | `1.23.0` |
| `Microsoft.Azure.Functions.Worker.Extensions.Timer` | `4.3.1` |
| `Microsoft.Azure.Functions.Worker.Sdk` | `1.18.0` (build-only, `PrivateAssets=all`) |
| `Microsoft.Extensions.Configuration` | `9.0.0` |
| `Microsoft.Extensions.DependencyInjection` | `9.0.0` |
| `Microsoft.Extensions.Hosting` | `9.0.0` |
| `Microsoft.Extensions.Logging` | `9.0.0` |

Project references:

- `../LeiMonitor.Core/LeiMonitor.Core.csproj`
- `../LeiMonitor.Data/LeiMonitor.Data.csproj`

Build items:

- `host.json` — `CopyToOutputDirectory: PreserveNewest`
- `local.settings.json` — `CopyToOutputDirectory: PreserveNewest`, `CopyToPublishDirectory: Never`

### `Program.cs`

Use `HostBuilder` with `ConfigureFunctionsWorkerDefaults`. Register the following services as singletons:

- `ILeiRepository` → `LeiRepository`
- `IAlertSender` → `EmailAlertSender`
- `LeiExpiryChecker`

Call `await host.RunAsync()`.

```csharp
using LeiMonitor.Core.Interfaces;
using LeiMonitor.Core.Services;
using LeiMonitor.Data.Notifications;
using LeiMonitor.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        services.AddSingleton<ILeiRepository, LeiRepository>();
        services.AddSingleton<IAlertSender, EmailAlertSender>();
        services.AddSingleton<LeiExpiryChecker>();
    })
    .Build();

await host.RunAsync();
```

### `LeiExpiryTimerFunction.cs`

Namespace: `LeiMonitor.Function`

- Class: `LeiExpiryTimerFunction`
- Constructor takes `LeiExpiryChecker` (injected by DI)
- One method `Run` decorated with `[Function("LeiExpiryCheck")]`
- Timer trigger CRON expression: `"0 0 8 * * *"` (daily at 08:00 UTC, six-part Azure Functions format)
- Method signature receives `TimerInfo timer`, `FunctionContext context`, and `CancellationToken ct`
- Calls `await _checker.RunAsync(ct)`

```csharp
using LeiMonitor.Core.Services;
using Microsoft.Azure.Functions.Worker;

namespace LeiMonitor.Function;

public class LeiExpiryTimerFunction
{
    private readonly LeiExpiryChecker _checker;

    public LeiExpiryTimerFunction(LeiExpiryChecker checker)
    {
        _checker = checker;
    }

    [Function("LeiExpiryCheck")]
    public async Task Run(
        [TimerTrigger("0 0 8 * * *")] TimerInfo timer,
        FunctionContext context,
        CancellationToken ct)
    {
        await _checker.RunAsync(ct);
    }
}
```

### `host.json`

```json
{
    "version": "2.0",
    "logging": {
        "applicationInsights": {
            "samplingSettings": {
                "isEnabled": true,
                "excludedTypes": "Request"
            },
            "enableLiveMetricsFilters": true
        }
    }
}
```

### `local.settings.json`

```json
{
    "IsEncrypted": false,
    "Values": {
        "AzureWebJobsStorage": "UseDevelopmentStorage=true",
        "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated"
    },
    "ConnectionStrings": {
        "CustomerStorage": "<connection string to Dev_CustomerStorage>"
    }
}
```

This file must never be published (enforced by `CopyToPublishDirectory: Never` in the project file). It must not contain real secrets.

---

## Solution structure after this task

```
LeiMonitor.sln
├── src/
│   ├── LeiMonitor.Core/           (.NET 9 class library)        ← already exists
│   ├── LeiMonitor.Data/           (.NET 9 class library)        ← already exists
│   └── LeiMonitor.Function/       (.NET 9 Azure Function v4)    ← create this
├── tests/
│   └── LeiMonitor.Core.Tests/     (.NET 9 xUnit test project)   ← already exists
```

---

## Additional notes

- Target `net9.0` throughout — no `net8.0` references.
- Do not add `Autofac`. Use `Microsoft.Extensions.DependencyInjection` only.
- Do not add a `Dockerfile`. The Function is zip-deployed, not containerised.
- Do not add Kubernetes manifests. Those belong to the AKS prompt.
- The connection string is read from `ConnectionStrings:CustomerStorage`, satisfied locally by `local.settings.json` and in Azure by an Application Setting named `ConnectionStrings__CustomerStorage`.
- Application Insights is wired automatically via `ConfigureFunctionsWorkerDefaults` — no explicit SDK reference is needed.
- The `LeiExpiryChecker` is the single orchestration point; the function is a thin trigger wrapper only.
- All async throughout — no sync-over-async.
