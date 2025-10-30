# Complete Terraform configuration for Cloud Run event handler service

terraform {
  required_version = ">= 1.0"
  
  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 5.0"
    }
  }

  backend "gcs" {
    bucket = "henry-terraform-state"
    prefix = "event-handler-service"
  }
}

provider "google" {
  project = var.project_id
  region  = var.region
}

# Service Account
resource "google_service_account" "service" {
  account_id   = "event-handler-service"
  display_name = "Event Handler Service"
  description  = "Service account for event handler microservice"
}

# IAM - Hasura Admin Secret access
resource "google_secret_manager_secret_iam_member" "hasura_secret" {
  secret_id = "hasura-admin-secret"
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.service.email}"
}

# IAM - Chargebee API Key access
resource "google_secret_manager_secret_iam_member" "chargebee_key" {
  secret_id = "chargebee-api-key"
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.service.email}"
}

# IAM - Cloud Tasks enqueuer
resource "google_project_iam_member" "task_enqueuer" {
  project = var.project_id
  role    = "roles/cloudtasks.enqueuer"
  member  = "serviceAccount:${google_service_account.service.email}"
}

# IAM - Pub/Sub publisher
resource "google_project_iam_member" "pubsub_publisher" {
  project = var.project_id
  role    = "roles/pubsub.publisher"
  member  = "serviceAccount:${google_service_account.service.email}"
}

# Cloud Run Service
resource "google_cloud_run_v2_service" "service" {
  name     = "event-handler-service-${var.environment}"
  location = var.region
  ingress  = "INGRESS_TRAFFIC_INTERNAL_ONLY"

  template {
    service_account = google_service_account.service.email

    scaling {
      min_instance_count = var.environment == "prod" ? 1 : 0
      max_instance_count = var.environment == "prod" ? 10 : 3
    }

    containers {
      image = "${var.region}-docker.pkg.dev/${var.project_id}/docker-repo/event-handler-service:${var.image_tag}"

      resources {
        limits = {
          cpu    = "1"
          memory = "512Mi"
        }
        cpu_idle = true
        startup_cpu_boost = true
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = var.environment == "prod" ? "Production" : "Development"
      }

      env {
        name  = "GCP_PROJECT_ID"
        value = var.project_id
      }

      env {
        name  = "GCP_LOCATION"
        value = var.region
      }

      env {
        name  = "CLOUD_TASK_QUEUE"
        value = google_cloud_tasks_queue.queue.name
      }

      env {
        name  = "SERVICE_URL"
        value = "https://${google_cloud_run_v2_service.service.uri}"
      }

      env {
        name  = "HASURA_ENDPOINT"
        value = var.hasura_endpoint
      }

      env {
        name  = "EVENT_TOPIC_ID"
        value = google_pubsub_topic.events.name
      }

      env {
        name = "HASURA_ADMIN_SECRET"
        value_source {
          secret_key_ref {
            secret  = "hasura-admin-secret"
            version = "latest"
          }
        }
      }

      env {
        name = "CHARGEBEE_API_KEY"
        value_source {
          secret_key_ref {
            secret  = "chargebee-api-key"
            version = "latest"
          }
        }
      }

      ports {
        container_port = 8080
      }

      startup_probe {
        http_get {
          path = "/api/health"
          port = 8080
        }
        initial_delay_seconds = 0
        period_seconds        = 10
        timeout_seconds       = 3
        failure_threshold     = 3
      }

      liveness_probe {
        http_get {
          path = "/api/health"
          port = 8080
        }
        initial_delay_seconds = 30
        period_seconds        = 30
        timeout_seconds       = 5
        failure_threshold     = 3
      }
    }

    # Maximum request execution time
    timeout = "300s"
  }

  traffic {
    type    = "TRAFFIC_TARGET_ALLOCATION_TYPE_LATEST"
    percent = 100
  }

  # Enable canary deployments for non-prod
  dynamic "traffic" {
    for_each = var.environment != "prod" && var.canary_revision != "" ? [1] : []
    content {
      type     = "TRAFFIC_TARGET_ALLOCATION_TYPE_REVISION"
      revision = var.canary_revision
      percent  = 10
    }
  }

  lifecycle {
    ignore_changes = [
      template[0].revision,
      traffic
    ]
  }
}

# Pub/Sub Topic for Events
resource "google_pubsub_topic" "events" {
  name = "contract-events-${var.environment}"

  message_retention_duration = "86400s" # 24 hours
}

# Pub/Sub Subscription (push to Cloud Run)
resource "google_pubsub_subscription" "hasura_events" {
  name  = "hasura-events-${var.environment}"
  topic = var.hasura_events_topic_id

  push_config {
    push_endpoint = "${google_cloud_run_v2_service.service.uri}/api/events"

    oidc_token {
      service_account_email = google_service_account.pubsub_invoker.email
    }

    attributes = {
      x-goog-version = "v1"
    }
  }

  ack_deadline_seconds = 60
  message_retention_duration = "86400s"
  retain_acked_messages = false

  retry_policy {
    minimum_backoff = "10s"
    maximum_backoff = "600s"
  }

  dead_letter_policy {
    dead_letter_topic = google_pubsub_topic.dead_letter.id
    max_delivery_attempts = 5
  }
}

# Dead Letter Topic
resource "google_pubsub_topic" "dead_letter" {
  name = "hasura-events-dead-letter-${var.environment}"
}

# Service Account for Pub/Sub to invoke Cloud Run
resource "google_service_account" "pubsub_invoker" {
  account_id   = "pubsub-invoker-event-handler"
  display_name = "Pub/Sub Invoker for Event Handler"
}

resource "google_cloud_run_service_iam_member" "pubsub_invoker" {
  service  = google_cloud_run_v2_service.service.name
  location = var.region
  role     = "roles/run.invoker"
  member   = "serviceAccount:${google_service_account.pubsub_invoker.email}"
}

# Cloud Tasks Queue
resource "google_cloud_tasks_queue" "queue" {
  name     = "contract-tasks-${var.environment}"
  location = var.region

  rate_limits {
    max_dispatches_per_second = var.environment == "prod" ? 100 : 10
    max_concurrent_dispatches = var.environment == "prod" ? 50 : 5
  }

  retry_config {
    max_attempts = 5
    max_retry_duration = "3600s" # 1 hour
    min_backoff = "10s"
    max_backoff = "300s"
    max_doublings = 3
  }

  stackdriver_logging_config {
    sampling_ratio = 1.0
  }
}

# Outputs
output "service_url" {
  value       = google_cloud_run_v2_service.service.uri
  description = "URL of the Cloud Run service"
}

output "service_account_email" {
  value       = google_service_account.service.email
  description = "Email of the service account"
}

output "queue_name" {
  value       = google_cloud_tasks_queue.queue.name
  description = "Name of the Cloud Tasks queue"
}

output "events_topic_name" {
  value       = google_pubsub_topic.events.name
  description = "Name of the events Pub/Sub topic"
}
