terraform {
  required_providers {
    oci = {
      source = "oracle/oci"
    }
  }
}

variable "tenancy_ocid" {
  type = string
}

variable "compartment_name" {
  type = string
}

variable "existing_compartment_id" {
  type = string
}

locals {
  create_compartment = trimspace(var.existing_compartment_id) == ""
}

resource "oci_identity_compartment" "mcmgr" {
  count          = local.create_compartment ? 1 : 0
  compartment_id = var.tenancy_ocid
  name           = var.compartment_name
  description    = "MC Manager Always Free Minecraft doorbell stack"
  enable_delete  = true

  freeform_tags = {
    "mcmgr-domain" = "mc-server-compartment"
  }
}

output "id" {
  value = local.create_compartment ? oci_identity_compartment.mcmgr[0].id : trimspace(var.existing_compartment_id)
}

output "name" {
  value = var.compartment_name
}

output "created" {
  value = local.create_compartment
}
