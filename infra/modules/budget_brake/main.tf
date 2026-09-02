terraform {
  required_providers {
    oci = {
      source = "oracle/oci"
    }
    time = {
      source = "hashicorp/time"
    }
  }
}

variable "tenancy_ocid" {
  type        = string
  description = "Tenancy OCID. CreateBudget requires the budget resource to live in the tenancy; targets still point at the stack compartment."
}

variable "compartment_id" {
  type = string
}

variable "subnet_id" {
  type = string
}

variable "alert_email" {
  type = string
}

variable "function_image" {
  type = string
}

variable "softstop_instance_ids" {
  type = list(string)
}

variable "object_storage_namespace" {
  type = string
}

variable "object_storage_bucket_name" {
  type = string
}

variable "delay_artifacts_after_new_compartment" {
  type        = bool
  default     = false
  description = "True when this apply created the stack compartment. OCIR 404-DENIED until Artifacts sees a brand-new compartment (SETUP-ISSUE-9)."
}

locals {
  create_function = trimspace(var.function_image) != ""
  # OCI CreateBudget description must be 0–200 characters (400-InvalidParameter otherwise).
  budget_description = "Last-resort $1 actual-spend brake. SoftStops VM1 and PUTs lock; door stays up. Residual ~$1-$2 that month possible. Not a $0 guarantee."
}

resource "time_sleep" "wait_artifacts" {
  count           = var.delay_artifacts_after_new_compartment ? 1 : 0
  create_duration = "2m"
}

resource "oci_budget_budget" "one_usd" {
  # CreateBudget compartmentId must be the tenancy, not the child stack compartment.
  compartment_id                        = var.tenancy_ocid
  amount                                = 1
  display_name                          = "mcmgr-budget-1usd"
  description                           = local.budget_description
  reset_period                          = "MONTHLY"
  processing_period_type                = "MONTH"
  budget_processing_period_start_offset = 1
  target_type                           = "COMPARTMENT"
  targets                               = [var.compartment_id]

  lifecycle {
    precondition {
      condition     = length(local.budget_description) <= 200
      error_message = "OCI CreateBudget description must be 0-200 characters."
    }
  }
}

resource "oci_budget_alert_rule" "one_usd" {
  budget_id      = oci_budget_budget.one_usd.id
  display_name   = "mcmgr-budget-1usd-alert"
  threshold      = 1
  threshold_type = "ABSOLUTE"
  type           = "ACTUAL"
  recipients     = var.alert_email
  message        = "MC Manager $1 spend brake fired. Minecraft VM will SoftStop; doorbell stays up. Residual ~$1-$2 that month is possible because the Function is not instantaneous."
}

resource "oci_artifacts_container_repository" "softstop" {
  # Artifacts lags Identity on a brand-new compartment (404-DENIED ~1 min). Sleep + long create timeout.
  depends_on     = [time_sleep.wait_artifacts]
  compartment_id = var.compartment_id
  display_name   = "mcmgr-fn/softstop"
  is_immutable   = false
  is_public      = false

  timeouts {
    create = "10m"
    delete = "20m"
  }
}

resource "oci_functions_application" "app" {
  compartment_id = var.compartment_id
  display_name   = "mcmgr-fn-app"
  shape          = "GENERIC_ARM"
  subnet_ids     = [var.subnet_id]
}

resource "oci_functions_function" "softstop" {
  count              = local.create_function ? 1 : 0
  application_id     = oci_functions_application.app.id
  display_name       = "mcmgr-fn-softstop"
  image              = var.function_image
  memory_in_mbs      = "256"
  timeout_in_seconds = 30

  config = {
    INSTANCE_OCIDS = join(",", var.softstop_instance_ids)
    OS_NAMESPACE   = var.object_storage_namespace
    OS_BUCKET      = var.object_storage_bucket_name
    OS_LOCK_OBJECT = "meta/spend-brake-triggered.json"
    BUDGET_ID      = oci_budget_budget.one_usd.id
  }
}

resource "oci_events_rule" "budget_alert" {
  count          = local.create_function ? 1 : 0
  compartment_id = var.compartment_id
  display_name   = "mcmgr-events-budget-alert"
  description    = "Budget triggered-alert → spend-brake Function (no ONS topic)."
  is_enabled     = true

  condition = jsonencode({
    eventType = ["com.oraclecloud.budgets.createtriggeredalert"]
    data = {
      compartmentId = [var.compartment_id]
    }
  })

  actions {
    actions {
      action_type = "FAAS"
      is_enabled  = true
      function_id = oci_functions_function.softstop[0].id
    }
  }
}

output "budget_id" {
  value = oci_budget_budget.one_usd.id
}

output "function_application_id" {
  value = oci_functions_application.app.id
}

output "function_id" {
  value = local.create_function ? oci_functions_function.softstop[0].id : null
}

output "events_rule_id" {
  value = local.create_function ? oci_events_rule.budget_alert[0].id : null
}

output "ocir_repository_id" {
  value = oci_artifacts_container_repository.softstop.id
}

output "ocir_repository_name" {
  value = oci_artifacts_container_repository.softstop.display_name
}
