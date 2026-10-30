terraform {
  required_version = ">= 1.9"
  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
  }
}
provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
}
variable "subscription_id" { type = string }
variable "location" { default = "centralindia" }
variable "name" {
  type = string
  description = "Globally unique, lowercase letters and numbers, 3–20 characters."
  validation {
    condition = can(regex("^[a-z][a-z0-9]{2,19}$", var.name))
    error_message = "Use 3–20 lowercase letters/numbers, starting with a letter."
  }
}
resource "azurerm_resource_group" "expenses" {
  name = "${var.name}-lab"
  location = var.location
  tags = { purpose = "expenses-learning" }
}
resource "azurerm_service_plan" "web" {
  name = "${var.name}-web"
  resource_group_name = azurerm_resource_group.expenses.name
  location = azurerm_resource_group.expenses.location
  os_type = "Linux"
  sku_name = "B1"
}
resource "azurerm_storage_account" "expenses" {
  name = "${var.name}store"
  resource_group_name = azurerm_resource_group.expenses.name
  location = azurerm_resource_group.expenses.location
  account_tier = "Standard"
  account_replication_type = "LRS"
  min_tls_version = "TLS1_2"
  allow_nested_items_to_be_public = false
}
resource "azurerm_storage_queue" "emails" {
  name = "expense-emails"
  storage_account_name = azurerm_storage_account.expenses.name
}
resource "azurerm_linux_web_app" "web" {
  name = "${var.name}-web"
  resource_group_name = azurerm_resource_group.expenses.name
  location = azurerm_resource_group.expenses.location
  service_plan_id = azurerm_service_plan.web.id
  https_only = true
  identity { type = "SystemAssigned" }
  site_config {
    always_on = true
    application_stack { dotnet_version = "10.0" }
  }
  app_settings = {
    ASPNETCORE_ENVIRONMENT = "Production"
    App__BaseUrl = "https://${var.name}-web.azurewebsites.net"
    Database__Provider = "SqlServer"
    Notifications__QueueUri = "${azurerm_storage_account.expenses.primary_queue_endpoint}expense-emails"
    DataProtection__KeyPath = "/home/expenses-keys"
    WEBSITE_RUN_FROM_PACKAGE = "1"
  }
}
resource "azurerm_role_assignment" "queue_sender" {
  scope = azurerm_storage_account.expenses.id
  role_definition_name = "Storage Queue Data Message Sender"
  principal_id = azurerm_linux_web_app.web.identity[0].principal_id
}
output "web_url" { value = "https://${azurerm_linux_web_app.web.default_hostname}" }
output "web_identity_id" { value = azurerm_linux_web_app.web.identity[0].principal_id }
