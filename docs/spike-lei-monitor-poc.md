Spike: LEI Expiry Notification Function – PoC Validation and Hosting Decision

Epic: ISO 20022 LEI Compliance Monitoring
Type: Spike
Target: 1 sprint (time-boxed)


Background

As part of our ISO 20022 compliance obligations, Shawbrook colleagues must be made aware when stored LEI records are approaching or have passed their renewal or expiry date so they can act before POP2 enrichment or payment-file generation attempts to use stale or lapsed data.

The proposed approach is a scheduled function that runs on a configured cadence, queries CustomerStorage for active LEI records where the renewal or expiry date is within an agreed threshold, and raises a notification for colleague review. This is a notification control only and is not a payment-blocking mechanism. Wider payment blocking and warning rules, the Customer Service API, the PEGA colleague journey, and POP2 enrichment logic are covered by the HLD and TD pages and are out of scope here.

The right deployment target is an open question. The team's default has been a managed Azure Function, but the platform is migrating toward AKS with the audit service as the first workload. This spike must produce a clear hosting model recommendation before implementation begins.


Key Questions

- At what point within the process can LEI data quality issues be detected, and what query against CustomerStorage reliably identifies records that are expired, expiring, or otherwise unusable?
- Is the LeiStatus computed column in dbo.CustomerLeiCode reliable as a detection signal, and what are its edge cases?
- Who are the notification recipients, what channel should be used (email, ServiceNow, Teams), and should alerts fire repeatedly while an LEI remains unactioned?
- What information should be included in a notification to give colleagues enough context to act?
- What is the right hosting model: managed Azure Function, Azure Container Apps Job, or AKS CronJob?
- Is a shared AKS cluster already planned or available, and does co-locating this workload there provide a concrete benefit such as shared networking, secrets, or operational alignment with the audit service?
- Is there an existing VNet and private endpoint route from the chosen hosting option to CustomerStorage?
- Is there an Azure Container Registry available if a container-based option is recommended?
- What observability and alerting on the function itself is required beyond basic logging?


Hosting Options Under Consideration

Option A – Managed Azure Function

The scheduled logic runs as an Azure Functions v4 timer trigger on either a Consumption or Premium plan. The function is zip-deployed from a CI/CD pipeline and the runtime, patching, and scaling are fully managed by the platform. Configuration is supplied via Application Settings and Connection Strings in the Function App, with optional Key Vault references. Application Insights is natively integrated with no additional setup. Networking to CustomerStorage can be achieved via VNet Integration on a Premium plan or via an App Service Environment; Consumption plan requires an additional VNet Integration add-on. There is no container image to build, scan, or store. Deployment is well understood by the team and fits existing Azure CLI or Bicep pipelines. The main trade-off is that it is not portable to AKS without a rewrite, and cold-start on the Consumption plan could cause a delay at the trigger window, though this is unlikely to be material for a daily notification job.

Option B – Azure Container Apps Job

The scheduled logic is packaged as a containerised console application with the function entrypoint calling the checker directly and exiting on completion. The job is scheduled via an ACA Job CRON expression, and the ACA environment manages the container lifecycle, scaling to zero between runs. The container image is built and pushed to an Azure Container Registry as part of the CI/CD pipeline. Configuration is injected as environment variables, which means the application must read all settings from environment variables or mounted secrets rather than a local settings file. ACA natively integrates with Application Insights via the OTEL collector or direct SDK. Networking to CustomerStorage is handled at the ACA environment level via VNet injection, which must be pre-provisioned. There is no AKS cluster to own or operate, but there is an ACR dependency and a new ACA environment to provision. This option provides most of the container portability benefit of Option C without the cluster ownership overhead, making it a pragmatic middle ground if the team anticipates moving to AKS later but does not have a shared cluster ready now.

Option C – AKS CronJob

The same container image built for Option B is deployed as a Kubernetes CronJob resource on a shared AKS cluster. Scheduling is defined in the CronJob manifest using a standard cron expression. The cluster provides secrets management via Kubernetes Secrets or integration with Azure Key Vault via the Secrets Store CSI driver. Observability requires explicit configuration, either via the Azure Monitor agent deployed to the cluster or by instrumenting the application with an OTEL SDK and pushing to an Application Insights or Prometheus endpoint. Networking to CustomerStorage is handled at the cluster level via private endpoint or VNet peering configured by the cluster operator. The main advantage of this option is operational alignment: if the audit service and other workloads are already running on the cluster, there is a shared ops runbook, shared ingress, shared secret store, and potentially shared service mesh configuration. The cost of adding a CronJob to an existing cluster is low. However, if the cluster does not yet exist, this option carries significant lead time and infrastructure overhead for a single notification job, and cluster lifecycle ownership must be assigned to a team. This option is only recommended if a shared cluster is confirmed available and co-location provides a concrete, named benefit.


Out of Scope

- Production secrets or Key Vault integration
- Real notification provider implementation
- Alert de-duplication or snooze logic
- Multi-environment CI/CD pipeline
- Performance or load testing of the SQL query


Risks and Assumptions

- The dbo.CustomerLeiCode table structure and the definition of the LeiStatus computed column must be confirmed with the DBA before detection logic can be finalised.
- The LeiStatus computed column may not be persisted, which could affect how it is read at the application layer. This must be validated early.
- Notification channel and recipient requirements are currently undefined and must be agreed before implementation begins.
- A shared AKS cluster may not yet exist, which would make Option C high effort relative to its benefit at this stage. Option B provides most of the container portability benefit without that dependency.
- An Azure Container Registry may not be in place. This must be confirmed before recommending Option B or C.
- A VNet and private endpoint route to CustomerStorage may not be available to all hosting options. This is a high-impact constraint and must be confirmed before committing to a hosting model.
- Azure subscription access for non-prod deployment testing may need to be requested in parallel with the investigation.


Acceptance Criteria

- Potential detection points for LEI data quality issues within CustomerStorage are identified and documented.
- The dbo.CustomerLeiCode schema is confirmed, the LeiStatus computed column behaviour is validated, and any gaps or edge cases are noted.
- Available notification channels and their technical constraints are documented, with a recommended approach for recipient targeting and alert content.
- A hosting model recommendation is documented covering which option is recommended (A, B, or C), the rationale, the trade-offs considered, and any infrastructure pre-requisites such as VNet, ACR, or AKS cluster readiness.
- Technical constraints, dependencies, assumptions, and risks are captured.
- Findings and recommendations are documented and linked to this Jira ticket.
