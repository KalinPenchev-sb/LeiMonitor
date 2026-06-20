# CI/CD for LeiMonitor.Function — Best Practices Guide

**What this covers:** How the GitHub Actions pipeline in `.github/workflows/function-ci-cd.yml` is structured, why each decision was made, and how to wire up the required secrets in GitHub and Azure.

---

## Pipeline overview

```
Push to master ──► Build & Test ──► (if green) ──► Publish & Deploy ──► Azure Function App
Pull request   ──► Build & Test ──► (no deploy)
```

Two jobs run in sequence:

| Job | Triggers on | Purpose |
|---|---|---|
| `build-and-test` | Every push and every PR | Compile, run xUnit tests, publish test results |
| `deploy` | Push to `master` only, after `build-and-test` passes | `dotnet publish`, zip, deploy to Azure |

A pull request can never deploy. Deployment only happens when a reviewed change lands on `master`.

---

## Best practices applied

### 1. Path filters — only run when relevant files change

```yaml
paths:
  - 'src/LeiMonitor.Core/**'
  - 'src/LeiMonitor.Data/**'
  - 'src/LeiMonitor.Function/**'
  - 'tests/LeiMonitor.Core.Tests/**'
  - '.github/workflows/function-ci-cd.yml'
```

Changes to `docs/`, `k8s/`, or `src/LeiMonitor.Worker/` do not trigger this pipeline. This keeps CI fast and avoids consuming GitHub Actions minutes for irrelevant commits.

The workflow file itself is included in the path filter so that changes to the pipeline also trigger a run.

### 2. Two-job separation — build gate before deploy

`deploy` declares `needs: build-and-test`. The deploy job never starts if tests fail. This is the minimum viable quality gate: broken code cannot reach Azure.

### 3. Secrets — never hardcode credentials

All Azure credentials are stored as GitHub repository secrets, not in the workflow YAML. The pipeline uses five secrets:

| Secret name | Value |
|---|---|
| `AZURE_CLIENT_ID` | Client ID of the Azure AD app registration used for OIDC login |
| `AZURE_TENANT_ID` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `AZURE_RESOURCE_GROUP` | `rg-lei-monitor-dev` |
| `AZURE_FUNCTION_APP_NAME` | `func-lei-monitor-dev` |

`AZURE_RESOURCE_GROUP` and `AZURE_FUNCTION_APP_NAME` are stored as secrets (rather than plain env vars) so the resource names are not publicly visible in the workflow file on a public repository.

### 4. OIDC login — no stored credentials

The pipeline uses `azure/login@v2` with `client-id`, `tenant-id`, and `subscription-id` rather than a `creds` JSON secret. This is the recommended approach:

- No long-lived secret that can be leaked or expire unexpectedly
- Azure AD issues a short-lived token for each workflow run via OpenID Connect (OIDC)
- The token is automatically scoped to the workflow run and discarded afterwards

### 5. GitHub Environments — deployment protection

The `deploy` job declares `environment: dev`. GitHub Environments let you add:

- **Required reviewers** — a human must approve before the deploy runs
- **Wait timer** — introduce a delay before deployment
- **Deployment history** — a full log of what was deployed and when, visible in the GitHub UI

For a dev environment this is optional, but the scaffolding is in place so you can add protection rules later without changing the workflow.

### 6. `--no-self-contained` publish

```yaml
dotnet publish ... --no-self-contained
```

This produces a framework-dependent binary. The Azure Functions runtime on the host already has .NET 9 installed. A self-contained publish would bundle the runtime unnecessarily, making the zip larger and the deployment slower.

### 7. Test results published as workflow annotations

```yaml
- uses: dorny/test-reporter@v1
  if: always()
```

`if: always()` ensures test results are published even when tests fail, so you can read the failure details directly in the GitHub PR or commit view without downloading a log file. Without this, a test failure just shows a red job with no context.

---

## One-time setup: wire up OIDC in Azure

Run these commands once from a terminal (requires Azure CLI and Owner or Contributor + User Access Administrator on the subscription).

```powershell
$subscriptionId = "<YOUR_SUBSCRIPTION_ID>"
$resourceGroup  = "rg-lei-monitor-dev"
$appName        = "sp-lei-monitor-github"
$githubOrg      = "<YOUR_GITHUB_ORG_OR_USERNAME>"
$githubRepo     = "LeiMonitor"

# 1. Create an app registration
$appId = az ad app create --display-name $appName --query appId -o tsv
az ad sp create --id $appId

# 2. Grant Contributor on the resource group only (least privilege)
az role assignment create `
  --assignee $appId `
  --role Contributor `
  --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup"

# 3. Add federated credentials for OIDC — one for push to master, one for PRs
$objectId = az ad app show --id $appId --query id -o tsv

az ad app federated-credential create --id $objectId --parameters '{
  "name": "github-master",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:'"$githubOrg/$githubRepo"':ref:refs/heads/master",
  "audiences": ["api://AzureADTokenExchange"]
}'

az ad app federated-credential create --id $objectId --parameters '{
  "name": "github-pr",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:'"$githubOrg/$githubRepo"':pull_request",
  "audiences": ["api://AzureADTokenExchange"]
}'

# 4. Print the values you need for GitHub secrets
$tenantId = az account show --query tenantId -o tsv
Write-Host "AZURE_CLIENT_ID:       $appId"
Write-Host "AZURE_TENANT_ID:       $tenantId"
Write-Host "AZURE_SUBSCRIPTION_ID: $subscriptionId"
```

---

## One-time setup: add secrets to GitHub

1. Go to your repository on GitHub.
2. **Settings** → **Secrets and variables** → **Actions** → **New repository secret**.
3. Add each secret from the table above.

For `AZURE_RESOURCE_GROUP` and `AZURE_FUNCTION_APP_NAME`:

4. Still in **Secrets and variables**, click the **Variables** tab.
   > These can be plain **Variables** rather than secrets if your repository is private. Use secrets if the repository is public.
5. Add `AZURE_RESOURCE_GROUP` = `rg-lei-monitor-dev` and `AZURE_FUNCTION_APP_NAME` = `func-lei-monitor-dev`.

   If you prefer to keep them as secrets (as the workflow currently has them), add them under **Secrets** instead.

---

## Enabling OIDC on the GitHub Actions workflow

GitHub requires you to explicitly grant the workflow permission to request OIDC tokens. The workflow already includes this implicitly through `azure/login@v2`, but your repository must have **Actions permissions** set correctly:

1. **Settings** → **Actions** → **General** → **Workflow permissions**
2. Select **Read and write permissions**
3. Check **Allow GitHub Actions to create and approve pull requests** (optional, not required for this pipeline)

---

## What the pipeline does NOT do (and why)

| Concern | Decision |
|---|---|
| Multi-environment (staging, prod) | Out of scope for a PoC. Add a second `deploy` job with `environment: prod` and required reviewers when ready. |
| Container image build | Not applicable — `LeiMonitor.Function` is zip-deployed, not containerised. See `Dockerfile` and `k8s/` for the Worker/AKS path. |
| Integration or database tests | No test database is available in CI. The xUnit tests cover business logic via mocks only (`LeiExpiryCheckerTests`). |
| NuGet package caching | Low priority at this project size. Add `actions/cache` on the NuGet global cache directory if restore times become material. |
| Dependabot | Recommended. Add a `dependabot.yml` to keep `Microsoft.Azure.Functions.Worker` and other packages current automatically. |
