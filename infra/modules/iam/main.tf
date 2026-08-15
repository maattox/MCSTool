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

variable "compartment_id" {
  type = string
}

variable "bucket_name" {
  type = string
}

variable "door_instance_id" {
  type        = string
  description = "Door instance OCID. Tag matching (mcmgr-role) does not put the door in this group on identity-domain tenancies; pin by instance.id like the lab SoT."
}

resource "oci_identity_dynamic_group" "instances" {
  compartment_id = var.tenancy_ocid
  name           = "mcmgr-dg-instances"
  description    = "All compute instances in the mcmgr stack compartment (Object Storage + instance-family)"
  matching_rule  = "ALL {instance.compartment.id = '${var.compartment_id}'}"
}

resource "oci_identity_dynamic_group" "door" {
  compartment_id = var.tenancy_ocid
  name           = "mcmgr-dg-door"
  description    = "Door Micro only — reserved play IP move"
  matching_rule = "ALL {instance.id = '${var.door_instance_id}'}"
}

resource "oci_identity_dynamic_group" "fn" {
  compartment_id = var.tenancy_ocid
  name           = "mcmgr-dg-fn"
  description    = "Functions in the mcmgr stack compartment ($1 spend brake)"
  matching_rule  = "ALL {resource.type = 'fnfunc', resource.compartment.id = '${var.compartment_id}'}"
}

resource "oci_identity_policy" "stack" {
  compartment_id = var.compartment_id
  name           = "mcmgr-stack"
  description    = "Least-privilege instance, door, and Function access for the MC Manager stack"
  statements = [
    "Allow dynamic-group ${oci_identity_dynamic_group.instances.name} to read buckets in compartment id ${var.compartment_id} where target.bucket.name='${var.bucket_name}'",
    "Allow dynamic-group ${oci_identity_dynamic_group.instances.name} to manage objects in compartment id ${var.compartment_id} where target.bucket.name='${var.bucket_name}'",
    "Allow dynamic-group ${oci_identity_dynamic_group.instances.name} to use instance-family in compartment id ${var.compartment_id}",
    "Allow dynamic-group ${oci_identity_dynamic_group.door.name} to manage public-ips in compartment id ${var.compartment_id}",
    "Allow dynamic-group ${oci_identity_dynamic_group.door.name} to use private-ips in compartment id ${var.compartment_id}",
    "Allow dynamic-group ${oci_identity_dynamic_group.door.name} to use virtual-network-family in compartment id ${var.compartment_id}",
    "Allow dynamic-group ${oci_identity_dynamic_group.fn.name} to use instance-family in compartment id ${var.compartment_id}",
    "Allow dynamic-group ${oci_identity_dynamic_group.fn.name} to read buckets in compartment id ${var.compartment_id} where target.bucket.name='${var.bucket_name}'",
    "Allow dynamic-group ${oci_identity_dynamic_group.fn.name} to manage objects in compartment id ${var.compartment_id} where target.bucket.name='${var.bucket_name}'",
  ]

  depends_on = [
    oci_identity_dynamic_group.instances,
    oci_identity_dynamic_group.door,
    oci_identity_dynamic_group.fn,
  ]
}

# UpdatePublicIp is NotAuthorizedOrNotFound when these verbs are only compartment-scoped
# (lab SoT uses tenancy). Must live at the root; a compartment policy cannot say "in tenancy".
resource "oci_identity_policy" "door_ip" {
  compartment_id = var.tenancy_ocid
  name           = "mcmgr-door-ip"
  description    = "Door reserved play IP move (UpdatePublicIp is tenancy-scoped)"
  statements = [
    "Allow dynamic-group ${oci_identity_dynamic_group.door.name} to manage public-ips in tenancy",
    "Allow dynamic-group ${oci_identity_dynamic_group.door.name} to use private-ips in tenancy",
    "Allow dynamic-group ${oci_identity_dynamic_group.door.name} to use virtual-network-family in tenancy",
  ]

  depends_on = [oci_identity_dynamic_group.door]
}

output "dg_instances_id" {
  value = oci_identity_dynamic_group.instances.id
}

output "dg_door_id" {
  value = oci_identity_dynamic_group.door.id
}

output "dg_fn_id" {
  value = oci_identity_dynamic_group.fn.id
}

output "policy_id" {
  value = oci_identity_policy.stack.id
}

output "door_ip_policy_id" {
  value = oci_identity_policy.door_ip.id
}
