terraform {
  required_providers {
    oci = {
      source = "oracle/oci"
    }
  }
}

variable "compartment_id" {
  type = string
}

variable "bucket_name" {
  type = string
}

# Namespace is tenancy-scoped. Omit compartment_id so GetNamespace uses the
# authenticated caller (a placeholder tenancy OCID is not a valid compartment).
data "oci_objectstorage_namespace" "ns" {}

resource "oci_objectstorage_bucket" "shared" {
  compartment_id        = var.compartment_id
  name                  = var.bucket_name
  namespace             = data.oci_objectstorage_namespace.ns.namespace
  access_type           = "NoPublicAccess"
  storage_tier          = "Standard"
  versioning            = "Disabled"
  object_events_enabled = false
  auto_tiering          = "Disabled"

  lifecycle {
    prevent_destroy = true
  }
}

output "namespace" {
  value = data.oci_objectstorage_namespace.ns.namespace
}

output "bucket_name" {
  value = oci_objectstorage_bucket.shared.name
}

output "bucket_id" {
  value = oci_objectstorage_bucket.shared.bucket_id
}
