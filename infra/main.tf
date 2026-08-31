locals {
  world_path     = "/opt/mcmgr/server/world"
  minecraft_unit = "minecraft"
  ssh_user       = "ubuntu"
  door_http_port = 8080
  stack_version  = "0.1.0"
  infra_schema   = 2

  # Product v1: SoftStop VM1 only. Always Free AMD Micro does not use Ampere
  # OCPU-hours; leaving the door up keeps MOTD / reconcile / IP parking.
  softstop_instance_ids = length(var.softstop_instance_ids) > 0 ? var.softstop_instance_ids : [
    module.compute.vm1_instance_id,
  ]
}

module "compartment" {
  source                  = "./modules/compartment"
  tenancy_ocid            = var.tenancy_ocid
  compartment_name        = var.compartment_name
  existing_compartment_id = var.existing_compartment_id
}

module "network" {
  source         = "./modules/network"
  compartment_id = module.compartment.id
  vcn_cidr       = var.vcn_cidr
  subnet_cidr    = var.subnet_cidr
  admin_cidr     = var.admin_cidr
  admin_name     = var.admin_name
}

module "storage" {
  source         = "./modules/storage"
  compartment_id = module.compartment.id
  bucket_name    = var.bucket_name
}

module "compute" {
  source              = "./modules/compute"
  tenancy_ocid        = var.tenancy_ocid
  compartment_id      = module.compartment.id
  subnet_id           = module.network.subnet_id
  ssh_public_key      = var.ssh_public_key
  door_ssh_public_key = var.door_ssh_public_key
  vm1_ocpus           = var.vm1_ocpus
  vm1_memory_gb       = var.vm1_memory_gb
  vm1_user_data = base64gzip(templatefile("${path.module}/cloud-init/vm1.yaml.tftpl", {
    firewalld_unit = replace(file("${path.module}/cloud-init/firewalld-mcmgr.service"), "\r\n", "\n")
  }))
  door_user_data = base64gzip(templatefile("${path.module}/cloud-init/door.yaml.tftpl", {}))
}

module "iam" {
  source           = "./modules/iam"
  tenancy_ocid     = var.tenancy_ocid
  compartment_id   = module.compartment.id
  bucket_name      = module.storage.bucket_name
  door_instance_id = module.compute.door_instance_id
}

module "budget_brake" {
  source                                = "./modules/budget_brake"
  tenancy_ocid                          = var.tenancy_ocid
  compartment_id                        = module.compartment.id
  subnet_id                             = module.network.subnet_id
  alert_email                           = var.alert_email
  function_image                        = var.function_image
  softstop_instance_ids                 = local.softstop_instance_ids
  object_storage_namespace              = module.storage.namespace
  object_storage_bucket_name            = module.storage.bucket_name
  delay_artifacts_after_new_compartment = module.compartment.created
}
