# Deploy LeiMonitor Worker to AKS — Greenfield Setup

## Context

The `LeiMonitor` solution already exists at `C:\Repos\LeiMonitor` and on GitHub at
`https://github.com/KalinPenchev-sb/LeiMonitor` (branch: `master`).

The Azure Functions path (`LeiMonitor.Function`) is already deployed and working via GitHub Actions.

This task covers the **AKS path only**: build a container image from `LeiMonitor.Worker`,
push it to a new Azure Container Registry, and deploy it as a Kubernetes CronJob on a new AKS cluster.
No existing ACR or AKS cluster is available — everything must be provisioned from scratch.

---

## What already exists in the repo (do not recreate)

### Solution structure

```
LeiMonitor.sln
├── src/
│   ├── LeiMonitor.Core/       (.NET 9 class library — models, interfaces, LeiExpiryChecker)
│   ├── LeiMonitor.Data/       (.NET 9 class library — LeiRepository (Dapper), EmailAlertSender)
│   ├── LeiMonitor.Function/   (.NET 9 Azure Function v4 — already deployed, ignore for this task)
│   └── LeiMonitor.Worker/     (.NET 9 console app — Generic Host, exits 0/1, THIS is the AKS target)
├── tests/
│   └── LeiMonitor.Core.Tests/
└── k8s/
    ├── namespace.yaml
    ├── serviceaccount.yaml
    ├── secret.yaml
    ├── configmap.yaml
    └── cronjob.yaml
```

### `src/LeiMonitor.Worker/Program.cs` (already implemented)

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config => { config.AddEnvironmentVariables(); })
    .ConfigureServices((ctx, services) =>
    {
        services.AddSingleton<ILeiRepository, LeiRepository>();
        services.AddSingleton<IAlertSender, EmailAlertSender>();
        services.AddSingleton<LeiExpiryChecker>();
    })
    .Build();

try
{
    var checker = host.Services.GetRequiredService<LeiExpiryChecker>();
    await checker.RunAsync();
    return 0;
}
catch (Exception ex)
{
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Unhandled exception. Exiting.");
    return 1;
}
```

### `Dockerfile` (already exists at repo root)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/LeiMonitor.Core/LeiMonitor.Core.csproj src/LeiMonitor.Core/
COPY src/LeiMonitor.Data/LeiMonitor.Data.csproj src/LeiMonitor.Data/
COPY src/LeiMonitor.Worker/LeiMonitor.Worker.csproj src/LeiMonitor.Worker/
RUN dotnet restore src/LeiMonitor.Worker/LeiMonitor.Worker.csproj

COPY src/LeiMonitor.Core/ src/LeiMonitor.Core/
COPY src/LeiMonitor.Data/ src/LeiMonitor.Data/
COPY src/LeiMonitor.Worker/ src/LeiMonitor.Worker/

RUN dotnet publish src/LeiMonitor.Worker/LeiMonitor.Worker.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser
COPY --from=build /app .
ENTRYPOINT ["dotnet", "LeiMonitor.Worker.dll"]
```

### `k8s/` manifests (already exist, placeholders need filling)

**namespace.yaml**
```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: lei-monitor
```

**serviceaccount.yaml**
```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: lei-monitor-sa
  namespace: lei-monitor
  annotations:
    azure.workload.identity/client-id: "<AZURE_MANAGED_IDENTITY_CLIENT_ID>"
```

**secret.yaml**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: lei-monitor-secret
  namespace: lei-monitor
type: Opaque
stringData:
  # Placeholder only — replace with real value or use Key Vault CSI driver
  CustomerStorage: "<SQL_SERVER_CONNECTION_STRING>"
```

**configmap.yaml**
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: lei-monitor-config
  namespace: lei-monitor
data:
  DOTNET_ENVIRONMENT: "Production"
```

