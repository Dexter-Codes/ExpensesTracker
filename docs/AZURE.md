# Guided Azure lab

You configure and deploy every Azure resource yourself. Nothing in this repository runs Terraform or accesses Azure merely by building, testing, or opening the app. The deployment workflow is manual-only. Use a dedicated resource group and keep the resource names you choose in your own notes.

## 0. Check the subscription first

In the portal, open **Subscriptions → your subscription → Overview / Credits / Cost Management**. Confirm the precise expiry time, spending-limit status, and whether services stop or become billable when your particular credit offer expires. Resource creation does not extend the credit's lifetime. Check each service's estimated price and availability before creation. Credit subscriptions can have SKU and quota restrictions. A cost budget is an alert, not a hard spending cap.

The minimum functional path is App Service + SQL + Storage Queue + Functions + Communication Services Email. Key Vault supplies the external-login secrets. APIM and AKS are subsequent exercises. If time is short, prioritize one working App Service deployment and its email flow.

## 1. App Service

Create a Web App with **Code**, **.NET 10**, **Linux**, in a supported region, using a small paid plan such as B1 for this lab (subject to your subscription's availability). Enable HTTPS Only and Always On. Enable the app's **system-assigned managed identity** under Identity and note its principal/object ID.

Under **Environment variables**, configure:

| Setting | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` (App Service's trusted front-end; do not use indiscriminately on a directly exposed host) |
| `App__BaseUrl` | Your actual public HTTPS URL |
| `Database__Provider` | `SqlServer` |
| `ConnectionStrings__DefaultConnection` | See SQL step below |
| `Notifications__QueueUri` | `https://YOUR-STORAGE.queue.core.windows.net/expense-emails` |
| `DataProtection__KeyPath` | `/home/expenses-keys` |

Keep Development mode off in Azure: the local development inbox contains private account-verification and invitation links. `/home` on the built-in App Service Linux runtime persists across restarts; it keeps login/verification encryption keys available. Treat that directory as sensitive. Replicas in this lab must share the same application name and key directory. Production deployments should also encrypt persisted data-protection keys with Key Vault.

**Checkpoint:** after deployment, `https://YOUR-APP.azurewebsites.net/health` should respond with `{"status":"healthy"}`. This endpoint tests the web process, not SQL or email connectivity.

Reference: [Deploy .NET to App Service](https://learn.microsoft.com/azure/app-service/quickstart-dotnetcore).

## 2. SQL Database and identity-based access

Create an Azure SQL logical server and an empty database named `expenses`. Pick a small available development SKU; do not assume a free allowance applies. Configure a Microsoft Entra administrator on the SQL server. Under Networking, allow the app's outbound addresses and your temporary administration address. The broad “Allow Azure services” switch is convenient for a short lab but is not equivalent to a private endpoint or a narrow firewall rule.

Sign in to the database as its Entra administrator using a SQL client. Substitute your actual web-app managed identity's display name:

```sql
CREATE USER [YOUR-WEB-APP-NAME] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [YOUR-WEB-APP-NAME];
ALTER ROLE db_datawriter ADD MEMBER [YOUR-WEB-APP-NAME];
-- Temporary permission for the one-time schema initialization:
ALTER ROLE db_ddladmin ADD MEMBER [YOUR-WEB-APP-NAME];
```

Set the web app's connection setting (no password):

```text
ConnectionStrings__DefaultConnection = Server=tcp:YOUR-SERVER.database.windows.net,1433;Database=expenses;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False
```

After deploying the web code, use **App Service → Development Tools → SSH**. Find the published application directory (normally `/home/site/wwwroot`) and explicitly run:

```sh
dotnet Expenses.Web.dll --init-db
```

This creates the initial schema in a fresh database. It does not upgrade an existing schema. Check the database tables in your SQL client, then remove the temporary DDL role:

```sql
ALTER ROLE db_ddladmin DROP MEMBER [YOUR-WEB-APP-NAME];
```

**Checkpoint:** registration can create a user and an email-outbox row. An authorization/login error usually means identity permissions; a connection timeout usually means networking. Do not print access tokens or connection secrets while troubleshooting.

Reference: [Connect App Service to Azure SQL with managed identity](https://learn.microsoft.com/en-us/azure/app-service/tutorial-connect-msi-sql-database).

## 3. Storage Queue

Create a Standard general-purpose v2 Storage Account, LRS, HTTPS only. Create a queue named **`expense-emails`**. Give the web app identity the **Storage Queue Data Message Sender** role, scoped to this queue or the Storage Account. The application does not create the queue automatically.

Set `Notifications__QueueUri` from step 1. Leave `Notifications__StorageConnection` unset in Azure so the app uses managed identity. Save and restart the web app. Allow time for RBAC assignments to propagate.

**Checkpoint:** a new registration or invitation creates a queue message. The SQL `EmailNotifications.QueuedAt` field records successful queue publication, not delivery. Existing local-development emails are not copied into Azure because the databases are separate.

## 4. Key Vault and external sign-in

Create a Key Vault using Azure RBAC. Assign yourself an appropriate secret-management role and give the web app identity **Key Vault Secrets User** on the vault.

Register your own OAuth applications with Google and Microsoft. For Microsoft, choose supported account types appropriate to the users you want to invite. Configure the exact redirect URIs:

- Google: `https://YOUR-APP.azurewebsites.net/signin-google`
- Microsoft: `https://YOUR-APP.azurewebsites.net/signin-microsoft`

For local OAuth testing, add the corresponding `http://localhost:5080/...` redirect only if that provider permits it. For Google, configure the consent screen and add test users while the OAuth application is in testing mode.

Store the client secrets in Key Vault as `google-client-secret` and `microsoft-client-secret`. Set these web app environment variables:

| Setting | Value |
|---|---|
| `Authentication__Google__ClientId` | Google client ID |
| `Authentication__Google__ClientSecret` | `@Microsoft.KeyVault(SecretUri=https://YOUR-VAULT.vault.azure.net/secrets/google-client-secret)` |
| `Authentication__Microsoft__ClientId` | Microsoft application/client ID |
| `Authentication__Microsoft__ClientSecret` | `@Microsoft.KeyVault(SecretUri=https://YOUR-VAULT.vault.azure.net/secrets/microsoft-client-secret)` |

Client IDs are public identifiers; client secrets are private. Do not store normal URLs or display names in Key Vault just to demonstrate it. Check the portal's reference-resolution status and restart the app after configuring the providers.

**Checkpoint:** Google and Microsoft buttons appear on the sign-in page. A new account still needs email confirmation. If an email/password account already exists, sign into that account and use **Manage account → External logins** to associate a provider; accounts are not silently merged by email.

Reference: [App Service Key Vault references](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references).

## 5. Azure Function and email provider

Create a Function App using **.NET 10 isolated** and a plan your subscription supports. This lab uses **Windows Consumption (legacy)** because the credit subscription rejected Flex Consumption. Linux Flex Consumption is another option where supported; classic Linux Consumption does not support .NET 10. Let the portal configure its host/deployment storage; that storage is separate from the application's `EmailQueue` connection. Enable the Function App's system-assigned identity.

Give the Function identity **Storage Queue Data Contributor** on the email Storage Account for this lab (covers receiving/deleting messages and writing poison messages). Configure:

| Setting | Value |
|---|---|
| `EmailQueue__queueServiceUri` | `https://YOUR-STORAGE.queue.core.windows.net` |
| `Email__DryRun` | `true` initially |
| `Email__Endpoint` | Communication Services endpoint, after setup below |
| `Email__Sender` | Verified sender address from the linked email domain |

Do not set `EmailQueue` to an unrelated host-storage connection string. The prefix `EmailQueue__...` matches the Function's queue binding. Keep portal-generated `AzureWebJobsStorage` and deployment-storage settings. If you switch host storage to identity-based access, follow its additional role requirements separately.

Create **Email Communication Services** and provision a supported Azure-managed email domain (or verify your own domain). Create **Azure Communication Services** and connect that email domain to it. Copy the Communication Services endpoint and the domain's MailFrom sender address into the Function settings.

For this short lab, the official managed-identity email sample supports **Contributor** on the specific Communication Services resource. Assign it only on that resource, not on the subscription/resource group. For a longer-lived deployment, replace it with a custom role containing the documented required communication-service permissions. Do not confuse an identity on the Communication Services resource with the Function's identity that calls it.

Deploy the Function project. Confirm `SendEmail` is listed. With `Email__DryRun=true`, trigger a normal app update and inspect the Function logs for its notification ID. Dry-run messages are consumed without sending, so create a new update after disabling dry-run.

When you are ready to send real mail, set `Email__DryRun=false` yourself. Register a user whose inbox you control, follow the verification email, create a group, invite a second user, and check the invitation and expense-update emails. Domain provisioning, regional availability and sending quotas can prevent immediate delivery; inspect provider status rather than assuming queue consumption means delivery. No email has been sent during local development.

**Checkpoint:** adding/editing/deleting an expense emails affected members except the actor. Recording a settlement emails its recipient. Observe retries for failures and the `expense-emails-poison` queue after repeated failures. Messages can be delivered more than once following a crash; this app does not promise exactly-once email.

References: [isolated Functions and supported plans](https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide), [identity-based queue connections](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-queue-trigger), [email managed-identity sample](https://learn.microsoft.com/en-us/samples/azure-samples/communication-services-dotnet-quickstarts/email-sample-send-email-with-managed-identity/).

## 6. Deploy code and practise CI/CD

Build the web project with `dotnet publish src/Expenses.Web -c Release -o artifacts/web`, then use your chosen App Service deployment method to deploy the contents of that directory. Publish Functions separately with `dotnet publish src/Expenses.Functions -c Release -o artifacts/functions`. Do not upload source directories, local settings, SQLite files or development inbox contents.

To use the supplied GitHub Actions files, create a GitHub repository yourself and push the source. The CI workflow builds, tests and packages both projects; it does not deploy. Before running the deploy workflow:

1. Create a GitHub environment named `azure` and configure any desired approval rule.
2. Configure Microsoft Entra workload federation for your repository and that environment. New GitHub.com repositories use an immutable subject including numeric IDs (`repo:OWNER@OWNER-ID/REPO@REPO-ID:environment:azure`); match the format emitted by your repository. See the verified lab values in [CICD.md](CICD.md). Grant the deployment identity permissions on the two target apps; do not grant subscription-wide access.
3. Add environment variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_WEB_APP_NAME`, and `AZURE_FUNCTION_APP_NAME`.
4. Run **Deploy manually to Azure** and choose `web` or `functions`. The workflow tests before deploying and does not initialize the database.
5. Change a visible label, repeat deployment, verify it, then practise redeploying an earlier commit. B1 has no deployment slots; use a suitable higher plan if you want a slot-swap exercise.

The Function workflow publishes locally in the runner and deploys to the existing app; it does not force a Flex Consumption SKU or enable a remote build. It is prepared for this lab's Windows Consumption app. The workflows still need their first verification run in GitHub and Azure. Follow [the CI/CD setup](CICD.md) one step at a time.

Reference: [GitHub Actions OIDC for Azure](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure).

## 7. Observe behaviour

Create Application Insights and set `APPLICATIONINSIGHTS_CONNECTION_STRING` on the web app and Function App. The web app registers telemetry when this setting exists. Enable the Function's Application Insights integration in the portal. Inspect requests, exceptions, dependencies, and Function invocation logs. Configure an alert for failures and monitor poison-queue growth. Avoid logging email bodies, account-verification links or access tokens.

The web dispatcher retries pending email rows; SQL stores the durable record. Inspect both the outbox and Function logs to distinguish “saved”, “queued” and “sent”. Keep Log Analytics retention and data collection modest for a learning subscription.

## 8. Docker, ACR, Terraform, AKS, APIM

After the first successful end-to-end deployment:

- Build and run the same web app using the root Dockerfile. Use ACR for a private image, then run it as an App Service container. Supply the same environment settings and managed identity permissions. App Service custom containers need persistent `/home` storage enabled (`WEBSITES_ENABLE_APP_SERVICE_STORAGE=true`). Keep database initialization explicit.
- Use `infra/README.md` for a deliberately small Terraform exercise. It is optional and never automatically applied. Do not create duplicate resources with the same names.
- Use `docs/API.md` to practise subscription keys, rate limiting and response policies in APIM. The browser UI continues calling its own API directly in this first version.
- Create an AKS Free-tier cluster yourself and configure ACR pulls, OIDC/workload identity, SQL and queue access, then adapt `infra/kubernetes/app.yaml`. Configure TLS ingress and its forwarded-header trust before exposing it. Configure Google/Microsoft redirect URIs for the new hostname. App Service Key Vault references are a platform feature: they do not automatically work on AKS. Mount OAuth secrets through the Key Vault CSI driver or configure an equivalent secret provider before testing external sign-in on AKS.
- Use `kubectl get pods`, `logs`, `describe`, `scale`, `rollout status`, and `rollout undo`. AKS Free tier still bills underlying nodes, disks and networking. Compare the operational work with App Service.

## 9. Cleanup

Export only the source and any data you intentionally want to retain. Delete the dedicated lab resource group when done, or inspect and destroy Terraform-managed resources through their state. Verify deletion in the portal. Also remove temporary OAuth registrations/secrets and deployment role assignments you no longer need. Key Vault soft-deleted names can remain reserved until retention/purge rules allow reuse. Resource deletion and credit expiry are different events; verify your subscription's actual state.
