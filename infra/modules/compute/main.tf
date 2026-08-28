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

variable "subnet_id" {
  type = string
}

variable "ssh_public_key" {
  type = string
}

variable "vm1_ocpus" {
  type = number
}

variable "vm1_memory_gb" {
  type = number
}

variable "vm1_user_data" {
  type        = string
  description = "Base64 (optionally gzip) cloud-init payload for VM1."
}

variable "door_user_data" {
  type        = string
  description = "Base64 (optionally gzip) cloud-init payload for the door."
}

data "oci_identity_availability_domains" "ads" {
  compartment_id = var.tenancy_ocid
}

data "oci_core_images" "ubuntu_aarch64" {
  compartment_id           = var.tenancy_ocid
  operating_system         = "Canonical Ubuntu"
  operating_system_version = "22.04"
  shape                    = "VM.Standard.A1.Flex"
  sort_by                  = "TIMECREATED"
  sort_order               = "DESC"
  state                    = "AVAILABLE"

  filter {
    name   = "display_name"
    values = ["^Canonical-Ubuntu-22.04-aarch64-"]
    regex  = true
  }
}

data "oci_core_images" "ubuntu_x86" {
  compartment_id           = var.tenancy_ocid
  operating_system         = "Canonical Ubuntu"
  operating_system_version = "22.04"
  shape                    = "VM.Standard.E2.1.Micro"
  sort_by                  = "TIMECREATED"
  sort_order               = "DESC"
  state                    = "AVAILABLE"

  filter {
    name   = "display_name"
    values = ["^Canonical-Ubuntu-22.04-20"]
    regex  = true
  }
}

locals {
  availability_domain = try(data.oci_identity_availability_domains.ads.availability_domains[0].name, "")
  vm1_image_id        = try(data.oci_core_images.ubuntu_aarch64.images[0].id, "")
  door_image_id       = try(data.oci_core_images.ubuntu_x86.images[0].id, "")
}

resource "oci_core_instance" "vm1" {
  availability_domain = local.availability_domain
  compartment_id      = var.compartment_id
  display_name        = "mcmgr-vm1"
  shape               = "VM.Standard.A1.Flex"
  # Do not pin a fault domain: A1 is often empty in one FD while another still has host capacity.

  shape_config {
    ocpus                     = var.vm1_ocpus
    memory_in_gbs             = var.vm1_memory_gb
    baseline_ocpu_utilization = "BASELINE_1_1"
  }

  create_vnic_details {
    assign_public_ip          = true
    display_name              = "mcmgr-vm1"
    hostname_label            = "mcmgr-vm1"
    subnet_id                 = var.subnet_id
    assign_private_dns_record = true
  }

  source_details {
    source_type             = "image"
    source_id               = local.vm1_image_id
    boot_volume_size_in_gbs = 50
  }

  metadata = {
    ssh_authorized_keys = var.ssh_public_key
    user_data           = var.vm1_user_data
  }

  instance_options {
    are_legacy_imds_endpoints_disabled = true
  }

  agent_config {
    are_all_plugins_disabled = true
    is_management_disabled   = true
    is_monitoring_disabled   = true
  }

  freeform_tags = {
    "mcmgr-role" = "vm1"
  }

  lifecycle {
    ignore_changes = [metadata]

    precondition {
      condition     = local.availability_domain != ""
      error_message = "No availability domains returned. tenancy_ocid must be the real tenancy OCID from ~/.oci/config (tenancy=), not the example REPLACE_ME value."
    }

    precondition {
      condition     = startswith(local.vm1_image_id, "ocid1.image.")
      error_message = "No Canonical Ubuntu 22.04 aarch64 image found for VM.Standard.A1.Flex in this region. Check tenancy_ocid and region."
    }
  }
}

