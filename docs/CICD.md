# GitHub CI/CD exercise

The repository root is the directory containing `Expenses.slnx` and `.github/`, not the parent `outputs` directory. The target repository for this lab is `https://github.com/Dexter-Codes/ExpensesTracker`.

## 1. Push the source and verify CI

Review the files in VS Code Source Control before your first commit. Keep the SQLite database, local Function settings, signing keys, `.packages`, `bin`, `obj`, `artifacts` and local `.vscode/settings.json` out of Git. Existing local data and editor deployment settings stay on your machine. Only the generic `.vscode/tasks.json` is shared.

Use `main` as the branch and the provided repository as `origin`. Commit and push when you have reviewed the source. The **Build and test** workflow runs on pushes and pull requests, restores .NET packages, runs the tests and publishes both projects. Its `expenses` artifact includes the hidden `.azurefunctions` folder required by the Function runtime and is retained for seven days. CI does not sign into Azure or deploy anything.

Open **GitHub → Actions → Build and test** and confirm that it succeeds before configuring deployments. Download the artifact if you want to inspect what was published. It contains deployable binaries, not your source checkout or local data. Local verification is separate from a successful GitHub runner execution.

## 2. Create the deployment identity yourself

Create a **user-assigned managed identity** named `expenses-github-deploy` in the lab resource group, in Central India where supported. This identity is for GitHub deployment, not application runtime. Do not give it SQL, queue or Key Vault access.

Under the identity's **Federated credentials**, add the GitHub Actions scenario:

| Field | Value |
|---|---|
| Organization / owner | `Dexter-Codes` |
| Repository | `ExpensesTracker` |
| Entity type | `Environment` |
| Environment | `azure` |
| Credential name | `github-expenses-azure` |
| Issuer | `https://token.actions.githubusercontent.com` |
| Subject | `repo:Dexter-Codes/ExpensesTracker:environment:azure` |
| Audience | `api://AzureADTokenExchange` |

Assign **Website Contributor** to this identity separately on the existing **ExpensesTracker App Service** and **expensestracker-email-idgaf Function App**. Do not assign subscription-wide Contributor. The workflow uses `azure/login` with OIDC; no client secret or publish profile is required.

## 3. Configure the GitHub environment yourself

In **repository Settings → Environments**, create **azure**. Restrict deployment branches to `main`. Add a reviewer if your GitHub plan supports environment approvals. The workflow also refuses to deploy a non-main ref and serializes deployments to avoid overlapping web/Function releases.

Under this environment, add these **environment variables** (not application secrets):

| Name | Value |
|---|---|
| `AZURE_CLIENT_ID` | Client ID of `expenses-github-deploy` |
| `AZURE_TENANT_ID` | Directory/tenant ID of that identity |
| `AZURE_SUBSCRIPTION_ID` | Subscription containing the two apps |
| `AZURE_WEB_APP_NAME` | `ExpensesTracker` |
| `AZURE_FUNCTION_APP_NAME` | `expensestracker-email-idgaf` |

Client/tenant/subscription IDs identify Azure resources; the OIDC trust grants access. OAuth client secrets, JWT signing keys and runtime settings remain in Key Vault/App Service, not in workflow files. Environment protection availability for a private repository depends on your GitHub plan. If environment configuration is unavailable, pause and adapt the trust and workflow together; do not silently use a different OIDC subject.

## 4. Deploy manually

In **Actions → Deploy manually to Azure → Run workflow**, select `main` and `web` first. The workflow reruns tests, authenticates with OIDC, publishes and deploys only the selected component. It never initializes or upgrades the database, changes application settings, or provisions resources.

After the web run succeeds, check `/health`, sign in, read a group and create an API token. Existing runtime settings, managed identities and database data should remain intact; verify this in the actual deployment.

Run the workflow separately with `functions`. It publishes the .NET isolated project and uses `Azure/functions-action` against the existing **Windows Consumption** app. Keep the working host storage, `WEBSITE_RUN_FROM_PACKAGE=1`, queue connection, identity and email settings. The action discovers the hosting plan through Azure; no Flex-only SKU or remote build is requested. Check that `SendEmail` is listed and test one intended notification. A successful invocation is not proof of inbox delivery.

Once the first manual deployments work, change a visible label, push it, observe CI and manually deploy. This separates continuous integration from an intentional production release. Automatic deployment on a push is a later change, not enabled here.

## Troubleshooting

- CI restore/build/test failure: inspect the failed GitHub step; Azure permissions are not involved yet.
- OIDC no matching credential: check the exact owner, repository, environment name and subject. The workflow must target the `azure` environment.
- Azure deployment forbidden: check Website Contributor on the correct app for the deployment identity and allow time for role propagation.
- Missing Function after deployment: inspect the publish root for `host.json`, `functions.metadata` and `.azurefunctions`; do not deploy the source directory or exclude hidden runtime files.
- App fails after deployment: inspect its startup logs and Key Vault reference status; do not rerun database initialization against existing data as a repair step.

References: [GitHub OIDC and Azure](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure), [Functions GitHub Actions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-how-to-github-actions), [Functions action parameters](https://github.com/Azure/functions-action).
