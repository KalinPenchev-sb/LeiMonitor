# Deploy LeiMonitor.Function to Azure

**Audience:** Developer deploying for the first time from a bare Azure subscription  
**Starting point:** Active Azure subscription, no resource group  
**What gets deployed:** `LeiMonitor.Function` — an Azure Functions v4 isolated-process timer trigger (daily 08:00 UTC) running on .NET 9

---

## Prerequisites

Ensure the following are installed and available before starting:

| Tool | Check | Install |
|---|---|---|
| Azure CLI | `az --version` | https://aka.ms/installazurecliwindows |
| .NET 9 SDK | `dotnet --version` | https://dotnet.microsoft.com/download/dotnet/9 |

---

## Step 1 — Define variables

Run this block once at the start of your terminal session. Every subsequent command references these variables so nothing is typed twice.

```powershell
# --- change these three values ---
$subscriptionId = "<YOUR_SUBSCRIPTION_ID>"
$sqlConnectionString = "<SQL_SERVER_CONNECTION_STRING_FOR_CustomerStorage>"

# --- adjust region if needed ---
$location = "uksouth"

# --- resource names (change suffix if names are already taken) ---
$rg              = "rg-lei-monitor-dev"
$storageAccount  = "stleimonitordev"        # globally unique, 3–24 lowercase alphanumeric only
$logAnalytics    = "log-lei-monitor-dev"
$appInsights     = "appi-lei-monitor-dev"
$functionApp     = "func-lei-monitor-dev"   # globally unique, becomes func-lei-monitor-dev.azurewebsites.net
```

> **Storage account name rules:** 3–24 characters, lowercase letters and numbers only, globally unique across all of Azure. If `stleimonitordev` is taken, try `stleimon<yourname>`.

---

## Step 2 — Log in and select subscription

```powershell
az login
az account set --subscription $subscriptionId

# Confirm the correct subscription is active
az account show --query "{Name:name, Id:id, State:state}" -o table
```

---

## Step 3 — Create the resource group

All resources in this guide are placed in one resource group for easy management and clean-up.

```powershell
az group create `
  --name $rg `
  --location $location
```

Expected output includes `"provisioningState": "Succeeded"`.

---

## Step 4 — Create the storage account

Azure Functions requires a storage account for internal state (trigger leases, host coordination, deployment packages).

```powershell
az storage account create `
  --name $storageAccount `
  --resource-group $rg `
  --location $location `
  --sku Standard_LRS `
  --kind StorageV2 `
  --allow-blob-public-access false
```

---

## Step 5 — Create a Log Analytics workspace

Application Insights requires a Log Analytics workspace (workspace-based mode).

```powershell
az monitor log-analytics workspace create `
  --resource-group $rg `
  --workspace-name $logAnalytics `
  --location $location `
  --sku PerGB2018 `
  --retention-time 30
```

> **Cost note:** `--retention-time 30` sets the minimum allowed retention (30 days). The default is 90 days. `PerGB2018` is the pay-as-you-go SKU — first 5 GB ingested per month is free.

---

## Step 6 — Create Application Insights

Retrieves the instrumentation key that will be wired into the Function App automatically.

```powershell
az monitor app-insights component create `
  --app $appInsights `
  --resource-group $rg `
  --location $location `
  --kind web `
  --workspace (az monitor log-analytics workspace show `
    --resource-group $rg `
    --workspace-name $logAnalytics `
    --query id -o tsv)
```

Set a daily data ingestion cap of 0.1 GB (100 MB) to prevent unexpected charges. A once-daily function produces negligible telemetry; this cap is a safety ceiling only.

```powershell
az monitor app-insights component billing update `
  --app $appInsights `
  --resource-group $rg `
  --cap 0.1
```

---

## Step 7 — Create the Function App

Creates the Function App on a **Consumption plan** (pay-per-execution, scales to zero between the daily runs). The runtime is `dotnet-isolated` targeting .NET 9.

```powershell
az functionapp create `
  --resource-group $rg `
  --consumption-plan-location $location `
  --runtime dotnet-isolated `
  --runtime-version 9 `
  --functions-version 4 `
  --name $functionApp `
  --storage-account $storageAccount `
  --app-insights $appInsights
```

This command provisions the Function App, wires Application Insights automatically, and sets `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated`.

---

## Step 8 — Configure the connection string

The `LeiRepository` reads `ConnectionStrings:CustomerStorage` from `IConfiguration`. In Azure, that maps to an App Setting named `ConnectionStrings__CustomerStorage` (double underscore = colon separator for environment variables in .NET).