resource "oci_core_instance" "door" {
  availability_domain = local.availability_domain
  compartment_id      = var.compartment_id
  display_name        = "mcmgr-door"
  shape               = "VM.Standard.E2.1.Micro"
  fault_domain        = "FAULT-DOMAIN-3"

  create_vnic_details {
    assign_public_ip          = true
    display_name              = "mcmgr-door"
    hostname_label            = "mcmgr-door"
    subnet_id                 = var.subnet_id
    assign_private_dns_record = true
  }

  source_details {
    source_type             = "image"
    source_id               = local.door_image_id
    boot_volume_size_in_gbs = 50
  }

  metadata = {
    ssh_authorized_keys = var.ssh_public_key
    user_data           = var.door_user_data
  }

  instance_options {
    are_legacy_imds_endpoints_disabled = true
  }

  agent_config {
    are_all_plugins_disabled = true
    is_management_disabled   = true
    is_monitoring_disabled   = true
  }

  freeform_tags = {
    "mcmgr-role" = "door"
  }

  lifecycle {
    ignore_changes = [metadata]

    precondition {
      condition     = startswith(local.door_image_id, "ocid1.image.")
      error_message = "No Canonical Ubuntu 22.04 x86_64 image found for VM.Standard.E2.1.Micro in this region. Check tenancy_ocid and region."
    }
  }
}

data "oci_core_vnic_attachments" "vm1" {
  compartment_id = var.compartment_id
  instance_id    = oci_core_instance.vm1.id
}

data "oci_core_vnic_attachments" "door" {
  compartment_id = var.compartment_id
  instance_id    = oci_core_instance.door.id
}

data "oci_core_vnic" "vm1" {
  vnic_id = data.oci_core_vnic_attachments.vm1.vnic_attachments[0].vnic_id
}

data "oci_core_vnic" "door" {
  vnic_id = data.oci_core_vnic_attachments.door.vnic_attachments[0].vnic_id
}

resource "oci_core_private_ip" "vm1_play" {
  vnic_id        = data.oci_core_vnic.vm1.id
  display_name   = "mcmgr-vm1-play"
  hostname_label = "mcmgr-vm1-play"
}

resource "oci_core_private_ip" "door_play" {
  vnic_id        = data.oci_core_vnic.door.id
  display_name   = "mcmgr-door-play"
  hostname_label = "mcmgr-door-play"
}

resource "oci_core_public_ip" "play" {
  compartment_id = var.compartment_id
  display_name   = "mcmgr-play-ip"
  lifetime       = "RESERVED"
  private_ip_id  = oci_core_private_ip.door_play.id

  # Create parks on the door (idle). Door scripts move it to VM1 while playing.
  # Without this, a later tofu apply (spend-brake Function image) puts it back
  # on the door while Minecraft is already up (SETUP-ISSUE-15).
  lifecycle {
    ignore_changes = [private_ip_id]
  }
}

output "vm1_instance_id" {
  value = oci_core_instance.vm1.id
}

output "vm1_display_name" {
  value = oci_core_instance.vm1.display_name
}

output "vm1_shape" {
  value = oci_core_instance.vm1.shape
}

output "vm1_ocpus" {
  value = var.vm1_ocpus
}

output "vm1_memory_gb" {
  value = var.vm1_memory_gb
}

output "vm1_primary_private_ip" {
  value = data.oci_core_vnic.vm1.private_ip_address
}

output "vm1_secondary_private_ip" {
  value = oci_core_private_ip.vm1_play.ip_address
}

output "vm1_secondary_private_ip_id" {
  value = oci_core_private_ip.vm1_play.id
}

output "vm1_ssh_host" {
  value = data.oci_core_vnic.vm1.public_ip_address
}

output "door_instance_id" {
  value = oci_core_instance.door.id
}

output "door_display_name" {
  value = oci_core_instance.door.display_name
}

output "door_primary_private_ip" {
  value = data.oci_core_vnic.door.private_ip_address
}

output "door_secondary_private_ip" {
  value = oci_core_private_ip.door_play.ip_address
}

output "door_secondary_private_ip_id" {
  value = oci_core_private_ip.door_play.id
}

output "door_ssh_host" {
  value = data.oci_core_vnic.door.public_ip_address
}

output "play_reserved_public_ip" {
  value = oci_core_public_ip.play.ip_address
}

output "play_reserved_public_ip_id" {
  value = oci_core_public_ip.play.id
}

output "ubuntu_image_ocid_vm1" {
  value = local.vm1_image_id
}

output "ubuntu_image_ocid_door" {
  value = local.door_image_id
}

output "availability_domain" {
  value = local.availability_domain
}
