# Deploy LeiMonitor.Worker to AKS — Portal Guide

**Audience:** Developer deploying for the first time using the Azure Portal and a terminal  
**Starting point:** `rg-lei-monitor-dev` resource group already exists (created for the Function)  
**What gets deployed:** `LeiMonitor.Worker` — a .NET 9 console app packaged as a Docker image, running as a Kubernetes CronJob on AKS

---

## Where to run each step

| Step | Run from |
|---|---|
| Steps 1–4 (portal config, ACR, AKS, roles) | Any browser — https://portal.azure.com |
| Step 5 (build and push the image) | **Docker VM** — needs Docker and Azure CLI |
| Step 6 (apply manifests) | **Azure Cloud Shell** — browser only, no tools needed locally |
| Step 7 (verify) | **Azure Cloud Shell** or the portal UI |

> **Nothing needs to be installed on this machine.** All portal steps use the browser. The image build runs on the Docker VM. `kubectl` is not needed anywhere — Cloud Shell has it pre-installed.

---

## Prerequisites

| Tool | Machine | Notes |
|---|---|---|
| Azure account | Browser | https://portal.azure.com |
| Azure CLI + Docker | Docker VM | Only for Step 5 |

---

## Step 1 — Resource group

No action needed. The existing `rg-lei-monitor-dev` resource group is used. The ACR and AKS cluster will be created in **North Europe** (quota is more generous there than UK South).

---

## Step 2 — Create an Azure Container Registry

1. In the portal search bar type **Container registries** and select it.
2. Click **+ Create**.
3. Fill in the **Basics** tab:
   - **Subscription:** your subscription
   - **Resource group:** `rg-lei-monitor-dev`
   - **Registry name:** `acrleimonitordev` *(must be globally unique — lowercase alphanumeric only)*
   - **Location:** `North Europe`
   - **Pricing plan:** **Basic**
4. Leave all other tabs at their defaults.
5. Click **Review + create** → **Create**.

---

## Step 3 — Create an AKS cluster

1. In the portal search bar type **Kubernetes services** and select it.
2. Click **+ Create** → **Create a Kubernetes cluster**.
3. Fill in the **Basics** tab:
   - **Subscription:** your subscription
   - **Resource group:** `rg-lei-monitor-dev`
   - **Cluster name:** `aks-lei-monitor-dev`
   - **Region:** `North Europe`
   - **Availability zones:** None *(cheapest — no zone redundancy needed for dev)*
   - Under **Node pools → agentpool:**
     - **Node size:** `Standard_D2s_v3` *(2 vCPU / 8 GB — recommended; most subscriptions have default quota for this family)*
     - **Node count:** `1`

   > **Quota errors:** AKS requires at least 2 vCPUs per node. New or trial subscriptions often start with 0 quota for certain VM families in a given region. If the portal shows a quota error, either pick a different size from the list below or request an increase.
   >
   > **Recommended sizes to try (in order):**
   > | Size | Family | vCPU | RAM |
   > |---|---|---|---|
   > | `Standard_D2s_v3` | standardDSv3Family | 2 | 8 GB |
   > | `Standard_D2as_v4` | standardDASv4Family | 2 | 8 GB |
   > | `Standard_D2s_v5` | standardDSv5Family | 2 | 8 GB |
   >
   > **To request a quota increase:** Portal → **Subscriptions** → your subscription → **Usage + quotas** → search the family name → click the pencil icon → set **New limit** to `2` → **Submit**. Approval is usually instant on pay-as-you-go subscriptions.
4. Go to the **Integrations** tab:
   - **Container registry:** select `acrleimonitordev`

   > **Why:** Selecting the ACR here automatically grants the cluster's managed identity the `AcrPull` role on the registry. No additional credentials are needed for image pulls.

5. Leave all other tabs at their defaults.
6. Click **Review + create** → **Create** *(takes approximately 5 minutes)*.

---

## Step 4 — Role assignments for `sp-lei-monitor-github`

The existing app registration used by the Function pipeline needs two additional roles so the GitHub Actions worker pipeline can push images and update the cluster.

### AcrPush on the ACR