```powershell
az functionapp config appsettings set `
  --resource-group $rg `
  --name $functionApp `
  --settings "ConnectionStrings__CustomerStorage=$sqlConnectionString"
```

Verify the setting was saved:

```powershell
az functionapp config appsettings list `
  --resource-group $rg `
  --name $functionApp `
  --query "[?name=='ConnectionStrings__CustomerStorage'].{Name:name, Value:value}" `
  -o table
```

---

## Step 9 — Publish the function

### 9a. Build and publish to a local folder

Run from the repository root (`C:\Repos\LeiMonitor`):

```powershell
dotnet publish src/LeiMonitor.Function/LeiMonitor.Function.csproj `
  --configuration Release `
  --output ./publish/function `
  --no-self-contained
```

### 9b. Zip the publish output

```powershell
Compress-Archive `
  -Path ./publish/function/* `
  -DestinationPath ./publish/function.zip `
  -Force
```

### 9c. Deploy the zip to Azure

```powershell
az functionapp deployment source config-zip `
  --resource-group $rg `
  --name $functionApp `
  --src ./publish/function.zip
```

Expected output includes `"provisioningState": "Succeeded"`.

---

## Step 10 — Verify the deployment

### Check the function is listed

```powershell
az functionapp function list `
  --resource-group $rg `
  --name $functionApp `
  --query "[].{Name:name, Language:language}" `
  -o table
```

You should see `LeiExpiryCheck` in the list.

### Trigger a manual run (optional smoke test)

```powershell
az functionapp function invoke `
  --resource-group $rg `
  --name $functionApp `
  --function-name LeiExpiryCheck `
  --data '{}'
```

### Check logs in Application Insights

Open the Azure Portal, navigate to the `appi-lei-monitor-dev` Application Insights resource, and go to **Logs**. Run:

```kusto
traces
| where timestamp > ago(10m)
| order by timestamp desc
```

Successful runs log either:
- `LEI expiry check completed — no issues found.`
- `LEI expiry check found N issue(s). Sending alert.`

---

## Estimated monthly cost

All figures are approximate for a once-daily timer trigger in `uksouth`.

| Resource | Pricing model | Expected monthly cost |
|---|---|---|
| Function App (Consumption) | First 1 million executions free, then £0.169 per million | **£0.00** (1 execution/day = ~31/month) |
| Storage Account (Standard LRS) | ~£0.016/GB stored + tiny transaction cost | **< £0.05** |
| Log Analytics (PerGB2018) | First 5 GB/month free | **£0.00** (a daily function logs < 1 MB/month) |
| Application Insights | Billed via Log Analytics workspace | **£0.00** (within free tier, capped at 0.1 GB/day) |
| **Total** | | **< £0.05/month** |

> The dominant cost driver if this function runs correctly is effectively zero. Cost only becomes non-zero if the daily cap is hit repeatedly or the storage account accumulates deployment artifacts over time.

---

## Resource summary

| Resource | Name | Purpose |
|---|---|---|
| Resource Group | `rg-lei-monitor-dev` | Container for all resources |
| Storage Account | `stleimonitordev` | Functions internal state and deployment |
| Log Analytics Workspace | `log-lei-monitor-dev` | Backend for Application Insights |
| Application Insights | `appi-lei-monitor-dev` | Logging and live metrics |
| Function App | `func-lei-monitor-dev` | Hosts `LeiExpiryCheck` on Consumption plan |

---

## Configuration reference

| App Setting | Value | Purpose |
|---|---|---|
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` | Set automatically by `az functionapp create` |
| `AzureWebJobsStorage` | set automatically | Functions internal storage connection |
| `APPINSIGHTS_INSTRUMENTATIONKEY` | set automatically | Telemetry |
| `ConnectionStrings__CustomerStorage` | your SQL connection string | Maps to `ConnectionStrings:CustomerStorage` in `IConfiguration` |

---

## Tear down

To remove all resources and stop all billing:

```powershell
az group delete --name $rg --yes --no-wait
```

---

## Next steps

- **Secrets management:** Move `ConnectionStrings__CustomerStorage` out of App Settings and into Azure Key Vault with a Key Vault reference (`@Microsoft.KeyVault(SecretUri=...)`).
- **VNet integration:** If CustomerStorage is behind a private endpoint, upgrade to a Premium plan and enable VNet Integration.
- **CI/CD:** Add a GitHub Actions workflow that runs `dotnet publish`, zips the output, and calls `az functionapp deployment source config-zip` on push to `main`.
