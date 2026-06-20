# Deploy LeiMonitor.Function to Azure — Portal Guide

**Audience:** Developer deploying for the first time using only a browser  
**Starting point:** Active Azure subscription, no resource group  
**What gets deployed:** `LeiMonitor.Function` — an Azure Functions v4 isolated-process timer trigger (daily 08:00 UTC) running on .NET 9

---

## Prerequisites

| Tool | Check | Install |
|---|---|---|
| .NET 9 SDK | `dotnet --version` in a terminal | https://dotnet.microsoft.com/download/dotnet/9 |
| PowerShell | Built into Windows | — |
| Azure account | https://portal.azure.com | — |

A terminal is only needed for the one `dotnet publish` + zip step. Everything else is done in the portal.

---

## Step 1 — Create a resource group

1. Go to https://portal.azure.com and sign in.
2. In the top search bar type **Resource groups** and select it.
3. Click **+ Create**.
4. Fill in the form:
   - **Subscription:** select your subscription
   - **Resource group:** `rg-lei-monitor-dev`
   - **Region:** `UK South`
5. Click **Review + create** → **Create**.

---

## Step 2 — Create a storage account

Azure Functions requires a storage account for trigger leases, host coordination, and deployment packages.

1. Search for **Storage accounts** in the top bar and select it.
2. Click **+ Create**.
3. Fill in the **Basics** tab:
   - **Subscription:** your subscription
   - **Resource group:** `rg-lei-monitor-dev`
   - **Storage account name:** `stleimonitordev` *(must be globally unique — if taken, try `stleimon<yourname>`)*
   - **Region:** `UK South`
   - **Performance:** Standard
   - **Redundancy:** Locally-redundant storage (LRS) — cheapest option
4. Go to the **Advanced** tab:
   - **Allow Blob public access:** Disabled
5. Leave all other tabs at their defaults.
6. Click **Review** → **Create**.

---

## Step 3 — Create a Log Analytics workspace

Application Insights stores its data in a Log Analytics workspace.

1. Search for **Log Analytics workspaces** and select it.
2. Click **+ Create**.
3. Fill in the form:
   - **Subscription:** your subscription
   - **Resource group:** `rg-lei-monitor-dev`
   - **Name:** `log-lei-monitor-dev`
   - **Region:** `UK South`
4. Click **Review + Create** → **Create**.
5. Once deployed, open the workspace, go to **Usage and estimated costs** → **Data Retention**.
6. Drag the retention slider to **30 days** (the minimum) and click **OK**.

> **Why:** The default retention is 90 days. 30 days is enough for a dev workload and is the cheapest setting.

---

## Step 4 — Create an Application Insights resource

1. Search for **Application Insights** and select it.
2. Click **+ Create**.
3. Fill in the form:
   - **Subscription:** your subscription
   - **Resource group:** `rg-lei-monitor-dev`
   - **Name:** `appi-lei-monitor-dev`
   - **Region:** `UK South`
   - **Resource Mode:** Workspace-based
   - **Log Analytics Workspace:** `log-lei-monitor-dev`
4. Click **Review + create** → **Create**.

### Set a daily data cap

5. Open the newly created `appi-lei-monitor-dev` resource.
6. In the left menu go to **Configure** → **Usage and estimated costs**.
7. Click **Daily cap**.
8. Set the cap to **0.1 GB** and click **OK**.

> **Why:** A once-daily function produces negligible telemetry. The cap prevents unexpected charges if something misbehaves.

---

## Step 5 — Create the Function App

1. Search for **Function App** and select it.
2. Click **+ Create** → **Consumption** (the default hosted option — cheapest, scales to zero).
3. Fill in the **Basics** tab:
   - **Subscription:** your subscription
   - **Resource group:** `rg-lei-monitor-dev`
   - **Function App name:** `func-lei-monitor-dev` *(globally unique — this becomes `func-lei-monitor-dev.azurewebsites.net`)*
   - **Runtime stack:** .NET
   - **Version:** 9 (isolated)
   - **Region:** `UK South`
   - **Operating System:** Windows *(Consumption plan on Linux requires a separate storage account in some regions — Windows avoids that complication)*
4. Go to the **Storage** tab:
   - **Storage account:** select `stleimonitordev`
5. Go to the **Monitoring** tab:
   - **Enable Application Insights:** Yes
   - **Application Insights:** select `appi-lei-monitor-dev`
