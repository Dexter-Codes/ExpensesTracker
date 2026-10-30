# Docker exercise

Use the project directory containing `Dockerfile` and `Expenses.slnx`. Docker Desktop must be running. These local commands do not access Azure or send real emails.

## Build an image

```sh
docker build --platform linux/amd64 -t expenses-web:dev .
```

The first stage uses the .NET SDK to restore and compile. The final stage contains the smaller ASP.NET runtime and published application, runs as the non-root `app` user and listens on port 8080. `.dockerignore` excludes local databases, build outputs, local settings and key files. Never pass real signing keys or OAuth secrets as Docker build arguments.

`linux/amd64` matches the Azure Linux hosting exercise. On an Apple Silicon Mac, the SDK stage compiles natively and publishes for the target architecture. Docker Desktop emulates the final amd64 container locally. An image built only for arm64 should not be used on an amd64 Azure host.

## Initialize a separate local database

```sh
docker run --rm --platform linux/amd64 \
  --mount type=volume,source=expenses-docker-data,target=/data \
  expenses-web:dev --init-db
```

The image defaults to SQLite at `/data/expenses.db`. This volume is separate from your source checkout's SQLite database and your Azure SQL database. Initialization is explicit and does not upgrade an existing schema. The writable directory belongs to the non-root application user; an empty Docker named volume inherits that directory's initial permissions.

## Run a container

```sh
docker run -d --name expenses-docker --platform linux/amd64 \
  -p 127.0.0.1:5280:8080 \
  --mount type=volume,source=expenses-docker-data,target=/data \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e App__BaseUrl=http://localhost:5280 \
  expenses-web:dev
```

Open `http://localhost:5280` and `http://localhost:5280/health`. The port mapping connects host port 5280 to container port 8080 and binds it only to the local machine. This uses Development only for the isolated local exercise; Azure must use Production.

By default this local container has no Azure queue credentials, no JWT signing key and no external-login secrets. Registration writes confirmation messages to the local database but does not send real email. The development inbox intentionally checks the request's loopback address; Docker bridge requests from the host may not qualify. Do not weaken the inbox guard or enable Development in Azure to work around that. Authenticated container smoke checks can retrieve a local confirmation page from container loopback. Azure deployment uses the already verified real email pipeline instead.

The `/data` volume stores both SQLite data and data-protection keys at `/data/keys`, so users and login encryption keys survive container recreation. The image itself does not store your runtime data.

Local verification completed with the linux/amd64 image: non-root UID 1654, healthy HTTP response, isolated registration and confirmation, cookie sign-in, CSRF-protected group creation, and persistence of the group and authenticated session after a container restart. The check used a synthetic account in the Docker-only volume and did not send real email. These HTTP checks do not establish visual browser validation or Azure container compatibility.

## Inspect, stop and restart

```sh
docker ps --filter name=expenses-docker
docker logs --tail 30 expenses-docker
docker exec expenses-docker id
docker stop expenses-docker
docker start expenses-docker
```

`docker exec ... id` should show a non-zero UID. Restart and verify the app still responds. To replace a container after changing code, rebuild the image, stop and remove only the old container, then repeat the run command with the same volume. The data survives because it lives in the volume. Reusing an image tag does not update an already running container.

## Azure Container Registry and hosting

Configure Azure yourself, one step at a time:

1. Create an Azure Container Registry in the lab resource group, initially using the Basic SKU if supported. Keep the registry admin account disabled.
2. Authenticate your local Azure CLI to the intended subscription, then use `az acr login --name YOUR_REGISTRY`.
3. Tag the tested image as `YOUR_LOGIN_SERVER/expenses-web:v1` and push it. Use the registry's actual login server from its Overview; do not infer it from a naming pattern.
4. Give the intended container host's managed identity the appropriate registry image-pull role. Keep deployment identity permissions separate from runtime permissions.
5. Configure a container-based App Service exercise with image port 8080. Preserve the working code-based app while configuring the new host. Configure SQL identity/firewall access, queue sender access, Key Vault references and the public app URL for that host. Do not use Development or the local SQLite settings in Azure.
6. Configure persisted data-protection keys on writable persistent storage for the container user. The existing code app's `/home` settings and filesystem permissions must be verified for the new host; custom-container storage is not automatically identical.
7. Add the new hostname's OAuth redirect URIs if testing external login there. Test account, group, expense and email behaviour before changing any gateway backend.

Registry images are versioned deployments; rebuilding the source and pushing a new tag is separate from starting a container with that tag. Registry publishing and Azure container hosting have not been verified merely by a successful local Docker build.

For this lab, the user has now verified an ACR push and a working Azure container deployment:

| Resource | Value |
|---|---|
| Registry | `expensesidgafacr` |
| Login server | `expensesidgafacr-bzgscdb7heazaxc8.azurecr.io` |
| Image | `expenses-web:v1` |
| Container web app | `expensestracker-container-idgaf` |
| Main container port | `8080` |
| Image-pull identity | User-assigned `expenses-container-pull`, with AcrPull on the registry |
| Runtime identity | The container web app's system-assigned identity |
| Runtime services | Existing Azure SQL database, queue and Key Vault |

The user confirmed sign-in, shared group/expense data in both web apps, Gmail notifications and API tokens on the container app. The new hostname's Microsoft external login configuration is intentionally deferred. Outlook delivery is also deferred; Gmail delivery is verified. No claim is made that an APIM policy applies automatically to direct container-app requests.

## Container release workflow

The manual **Deploy container to Azure** workflow tests the source, authenticates using the existing GitHub OIDC identity, builds linux/amd64, pushes a commit-SHA-tagged image and updates the existing sidecar-enabled App Service `main` container. It preserves the configured image-pull identity and runtime settings. It does not run on pushes and does not provision resources or initialize SQL.

Configure these permissions yourself for **expenses-github-deploy**:

- **AcrPush** on `expensesidgafacr` to upload images (this registry uses RBAC Registry Permissions).
- **Reader** on that registry so Azure CLI can resolve its actual login server, including its domain-name-label suffix.
- **Website Contributor** on `expensestracker-container-idgaf` to update its main container and restart it.

Keep the runtime pull identity at **AcrPull**; it does not need push access. Keep GitHub's existing web/Function role assignments.

Add three variables to the existing GitHub environment **azure**:

| Variable | Value |
|---|---|
| `AZURE_ACR_NAME` | `expensesidgafacr` |
| `AZURE_RESOURCE_GROUP` | `expenses-lab` |
| `AZURE_CONTAINER_WEB_APP_NAME` | `expensestracker-container-idgaf` |

Commit and push the Docker/workflow updates yourself after review, wait for CI, then manually run the new workflow from `main`. The workflow still needs its first GitHub/Azure verification run. A configured image tag or successful restart is not proof of application readiness: verify health, sign-in, group access, an intended expense change and notification delivery afterward.

Each image uses the source commit SHA as its tag rather than overwriting `v1` or `latest`. For a rollback exercise, select a previously working image tag in Deployment Center and verify the app again. There is no automatic rollback in this lab.

References: [.NET Docker images, ports and users](https://learn.microsoft.com/en-us/dotnet/core/docker/), [Docker multi-platform builds](https://docs.docker.com/build/building/multi-platform/), [App Service container update](https://learn.microsoft.com/en-us/cli/azure/webapp/sitecontainers#az-webapp-sitecontainers-update).
