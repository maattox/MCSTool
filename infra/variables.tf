variable "tenancy_ocid" {
  type        = string
  description = "Tenancy OCID (from ~/.oci/config tenancy=). Required for dynamic groups and image lookup. OpenTofu does not read this from ~/.oci automatically."

  validation {
    condition     = can(regex("^ocid1\\.tenancy\\.oc1\\.\\.[a-z0-9]+$", var.tenancy_ocid))
    error_message = "tenancy_ocid must be a real tenancy OCID (ocid1.tenancy.oc1..…). Copy tenancy= from %USERPROFILE%\\.oci\\config or Console → profile → Tenancy details. Do not leave REPLACE_ME from the example."
  }
}

variable "region" {
  type        = string
  description = "Home region for Always Free eligibility (e.g. us-sanjose-1)."
}

variable "oci_profile" {
  type        = string
  description = "Profile name in ~/.oci/config."
  default     = "DEFAULT"
}

variable "ssh_public_key" {
  type        = string
  description = "OpenSSH public key injected into the game VM (ubuntu user). Entire single line from a .pub file. Also used for the door when door_ssh_public_key is empty."

  validation {
    condition     = can(regex("^ssh-(ed25519|rsa|ed25519-sk|rsa-sha2-256|rsa-sha2-512) [A-Za-z0-9+/=]+", var.ssh_public_key)) && !strcontains(var.ssh_public_key, "AAAA...")
    error_message = "ssh_public_key must be a real OpenSSH public key line (starts with ssh-ed25519 or ssh-rsa), not the example AAAA... comment placeholder."
  }
}

variable "door_ssh_public_key" {
  type        = string
  description = "OpenSSH public key for the door VM. Empty = same as ssh_public_key."
  default     = ""

  validation {
    condition = var.door_ssh_public_key == "" || (
      can(regex("^ssh-(ed25519|rsa|ed25519-sk|rsa-sha2-256|rsa-sha2-512) [A-Za-z0-9+/=]+", var.door_ssh_public_key))
      && !strcontains(var.door_ssh_public_key, "AAAA...")
    )
    error_message = "door_ssh_public_key must be empty (use ssh_public_key) or a real OpenSSH public key line."
  }
}

variable "admin_cidr" {
  type        = string
  description = "Admin public IPv4 as /32. Used for SSH, Minecraft, and door :8080 on the Security List."

  validation {
    condition     = can(cidrhost(var.admin_cidr, 0)) && endswith(var.admin_cidr, "/32")
    error_message = "admin_cidr must be an IPv4 CIDR ending in /32."
  }
}

variable "admin_name" {
  type        = string
  description = "Display name used in Security List rule descriptions (Manager whitelist ownership)."
  default     = "admin"
}

variable "alert_email" {
  type        = string
  description = "Budget alert recipient (comma-separated if more than one)."
}

variable "compartment_name" {
  type        = string
  description = "Display name of the dedicated stack compartment when this module creates it."
  default     = "mcmgr"
}

variable "existing_compartment_id" {
  type        = string
  description = "If set, skip creating compartment mcmgr and place resources in this OCID (disposable test / repair)."
  default     = ""
}

variable "vcn_cidr" {
  type        = string
  description = "VCN CIDR."
  default     = "10.0.0.0/16"
}

variable "subnet_cidr" {
  type        = string
  description = "Public subnet CIDR (must be inside vcn_cidr)."
  default     = "10.0.0.0/24"
}

variable "vm1_ocpus" {
  type        = number
  description = "VM1 A1 Flex OCPUs. Setup picker writes 2 or 4; default 4 (product MVP)."
  default     = 4
}

variable "vm1_memory_gb" {
  type        = number
  description = "VM1 A1 Flex memory in GB. Setup picker writes 12 (with 2 OCPU) or 24 (with 4); default 24."
  default     = 24
}

variable "bucket_name" {
  type        = string
  description = "Object Storage bucket name (unique per namespace). If taken, change this and record the actual name in meta."
  default     = "mcmgr-shared-data"
}

variable "function_image" {
  type        = string
  description = "OCIR image for the spend-brake Function (region.ocir.io/namespace/mcmgr-fn/softstop:tag). Empty in Step 3.1 — Function + Events action are skipped until Step 3.3."
  default     = ""
}

variable "softstop_instance_ids" {
  type        = list(string)
  description = "Instance OCIDs the $1 Function SoftStops. Empty = VM1 only (product v1: Always Free door Micro stays up)."
  default     = []
}
