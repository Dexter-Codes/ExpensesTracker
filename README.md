# Expenses

A small multi-user expense-sharing web app, built to learn Azure with a real use case.

- Groups, email invitations, INR expenses, equal/custom splits, balances and recorded settlements.
- Email/password plus configurable Google and Microsoft sign-in, using ASP.NET Core Identity.
- App Service hosts the web app. An Azure Function sends queued update emails.
- SQLite for local development; Azure SQL for deployment. Managed identities and Key Vault references for Azure access and OAuth secrets.
- Optional 15-minute API tokens for APIM exercises; existing browser sign-in stays cookie-based. See [JWT setup](docs/API.md#short-lived-jwt-exercise).

## Run locally

Install the .NET 10 SDK, then from this directory:

```sh
dotnet restore Expenses.slnx
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Expenses.Web -- --init-db
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Expenses.Web --urls http://localhost:5080
```

If file watching blocks startup in a restricted environment, prefix the local commands with `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false DOTNET_USE_POLLING_FILE_WATCHER=1`.

Open [the app](http://localhost:5080). Create an account, then open [the local development inbox](http://localhost:5080/Dev/Emails) to follow its email-confirmation link. Sign in, create a group and invite a second email address. Use a separate browser/private window for the second user. The local inbox previews confirmation, password-reset, invitation and update emails without sending them. It is restricted to Development and loopback connections and is unavailable in Azure Production.

Default password rules are ASP.NET Core Identity's rules: at least six characters including upper/lowercase, digit and a symbol. Use a stronger password for actual accounts. Google/Microsoft buttons appear after their client IDs/secrets are configured; see the Azure guide. Do not put secrets in committed configuration.

For the full local queue flow, run Azurite and install Azure Functions Core Tools v4 with .NET 10 isolated support. Create the `expense-emails` queue in Azurite, set the web app's `Notifications__StorageConnection=UseDevelopmentStorage=true`, copy the Function's `local.settings.example.json` to `local.settings.json`, then run `func start` in `src/Expenses.Functions`. The example is dry-run: it consumes messages but does not send mail. The Function host/Azurite flow still needs runtime verification on a machine with those tools.

## Verify

```sh
dotnet test tests/Expenses.Tests/Expenses.Tests.csproj
dotnet publish src/Expenses.Web -c Release -o artifacts/web
dotnet publish src/Expenses.Functions -c Release -o artifacts/functions
```

The tests cover money calculations, membership restrictions, editing, settlements, optimistic concurrency, invitations, notification recipients, HTTP authentication/CSRF, and Function dry-run handling. Cloud deployment and delivery must be verified separately in your subscription.

## Learn Azure yourself

Follow [the guided Azure lab](docs/AZURE.md). You configure Azure resources yourself; building, testing or opening this project does not provision them. CI builds and tests pushes and pull requests. The deployment workflow requires manual invocation from `main` after you configure OIDC. Terraform is an optional exercise, not a prerequisite to using the portal.

- [Business rules and implementation choices](docs/DECISIONS.md)
- [API and API Management exercises](docs/API.md)
- [GitHub CI/CD setup](docs/CICD.md)
- [Terraform and Kubernetes starters](infra/README.md)

Keep the first deployment small. App Service and AKS are alternative hosts for the same application; run both together only when you deliberately want to compare them. Azure Functions handles asynchronous emails in either case.
