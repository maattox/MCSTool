output "compartment_id" {
  value = module.compartment.id
}

output "tenancy_id" {
  value = var.tenancy_ocid
}

output "region" {
  value = var.region
}

output "vcn_id" {
  value = module.network.vcn_id
}

output "subnet_id" {
  value = module.network.subnet_id
}

output "security_list_id" {
  value = module.network.security_list_id
}

output "vm1_instance_id" {
  value = module.compute.vm1_instance_id
}

output "vm1_display_name" {
  value = module.compute.vm1_display_name
}

output "vm1_shape" {
  value = module.compute.vm1_shape
}

output "vm1_ocpus" {
  value = module.compute.vm1_ocpus
}

output "vm1_memory_gb" {
  value = module.compute.vm1_memory_gb
}

output "vm1_primary_private_ip" {
  value = module.compute.vm1_primary_private_ip
}

output "vm1_secondary_private_ip" {
  value = module.compute.vm1_secondary_private_ip
}

output "vm1_secondary_private_ip_id" {
  value = module.compute.vm1_secondary_private_ip_id
}

output "vm1_ssh_host" {
  value       = module.compute.vm1_ssh_host
  description = "Ephemeral public IP on the VM1 primary VNIC (SSH). Not the reserved play IP."
}

output "door_instance_id" {
  value = module.compute.door_instance_id
}

output "door_display_name" {
  value = module.compute.door_display_name
}

output "door_primary_private_ip" {
  value = module.compute.door_primary_private_ip
}

output "door_secondary_private_ip" {
  value = module.compute.door_secondary_private_ip
}

output "door_secondary_private_ip_id" {
  value = module.compute.door_secondary_private_ip_id
}

output "door_ssh_host" {
  value       = module.compute.door_ssh_host
  description = "Ephemeral public IP on the door primary VNIC (SSH / admin :8080)."
}

output "door_http_port" {
  value = local.door_http_port
}

output "play_reserved_public_ip" {
  value = module.compute.play_reserved_public_ip
}

output "play_reserved_public_ip_id" {
  value = module.compute.play_reserved_public_ip_id
}

output "object_storage_namespace" {
  value = module.storage.namespace
}

output "object_storage_bucket" {
  value = module.storage.bucket_name
}

output "object_storage_bucket_id" {
  value = module.storage.bucket_id
}

output "budget_id" {
  value = module.budget_brake.budget_id
}

output "function_id" {
  value       = module.budget_brake.function_id
  description = "Null until var.function_image is set (Step 3.3 OCIR push)."
}

output "ubuntu_image_ocid_vm1" {
  value = module.compute.ubuntu_image_ocid_vm1
}

output "ubuntu_image_ocid_door" {
  value = module.compute.ubuntu_image_ocid_door
}

output "world_path" {
  value = local.world_path
}

output "minecraft_unit" {
  value = local.minecraft_unit
}

output "ssh_user" {
  value = local.ssh_user
}

output "infra_meta_skeleton" {
  description = "Nested meta/infra.json v2 field groups for Step 3.3 publish. game stays unspecified until SSH bootstrap. No secrets."
  value = {
    version        = 2
    infra_schema   = local.infra_schema
    stack_version  = local.stack_version
    stack_name     = "mcmgr"
    mode           = "always_free"
    region         = var.region
    tenancy_id     = var.tenancy_ocid
    compartment_id = module.compartment.id
    play = {
      reserved_public_ip    = module.compute.play_reserved_public_ip
      reserved_public_ip_id = module.compute.play_reserved_public_ip_id
    }
    game = {
      server_kind       = "vanilla"
      minecraft_version = "unspecified"
      server_jar_sha1   = null
    }
    network = {
      vcn_id           = module.network.vcn_id
      subnet_id        = module.network.subnet_id
      security_list_id = module.network.security_list_id
      minecraft_port   = 25565
      ssh_port         = 22
    }
    vm1 = {
      instance_id             = module.compute.vm1_instance_id
      display_name            = module.compute.vm1_display_name
      shape                   = module.compute.vm1_shape
      shape_ocpus             = module.compute.vm1_ocpus
      shape_memory_gb         = module.compute.vm1_memory_gb
      primary_private_ip      = module.compute.vm1_primary_private_ip
      secondary_private_ip    = module.compute.vm1_secondary_private_ip
      secondary_private_ip_id = module.compute.vm1_secondary_private_ip_id
      ssh_host                = module.compute.vm1_ssh_host
      ssh_user                = local.ssh_user
      world_path              = local.world_path
      minecraft_unit          = local.minecraft_unit
    }
    door = {
      instance_id             = module.compute.door_instance_id
      display_name            = module.compute.door_display_name
      primary_private_ip      = module.compute.door_primary_private_ip
      secondary_private_ip    = module.compute.door_secondary_private_ip
      secondary_private_ip_id = module.compute.door_secondary_private_ip_id
      ssh_host                = module.compute.door_ssh_host
      ssh_user                = local.ssh_user
      http_port               = local.door_http_port
    }
    object_storage = {
      namespace      = module.storage.namespace
      bucket         = module.storage.bucket_name
      bucket_id      = module.storage.bucket_id
      soft_cap_gb    = 9.5
      backup_enabled = true
      prefixes = {
        meta     = "meta/"
        ledger   = "ledger/"
        budget   = "budget/"
        ip       = "ip/"
        messages = "messages/"
        backups  = "backups/"
      }
    }
    budget_brake = {
      budget_id   = module.budget_brake.budget_id
      function_id = module.budget_brake.function_id
    }
    ssh = {
      public_key_fingerprint = null
      private_key_location   = "admin_pc_only"
    }
  }
}
