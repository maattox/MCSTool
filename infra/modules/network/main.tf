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

variable "vcn_cidr" {
  type = string
}

variable "subnet_cidr" {
  type = string
}

variable "admin_cidr" {
  type = string
}

variable "admin_name" {
  type = string
}

resource "oci_core_vcn" "mcmgr" {
  cidr_blocks    = [var.vcn_cidr]
  compartment_id = var.compartment_id
  display_name   = "mcmgr-vcn"
  dns_label      = "mcmgr"
  is_ipv6enabled = false
}

resource "oci_core_internet_gateway" "mcmgr" {
  compartment_id = var.compartment_id
  display_name   = "mcmgr-igw"
  enabled        = true
  vcn_id         = oci_core_vcn.mcmgr.id
}

resource "oci_core_default_route_table" "public" {
  manage_default_resource_id = oci_core_vcn.mcmgr.default_route_table_id
  display_name               = "mcmgr-rt-public"

  route_rules {
    destination       = "0.0.0.0/0"
    destination_type  = "CIDR_BLOCK"
    network_entity_id = oci_core_internet_gateway.mcmgr.id
  }
}

resource "oci_core_security_list" "mcmgr" {
  compartment_id = var.compartment_id
  vcn_id         = oci_core_vcn.mcmgr.id
  display_name   = "mcmgr-sl"

  egress_security_rules {
    destination      = "0.0.0.0/0"
    destination_type = "CIDR_BLOCK"
    protocol         = "all"
    stateless        = false
  }

  # Path MTU discovery
  ingress_security_rules {
    protocol    = "1"
    source      = "0.0.0.0/0"
    source_type = "CIDR_BLOCK"
    stateless   = false

    icmp_options {
      type = 3
      code = 4
    }
  }

  # ICMP destination unreachable from the VCN
  ingress_security_rules {
    protocol    = "1"
    source      = var.vcn_cidr
    source_type = "CIDR_BLOCK"
    stateless   = false

    icmp_options {
      type = 3
    }
  }

  # Door wait_forge private poll (subnet → Minecraft TCP)
  ingress_security_rules {
    description = "Door wait_forge private poll"
    protocol    = "6"
    source      = var.subnet_cidr
    source_type = "CIDR_BLOCK"
    stateless   = false

    tcp_options {
      min = 25565
      max = 25565
    }
  }

  # Admin SSH — Manager description convention
  ingress_security_rules {
    description = "${var.admin_name} SSH access"
    protocol    = "6"
    source      = var.admin_cidr
    source_type = "CIDR_BLOCK"
    stateless   = false

    tcp_options {
      min = 22
      max = 22
    }
  }

  # Admin Minecraft TCP — Manager description = player name
  ingress_security_rules {
    description = var.admin_name
    protocol    = "6"
    source      = var.admin_cidr
    source_type = "CIDR_BLOCK"
    stateless   = false

    tcp_options {
      min = 25565
      max = 25565
    }
  }

  # Admin Minecraft UDP
  ingress_security_rules {
    description = var.admin_name
    protocol    = "17"
    source      = var.admin_cidr
    source_type = "CIDR_BLOCK"
    stateless   = false

    udp_options {
      min = 25565
      max = 25565
    }
  }

  # Admin door HTTP
  ingress_security_rules {
    description = "${var.admin_name} door access"
    protocol    = "6"
    source      = var.admin_cidr
    source_type = "CIDR_BLOCK"
    stateless   = false

    tcp_options {
      min = 8080
      max = 8080
    }
  }

  lifecycle {
    # Day-2 Manager whitelist sync owns friend /32s. Do not revert ingress on later applies.
    ignore_changes = [ingress_security_rules]
  }
}

resource "oci_core_subnet" "public" {
  cidr_block                 = var.subnet_cidr
  compartment_id             = var.compartment_id
  display_name               = "mcmgr-subnet-public"
  dns_label                  = "public"
  prohibit_internet_ingress  = false
  prohibit_public_ip_on_vnic = false
  vcn_id                     = oci_core_vcn.mcmgr.id
  route_table_id             = oci_core_default_route_table.public.id
  security_list_ids          = [oci_core_security_list.mcmgr.id]
}

output "vcn_id" {
  value = oci_core_vcn.mcmgr.id
}

output "subnet_id" {
  value = oci_core_subnet.public.id
}

output "security_list_id" {
  value = oci_core_security_list.mcmgr.id
}

output "internet_gateway_id" {
  value = oci_core_internet_gateway.mcmgr.id
}