**cronjob.yaml**
```yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: lei-expiry-check
  namespace: lei-monitor
spec:
  schedule: "0 8 * * *"
  concurrencyPolicy: Forbid
  successfulJobsHistoryLimit: 3
  failedJobsHistoryLimit: 3
  jobTemplate:
    spec:
      backoffLimit: 2
      template:
        spec:
          serviceAccountName: lei-monitor-sa
          restartPolicy: Never
          containers:
            - name: lei-monitor
              image: REGISTRY/lei-monitor:TAG   # <-- placeholder to replace
              env:
                - name: ConnectionStrings__CustomerStorage
                  valueFrom:
                    secretKeyRef:
                      name: lei-monitor-secret
                      key: CustomerStorage
              envFrom:
                - configMapRef:
                    name: lei-monitor-config
              resources:
                requests:
                  cpu: 100m
                  memory: 128Mi
                limits:
                  cpu: 250m
                  memory: 256Mi
```

---

## What needs to be done (in order)

### Step 1 — Provision infrastructure (Azure CLI, one-time)

Provision the following from scratch using the cheapest available SKUs:

- Resource group: `rg-lei-monitor-aks-dev` in `uksouth`
- Azure Container Registry: Basic SKU (cheapest), name `acrleimonitodev` (or similar unique name)
- AKS cluster: single node pool, 1 node, `Standard_B2s` VM size (cheapest for AKS), Kubernetes RBAC enabled
- Attach the ACR to the AKS cluster so the cluster can pull images without extra credentials

Provide all commands as a PowerShell script with variables defined at the top.

### Step 2 — Build and push the Docker image manually (one-time smoke test)

Provide the `docker build` and `docker push` commands to:
- Build the image from the `Dockerfile` at the repo root
- Tag it as `<acr-name>.azurecr.io/lei-monitor:latest`
- Push it to ACR

### Step 3 — Update `k8s/cronjob.yaml`

Replace the `image: REGISTRY/lei-monitor:TAG` placeholder with the real ACR image URL.

### Step 4 — Apply the manifests

Provide the `kubectl` commands to:
- Connect `kubectl` to the new AKS cluster
- Apply all five manifests in `k8s/` in the correct order

### Step 5 — Verify the CronJob

Provide commands to:
- Confirm the CronJob is registered
- Trigger a manual run immediately (without waiting for 08:00 UTC)
- Watch the pod logs to confirm it ran successfully

### Step 6 — GitHub Actions CI/CD for the Worker

Create a new workflow file `.github/workflows/worker-ci-cd.yml` that:
- Triggers on push to `master` for paths `src/LeiMonitor.Core/**`, `src/LeiMonitor.Data/**`,
  `src/LeiMonitor.Worker/**`, `Dockerfile`, `.github/workflows/worker-ci-cd.yml`
- Also triggers on `workflow_dispatch`
- Job 1 (build): `docker build` to validate the image builds
- Job 2 (deploy, master only): build, tag with `${{ github.sha }}` and `latest`, push to ACR,
  then update the CronJob image on the cluster using `kubectl set image`
- Uses OIDC login (`azure/login@v2`) — same secrets as the Function pipeline:
  `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
- ACR name and AKS cluster name stored as `env` vars in the workflow (not secrets — not sensitive)
- `permissions: id-token: write` and `contents: read` at the top level

### Step 7 — Role assignments for the pipeline identity

The existing app registration `sp-lei-monitor-github` (already used for the Function pipeline)
needs two additional role assignments:
- `AcrPush` on the ACR
- `Azure Kubernetes Service Cluster User Role` on the AKS cluster

Provide the `az role assignment create` commands.

---

## Constraints

- Do NOT recreate any files that already exist unless explicitly asked to update them
- Do NOT add Azure Functions packages to the Worker project
- Do NOT use Autofac — use `Microsoft.Extensions.DependencyInjection`
- Do NOT use a Windows base image in the Dockerfile
- The Worker exits with code 0 on success and non-zero on exception — this is already implemented
- Config is via environment variables only — no `appsettings.json`
- Use cheapest available Azure SKUs throughout (Basic ACR, Standard_B2s AKS node)
