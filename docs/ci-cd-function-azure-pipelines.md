# CI/CD for LeiMonitor.Function — Azure Pipelines Guide

**What this covers:** How `azure-pipelines-function.yml` is structured, why each decision was made, and how to wire up the Service Connection and pipeline in Azure DevOps.

---

## Pipeline overview

```
Push to master ──► Build ──► (if green) ──► Publish & Deploy ──► Azure Function App
Pull request   ──► Build ──► (no deploy)
```

Two stages run in sequence:

| Stage | Triggers on | Purpose |
|---|---|---|
| `Build` | Every push and every PR | Restore, compile |
| `Deploy` | Push to `master` only, after `Build` passes | `dotnet publish`, zip, deploy via `AzureFunctionApp@2` |

A pull request can never deploy. Deployment only happens when a change lands on `master`.

---

## How this differs from the GitHub Actions workflow

| Concern | GitHub Actions (`.github/workflows/function-ci-cd.yml`) | Azure Pipelines (`azure-pipelines-function.yml`) |
|---|---|---|
| **Schema** | `jobs` / `steps` / `uses` | `stages` / `jobs` / `steps` / `task` |
| **Azure login** | `azure/login@v2` + OIDC secrets | **Service Connection** — no secrets in YAML |
| **Function deploy** | `az functionapp deployment source config-zip` via CLI | `AzureFunctionApp@2` task (dedicated, simpler) |
| **Zip** | `zip` shell command | `ArchiveFiles@2` task (cross-platform) |
| **Environments** | `environment: dev` on job | `environment: dev` on `deployment` job (same concept) |
| **Secrets** | `${{ secrets.NAME }}` | `$(NAME)` — pipeline variables or variable groups |
| **Path filters** | `on.push.paths` | `trigger.paths.include` |

The `dotnet restore`, `dotnet build`, and `dotnet publish` commands are identical in both — those are just CLI commands.

---

## One-time setup: Service Connection

A Service Connection replaces all the OIDC app registration steps from the GitHub Actions guide. Azure DevOps manages the credential lifecycle for you.

1. In Azure DevOps, go to your **Project Settings** (bottom-left cog).
2. Under **Pipelines**, click **Service connections** → **New service connection**.
3. Select **Azure Resource Manager** → **Next**.
4. Select **Workload Identity federation (automatic)** → **Next**.
   - This is the recommended option — it uses OIDC under the hood with no secrets to manage.
5. Fill in:
   - **Scope level:** Resource Group
   - **Subscription:** select your subscription
   - **Resource group:** `rg-lei-monitor-dev`
   - **Service connection name:** `sc-lei-monitor-dev`
   - **Grant access permission to all pipelines:** check this for now (tighten later)
6. Click **Save**.

Azure DevOps creates and manages the app registration and federated credential in your tenant automatically.

---

## One-time setup: pipeline variable

The `azureServiceConnection` variable is not hardcoded in the YAML (the Service Connection name could differ per environment). Set it as a pipeline variable:

1. In Azure DevOps, open the pipeline → **Edit** → **Variables** (top right).
2. Add a variable:
   - **Name:** `azureServiceConnection`
   - **Value:** `sc-lei-monitor-dev`
   - **Keep this value secret:** No (it is not a credential, just a name)
3. Click **Save**.

`azureResourceGroup` and `azureFunctionAppName` are defined directly in the YAML `variables` block because they are not sensitive.

---

## One-time setup: create the pipeline in Azure DevOps

1. In Azure DevOps, go to **Pipelines** → **New pipeline**.
2. Select **GitHub** (if your repo is on GitHub) or **Azure Repos Git**.
3. Select the `LeiMonitor` repository.
4. Select **Existing Azure Pipelines YAML file**.
5. Set the path to `/azure-pipelines-function.yml`.
6. Click **Continue** → **Run**.

---

## One-time setup: Azure DevOps Environment

The `Deploy` stage references `environment: dev`. Azure DevOps creates this automatically on first run, but you can configure it in advance to add approval gates:

1. Go to **Pipelines** → **Environments** → **New environment**.
2. Name it `dev`, resource type **None**.
3. Open the environment → **Approvals and checks** → **+ Add** → **Approvals**.
4. Add yourself or a team as a required approver.

Once configured, every deployment to `dev` will pause and wait for approval before the `AzureFunctionApp@2` task runs.

---

## Key decisions

### `deployment` job instead of a plain `job`

The `Deploy` stage uses a `deployment` job with `strategy: runOnce`. This is required to reference an Azure DevOps **Environment**, which is what gives you deployment history, approval gates, and the deployment tracking UI. A plain `job` cannot reference an environment.

### `AzureFunctionApp@2` instead of raw CLI

```yaml
- task: AzureFunctionApp@2
  inputs:
    connectedServiceNameARM: $(azureServiceConnection)
    appType: functionApp
    appName: $(azureFunctionAppName)
    package: $(Pipeline.Workspace)/publish/function.zip
    deploymentMethod: zipDeploy
```

This task wraps `az functionapp deployment source config-zip` but handles authentication via the Service Connection automatically. No `az login` step is needed.

### `ArchiveFiles@2` instead of a shell zip command

The GitHub Actions workflow uses a `zip` shell command which only works on Linux. `ArchiveFiles@2` is a cross-platform Azure DevOps task that works on `ubuntu-latest`, `windows-latest`, and `macos-latest` without changes.

### `--no-self-contained` publish

The Functions host on Azure already has .NET 9. Bundling the runtime would make the zip larger and the deployment slower.

### `condition` on the Deploy stage

```yaml
condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/master'))
```

This is the Azure Pipelines equivalent of GitHub Actions' `if: github.event_name == 'push' && github.ref == 'refs/heads/master'`. It ensures PRs never trigger a deployment even if the `pr:` trigger fires.

---

## What the pipeline does NOT do (and why)

| Concern | Decision |
|---|---|
| Automated tests | Intentionally excluded — same decision as the GitHub Actions workflow. |
| Multi-environment (staging, prod) | Add a second `Deploy` stage with `dependsOn: Deploy` and `environment: prod` when ready. |
| Variable groups / Key Vault linking | For a PoC, plain pipeline variables are sufficient. Link a Variable Group to Azure Key Vault under **Pipelines → Library** when moving to production. |
| NuGet caching | Low priority at this project size. Add a `Cache@2` task on `$(Pipeline.Workspace)/.nuget/packages` if restore times become material. |