1. In the portal go to **Container registries** → `acrleimonitordev`.
2. In the left menu click **Access control (IAM)**.
3. Click **+ Add** → **Add role assignment**.
4. On the **Role** tab: search for and select **AcrPush** → **Next**.
5. On the **Members** tab: click **+ Select members** → search `sp-lei-monitor-github` → **Select**.
6. Click **Review + assign** → **Review + assign**.

### Azure Kubernetes Service Cluster User Role on the AKS cluster

1. In the portal go to **Kubernetes services** → `aks-lei-monitor-dev`.
2. In the left menu click **Access control (IAM)**.
3. Click **+ Add** → **Add role assignment**.
4. On the **Role** tab: search for and select **Azure Kubernetes Service Cluster User Role** → **Next**.
5. On the **Members** tab: click **+ Select members** → search `sp-lei-monitor-github` → **Select**.
6. Click **Review + assign** → **Review + assign**.

---

## Step 5 — Build and push the Docker image *(run on the Docker VM)*

Switch to your Docker VM. Clone or pull the repo there if you haven't already:

```powershell
git clone https://github.com/KalinPenchev-sb/LeiMonitor.git
cd LeiMonitor
```

Then build and push:

```powershell
az login
az acr build --registry acrleimonitordev --image lei-monitor:latest .
```

`az acr build` sends the source to Azure and builds it there — no local Docker daemon required. If Docker is already running on that VM you can use it instead, but `az acr build` works either way.

Verify the image arrived:

```powershell
az acr repository show-tags --name acrleimonitordev --repository lei-monitor --output table
```

---

## Step 6 — Apply the Kubernetes manifests via Cloud Shell

`kubectl` does not need to be installed locally. Use **Azure Cloud Shell** which has both `az` and `kubectl` pre-installed.

### Open Cloud Shell

1. In the portal click the **Cloud Shell** icon in the top bar (looks like `>_`).
2. Choose **Bash** when prompted.
3. If this is your first time, accept the storage account prompt.

### Fill in the real connection string

Before uploading `k8s/secret.yaml`, open the file locally and replace `<SQL_SERVER_CONNECTION_STRING>` with your real value. Do **not** commit this change — revert the file with `git checkout k8s/secret.yaml` immediately after this step.

### Upload the manifest files

1. In Cloud Shell click the **Upload/Download files** button (the document icon in the toolbar).
2. Upload all five files one by one: `k8s/namespace.yaml`, `k8s/serviceaccount.yaml`, `k8s/secret.yaml`, `k8s/configmap.yaml`, `k8s/cronjob.yaml`.
3. Uploaded files land in `~/` (your Cloud Shell home directory).

### Connect kubectl and apply

```bash
az aks get-credentials --resource-group rg-lei-monitor-dev --name aks-lei-monitor-dev --overwrite-existing

kubectl apply -f ~/namespace.yaml
kubectl apply -f ~/serviceaccount.yaml
kubectl apply -f ~/secret.yaml
kubectl apply -f ~/configmap.yaml
kubectl apply -f ~/cronjob.yaml
```

### Revert the secret file immediately after

Back on this machine, run:

```powershell
git checkout k8s/secret.yaml
```

---

## Step 7 — Verify the CronJob

Run all commands below in **Azure Cloud Shell** (the same session from Step 6, or open a new one).

### Confirm the CronJob is registered

```bash
kubectl get cronjob -n lei-monitor
```

Expected output:

```
NAME               SCHEDULE    SUSPEND   ACTIVE   LAST SCHEDULE   AGE
lei-expiry-check   0 8 * * *   False     0        <none>          Xs
```

### Trigger a manual run immediately

```bash
kubectl create job lei-expiry-manual --from=cronjob/lei-expiry-check -n lei-monitor
```

### Watch the pod logs

```bash
kubectl logs -l job-name=lei-expiry-manual -n lei-monitor --follow
```

You should see one of:
- `LEI expiry check completed — no issues found.`
- `LEI expiry check found N issue(s). Sending alert.`

### Check the pod exit code

```bash
kubectl get pods -n lei-monitor -l job-name=lei-expiry-manual
```

The pod status should show `Completed` (exit code 0) on success, or `Error` (exit code 1) on an unhandled exception.

> **Alternatively — view pods in the portal:** Portal → **Kubernetes services** → `aks-lei-monitor-dev` → **Workloads** → **Pods** → filter namespace `lei-monitor`. Click a pod to see its logs without any terminal.
