# Day 5 Task 3: Azure Container Apps Fundamentals

## Task Objective
Deploy the QuotesApi container image (built in [Day 5 Task 2](../task%20-%202/) via `dotnet publish .../t:PublishContainer`, no Dockerfile) to Azure Container Apps, and demonstrate the platform's core concepts: environments, external ingress, target port routing, autoscaling, and revisions.

## Resources Created

All resources live in the `Azure for Students` subscription, `centralindia` region, and are separate from the Day 4 monitoring resource group (`rg-quotesapi-monitoring`), which was not modified.

| Resource | Name | Purpose |
|---|---|---|
| Resource group | `thinkschool-rg` | Container for all Task 3 resources |
| Container Registry | `thinkschoolacr` (Basic SKU, admin disabled) | Hosts the `quotes-api` image since Container Apps cannot pull from a local Docker daemon |
| Container Apps environment | `thinkschool-env` | Shared networking/logging boundary for container apps |
| Container App | `quotes-api` | The deployed QuotesApi instance |

## Why a Container Registry Was Needed
The image `quotes-api:0.1.0` built in Task 2 only existed in the local Docker daemon. Azure Container Apps pulls images over the network, so the image was tagged and pushed to `thinkschoolacr.azurecr.io/quotes-api:0.1.0`. Docker Hub was intentionally avoided per the task's registry preference.

## Authentication: Registry → Container App
The ACR was created with the admin user **disabled**. The container app instead uses a **system-assigned managed identity** granted the `AcrPull` role on the registry (`--registry-identity system`). No registry credentials were ever stored in a file, an environment variable baked into the image, or Git.

## Ingress and Port
```
az containerapp create -n quotes-api -g thinkschool-rg \
  --environment thinkschool-env \
  --image thinkschoolacr.azurecr.io/quotes-api:0.1.0 \
  --target-port 8080 --ingress external \
  --registry-server thinkschoolacr.azurecr.io --registry-identity system \
  --system-assigned \
  --min-replicas 1 --max-replicas 3 \
  --secrets jwt-key=<runtime-only-test-value> \
  --env-vars Jwt__Key=secretref:jwt-key
```
`Jwt__Key` is supplied as an Azure Container Apps **secret**, referenced via `secretref:` — the same runtime-only pattern used for the local `docker run -e` verification in Task 2. It is never baked into the image.

Public URL: `https://quotes-api.victoriousbay-dc87b4fa.centralindia.azurecontainerapps.io`

## Scaling
```
az containerapp update -n quotes-api -g thinkschool-rg \
  --scale-rule-name http-concurrency-rule \
  --scale-rule-type http \
  --scale-rule-http-concurrency 20 \
  --min-replicas 1 --max-replicas 3
```
Resulting scale configuration:
```json
{
  "cooldownPeriod": 300,
  "maxReplicas": 3,
  "minReplicas": 1,
  "pollingInterval": 30,
  "rules": [
    {
      "http": { "metadata": { "concurrentRequests": "20" } },
      "name": "http-concurrency-rule"
    }
  ]
}
```

## Revisions (Blue-Green / Canary)
The app was switched to `--revisions-mode multiple`, and a second revision (`quotes-api--v2`) was created via `--revision-suffix v2`. Traffic was then split 70/30 between the two revisions using `az containerapp ingress traffic set`, demonstrating that multiple immutable revisions can coexist and share traffic:

```
CreatedTime                Active    Replicas    TrafficWeight    HealthState    ProvisioningState    Name
-------------------------  --------  ----------  ---------------  -------------  -------------------  -------------------
2026-08-14T06:51:49+00:00  True      1           70               Healthy        Provisioned          quotes-api--0000002
2026-08-14T06:53:36+00:00  True      1           30               Healthy        Provisioned          quotes-api--v2
```

## Verification
```
$ curl https://quotes-api.victoriousbay-dc87b4fa.centralindia.azurecontainerapps.io/health
{"status":"healthy"}

$ curl https://quotes-api.victoriousbay-dc87b4fa.centralindia.azurecontainerapps.io/api/quotes
{"page":1,"size":10,"totalCount":0,"items":[]}
```

Both endpoints returned HTTP 200 over the public FQDN, confirming the deployed container is the real QuotesApi application (migrated SQLite database, JWT-authenticated endpoints, `/health` check) — not a stub.

## Notes / Deviations
- **CLI verified against the installed version** (Azure CLI `2.89.0`): the task's `--scale-rule` is not a single flag in this version — it is split into `--scale-rule-name`, `--scale-rule-type`, and `--scale-rule-http-concurrency`, which is what was actually used above.
- **Resource providers**: `Microsoft.App` and `Microsoft.ContainerRegistry` were not registered on the subscription and were registered as a one-time prerequisite (`az provider register --wait`) before any resource creation.
- **No Dockerfile** was created at any point; the image is the same one produced by .NET's built-in container publishing in Task 2.
- Portal screenshots (Container App overview, Revisions blade, Scale blade, Registry repository view, `/health` in a browser) were not captured in this session — the working environment has no browser/screen-capture tool available. The CLI/JSON evidence above is the equivalent proof of each; portal screenshots would need to be captured manually if required for a written submission.