6. Leave all other tabs at their defaults.
7. Click **Review + create** → **Create**.

---

## Step 6 — Add the connection string

The `LeiRepository` reads `ConnectionStrings:CustomerStorage` from `IConfiguration`. In Azure this is set as an **Application Setting** using the environment variable naming convention (`__` replaces `:`).

1. Open the `func-lei-monitor-dev` Function App.
2. In the left menu go to **Settings** → **Environment variables**.
3. Click the **+ Add** button.
4. Fill in:
   - **Name:** `ConnectionStrings__CustomerStorage`
   - **Value:** your full SQL Server connection string for the `CustomerStorage` database
5. Click **Apply**, then **Apply** again on the confirmation banner at the bottom.

> **Verify:** After saving, the setting should appear in the list with its name. The value is hidden by default — click the eye icon to confirm it was saved correctly.

---

## Step 7 — Build and zip the publish output

Run the following in a terminal from the repository root (`C:\Repos\LeiMonitor`):

```powershell
# Build and publish to a local folder
dotnet publish src/LeiMonitor.Function/LeiMonitor.Function.csproj `
  --configuration Release `
  --output ./publish/function `
  --no-self-contained

# Zip the publish output
Compress-Archive `
  -Path ./publish/function/* `
  -DestinationPath ./publish/function.zip `
  -Force
```

This produces `publish/function.zip` in the repository root.

---

## Step 8 — Deploy the zip via the portal

1. Open the `func-lei-monitor-dev` Function App in the portal.
2. In the left menu go to **Deployment** → **Advanced Tools** and click **Go →**.
   This opens the Kudu console in a new tab.
3. In Kudu, go to **Tools** → **Zip Push Deploy**.
4. Drag and drop `publish/function.zip` onto the page, or use the file picker.
5. Wait for the upload to complete. Kudu extracts and deploys the zip automatically.

> **Alternative:** In the Function App left menu, go to **Deployment** → **Deployment Center**, select **External Git** or use **Manual Deploy** → **ZIP Deploy** if that option is visible in your portal version.

---

## Step 9 — Verify the deployment

### Check the function appears

1. In the Function App left menu click **Functions**.
2. You should see **LeiExpiryCheck** listed with type `TimerTrigger`.

### Trigger a manual test run

1. Click on **LeiExpiryCheck**.
2. Click **Test/Run** in the top bar.
3. Leave the input body empty and click **Run**.
4. The **Output** panel shows the function invocation log in real time.

You should see one of:
- `LEI expiry check completed — no issues found.`
- `LEI expiry check found N issue(s). Sending alert.`

### Check Application Insights logs

1. Open `appi-lei-monitor-dev` in the portal.
2. Go to **Monitoring** → **Logs**.
3. Run this query:

```kusto
traces
| where timestamp > ago(10m)
| order by timestamp desc
```

---

## Estimated monthly cost

| Resource | Pricing model | Expected monthly cost |
|---|---|---|
| Function App (Consumption) | First 1 million executions free | **£0.00** |
| Storage Account (Standard LRS) | ~£0.016/GB + transactions | **< £0.05** |
| Log Analytics (PerGB2018) | First 5 GB/month free | **£0.00** |
| Application Insights | Billed via Log Analytics | **£0.00** (capped at 0.1 GB/day) |
| **Total** | | **< £0.05/month** |

---

## Resource summary

| Resource | Name |
|---|---|
| Resource Group | `rg-lei-monitor-dev` |
| Storage Account | `stleimonitordev` |
| Log Analytics Workspace | `log-lei-monitor-dev` |
| Application Insights | `appi-lei-monitor-dev` |
| Function App | `func-lei-monitor-dev` |

---

## Tear down

To delete everything and stop all billing:

1. Search for **Resource groups** in the portal.
2. Select `rg-lei-monitor-dev`.
3. Click **Delete resource group**.
4. Type the resource group name to confirm and click **Delete**.

All five resources are deleted together.

---

## Next steps

- **Secrets management:** Replace the plain connection string in Application Settings with an Azure Key Vault reference: `@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/<name>/)`.
- **VNet integration:** If `CustomerStorage` is behind a private endpoint, upgrade to a **Premium plan** and configure VNet Integration under **Settings → Networking**.
- **CI/CD:** See the CLI guide (`deploy-function-to-azure.md`) for a GitHub Actions workflow that automates Steps 7 and 8.
