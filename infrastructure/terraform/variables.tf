variable "project_id" {
  description = "GCP Project ID"
  type        = string
}

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
  
  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "Environment must be dev, staging, or prod"
  }
}

variable "region" {
  description = "GCP region"
  type        = string
  default     = "us-central1"
}

variable "image_tag" {
  description = "Docker image tag to deploy"
  type        = string
  default     = "latest"
}

variable "hasura_endpoint" {
  description = "Hasura GraphQL endpoint URL"
  type        = string
}

variable "hasura_events_topic_id" {
  description = "Pub/Sub topic ID for Hasura events"
  type        = string
}

variable "canary_revision" {
  description = "Cloud Run revision for canary deployment"
  type        = string
  default     = ""
}
