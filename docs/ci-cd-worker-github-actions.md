# CI/CD for LeiMonitor.Worker — GitHub Actions Guide

**What this covers:** How the GitHub Actions pipeline in `.github/workflows/worker-ci-cd.yml` is structured, why each decision was made, and how to wire up the required secrets and permissions in GitHub and Azure.

---

## Pipeline overview

```
Push to master ──► Build (docker build) ──► (if green) ──► Push to ACR ──► kubectl set image ──► AKS CronJob
workflow_dispatch ──────────────────────────────────────► Push to ACR ──► kubectl set image ──► AKS CronJob
```

Two jobs run in sequence:

| Job | Triggers on | Purpose |
|---|---|---|
| `build` | Every push matching the path filter | `docker build` only — validates the image compiles |
| `deploy` | Push to `master` or `workflow_dispatch` only, after `build` passes | Build again, tag with SHA and `latest`, push to ACR, update the running CronJob on AKS |

A push that only touches docs or the Function project does not trigger this pipeline.

---

## Best practices applied

### 1. Path filters — only run when relevant files change

```yaml
paths:
  - 'src/LeiMonitor.Core/**'
  - 'src/LeiMonitor.Data/**'
  - 'src/LeiMonitor.Worker/**'
  - 'Dockerfile'
  - '.github/workflows/worker-ci-cd.yml'
```

Changes to `docs/`, `k8s/`, or `src/LeiMonitor.Function/` do not trigger this pipeline. The workflow file itself is included so that changes to the pipeline also trigger a run.

### 2. Two-job separation — build gate before deploy

`deploy` declares `needs: build`. The deploy job never starts if the image fails to build. Broken code cannot reach the cluster.

### 3. OIDC login — no stored credentials

The pipeline uses `azure/login@v2` with `client-id`, `tenant-id`, and `subscription-id` rather than a long-lived `creds` JSON secret. Azure AD issues a short-lived OIDC token scoped to the workflow run. The same three secrets already configured for the Function pipeline (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`) are reused here — no new secrets are needed.

### 4. Non-sensitive config as env vars, not secrets

```yaml
env:
  ACR_NAME:    acrleimonitodev
  AKS_CLUSTER: aks-lei-monitor-dev
  AKS_RG:      rg-lei-monitor-dev
  IMAGE_NAME:  lei-monitor
```

The ACR name, cluster name, and resource group are not credentials. Storing them as plain `env` vars makes the workflow self-documenting without exposing anything sensitive. Only the three OIDC values are secrets.

### 5. Dual image tags — SHA and `latest`

```yaml
docker build \
  --tag acrleimonitodev.azurecr.io/lei-monitor:${{ github.sha }} \
  --tag acrleimonitodev.azurecr.io/lei-monitor:latest \
  .
```

The SHA tag is immutable and traceable to an exact commit. The `latest` tag is a convenience alias for manual pulls. `kubectl set image` uses the SHA tag so each deployment is pinned to a specific build.

### 6. `kubectl set image` — zero-downtime update

```yaml
kubectl set image cronjob/lei-expiry-check \
  lei-monitor=acrleimonitodev.azurecr.io/lei-monitor:${{ github.sha }} \
  --namespace lei-monitor
```

This patches only the image field of the existing CronJob. No manifest re-apply is needed, and the `k8s/cronjob.yaml` in the repository does not need to store the current SHA.

---

## One-time setup: wire up OIDC in Azure

The `sp-lei-monitor-github` app registration already has federated credentials for the Function pipeline. The worker pipeline uses the same OIDC token, so **no new federated credentials are needed**. Only two additional role assignments are required (see `docs/deploy-worker-to-aks-portal.md` Step 4).

---

## One-time setup: no new GitHub secrets needed

The three OIDC secrets are already present from the Function pipeline setup:

| Secret name | Already set? |
|---|---|
| `AZURE_CLIENT_ID` | ✅ yes |
| `AZURE_TENANT_ID` | ✅ yes |
| `AZURE_SUBSCRIPTION_ID` | ✅ yes |

No additional secrets are required for the worker pipeline.

---

## What the pipeline does NOT do (and why)

| Concern | Decision |
|---|---|
| Unit tests | `LeiMonitor.Worker` is a thin host entry point with no testable logic of its own. Business logic tests live in `LeiMonitor.Core.Tests` and are covered by the Function pipeline. Add a test job here if worker-specific tests are added later. |
| Multi-environment (staging, prod) | Out of scope. Add a second `deploy` job with `environment: prod` and required reviewers when ready. |
| Kubernetes manifest re-apply on every deploy | Only the image is updated via `kubectl set image`. Full manifest re-applies (namespace, secret, configmap) are one-time operations done manually — see `docs/deploy-worker-to-aks-portal.md`. |
| NuGet restore / .NET build | Not needed — the Worker is built entirely inside the `Dockerfile` using the SDK image. The runner does not need .NET installed. |
| Image vulnerability scanning | Recommended for production. Azure Defender for Containers can scan ACR images automatically without changes to this workflow. |
