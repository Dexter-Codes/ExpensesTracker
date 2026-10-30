# Terraform exercise — run this yourself later

This optional starter creates a resource group, Linux App Service plan/app, Storage Account, email queue, and a queue-sender role assignment. It does not create SQL, Key Vault, Functions, email domains, APIM, or AKS; configure those through the portal guide first. The web app needs those remaining settings and database initialization before it can serve the application.

Do not apply this alongside portal-created resources with the same names. Use a fresh resource group/name, or learn `terraform import` before managing existing resources.

1. Install Terraform and sign into Azure CLI yourself.
2. Copy `example.tfvars` to `terraform.tfvars` and set your own values.
3. Run `terraform init`, `terraform fmt`, `terraform validate`, then `terraform plan -out=expenses.tfplan`.
4. Read every planned resource and its pricing in Azure before running `terraform apply expenses.tfplan`.
5. Configure the remaining services/settings from `../docs/AZURE.md` and deploy the app.
6. Run `terraform plan -destroy` and inspect it before cleanup. `terraform destroy` deletes the resources this state manages.

No Terraform commands have been run against Azure. This starter is not live-validated. Runtime/region availability and credit-offer restrictions must be checked in your subscription. Commit `.terraform.lock.hcl` after your own successful initialization. Keep state and tfvars containing secrets out of Git; use a secured remote state backend when collaborating.

Kubernetes manifests in `kubernetes/` are a later alternative hosting exercise. Replace the image, SQL server/database, Storage Account and workload identity placeholders first. They assume you have already configured AKS OIDC/workload identity, the federated credential, ACR pull permissions, SQL access, and queue-sender permissions. No cluster or Azure resource is created automatically.
