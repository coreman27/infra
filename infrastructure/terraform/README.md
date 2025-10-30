# Terraform Infrastructure Pattern

## Overview

All Henry Meds infrastructure is defined as code using Terraform. This ensures consistent, reproducible deployments across environments.

## Structure

```
terraform/
├── main.tf              # Main infrastructure definition
├── variables.tf         # Input variables
├── outputs.tf           # Output values
├── providers.tf         # Cloud providers
├── backend.tf           # Terraform state backend
└── modules/
    ├── cloud-run/
    ├── pubsub/
    └── secrets/
```

## Cloud Run Service Pattern

### main.tf
```hcl
# Google Cloud Provider
provider "google" {
  project = var.project_id
  region  = var.region
}

# Cloud Run Service
resource "google_cloud_run_service" "service" {
  name     = var.service_name
  location = var.region

  template {
    spec {
      # Service account with minimal permissions
      service_account_name = google_service_account.service.email

      containers {
        image = "gcr.io/${var.project_id}/${var.service_name}:${var.image_tag}"

        # Resource limits
        resources {
          limits = {
            cpu    = var.cpu_limit
            memory = var.memory_limit
          }
        }

        # Environment variables
        env {
          name  = "ASPNETCORE_ENVIRONMENT"
          value = var.environment
        }

        env {
          name  = "HASURA_ENDPOINT"
          value = var.hasura_endpoint
        }

        # Secrets from Secret Manager
        env {
          name = "HASURA_ADMIN_SECRET"
          value_from {
            secret_key_ref {
              name = google_secret_manager_secret.hasura_secret.secret_id
              key  = "latest"
            }
          }
        }

        env {
          name = "CHARGEBEE_API_KEY"
          value_from {
            secret_key_ref {
              name = google_secret_manager_secret.chargebee_key.secret_id
              key  = "latest"
            }
          }
        }

        # Health check
        liveness_probe {
          http_get {
            path = "/health"
          }
          initial_delay_seconds = 30
          timeout_seconds       = 3
          period_seconds        = 10
          failure_threshold     = 3
        }
      }

      # Autoscaling
      container_concurrency = 80
      timeout_seconds       = 300
    }

    metadata {
      annotations = {
        "autoscaling.knative.dev/minScale"      = var.min_instances
        "autoscaling.knative.dev/maxScale"      = var.max_instances
        "run.googleapis.com/cpu-throttling"     = "false"
        "run.googleapis.com/execution-environment" = "gen2"
        
        # Revision tagging for canary deployments
        "run.googleapis.com/ingress-status" = "all"
      }
    }
  }

  traffic {
    # 100% traffic to latest revision
    percent         = 100
    latest_revision = true
  }

  lifecycle {
    ignore_changes = [
      template[0].metadata[0].annotations["run.googleapis.com/client-name"],
      template[0].metadata[0].annotations["run.googleapis.com/client-version"],
    ]
  }
}

# Service account for Cloud Run
resource "google_service_account" "service" {
  account_id   = "${var.service_name}-sa"
  display_name = "Service account for ${var.service_name}"
}

# IAM binding for public access (adjust for internal services)
resource "google_cloud_run_service_iam_member" "public" {
  service  = google_cloud_run_service.service.name
  location = google_cloud_run_service.service.location
  role     = "roles/run.invoker"
  member   = "allUsers"
}

# Pub/Sub subscription for events
resource "google_pubsub_subscription" "events" {
  name  = "${var.service_name}-events"
  topic = var.event_topic_name

  push_config {
    push_endpoint = "${google_cloud_run_service.service.status[0].url}/api/events"

    oidc_token {
      service_account_email = google_service_account.service.email
    }
  }

  ack_deadline_seconds = 60
  message_retention_duration = "604800s" # 7 days

  retry_policy {
    minimum_backoff = "10s"
    maximum_backoff = "600s"
  }

  dead_letter_policy {
    dead_letter_topic     = google_pubsub_topic.dead_letter.id
    max_delivery_attempts = 5
  }
}

# Dead letter queue for failed events
resource "google_pubsub_topic" "dead_letter" {
  name = "${var.service_name}-dlq"
}

# Secrets (create secret, NOT version - manual step)
resource "google_secret_manager_secret" "hasura_secret" {
  secret_id = "${var.service_name}-hasura-secret"

  replication {
    auto {}
  }
}

resource "google_secret_manager_secret" "chargebee_key" {
  secret_id = "${var.service_name}-chargebee-key"

  replication {
    auto {}
  }
}

# Grant service account access to secrets
resource "google_secret_manager_secret_iam_member" "hasura_access" {
  secret_id = google_secret_manager_secret.hasura_secret.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.service.email}"
}

resource "google_secret_manager_secret_iam_member" "chargebee_access" {
  secret_id = google_secret_manager_secret.chargebee_key.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.service.email}"
}

# Cloud Tasks queue for scheduled jobs
resource "google_cloud_tasks_queue" "tasks" {
  name     = "${var.service_name}-tasks"
  location = var.region

  rate_limits {
    max_concurrent_dispatches = 10
    max_dispatches_per_second = 5
  }

  retry_config {
    max_attempts       = 5
    max_retry_duration = "3600s"
    min_backoff        = "10s"
    max_backoff        = "600s"
    max_doublings      = 5
  }
}

# Grant service account permissions
resource "google_project_iam_member" "pubsub_subscriber" {
  project = var.project_id
  role    = "roles/pubsub.subscriber"
  member  = "serviceAccount:${google_service_account.service.email}"
}

resource "google_project_iam_member" "cloudtasks_enqueuer" {
  project = var.project_id
  role    = "roles/cloudtasks.enqueuer"
  member  = "serviceAccount:${google_service_account.service.email}"
}

resource "google_project_iam_member" "cloudrun_invoker" {
  project = var.project_id
  role    = "roles/run.invoker"
  member  = "serviceAccount:${google_service_account.service.email}"
}
```

### variables.tf
```hcl
variable "project_id" {
  description = "GCP Project ID"
  type        = string
}

variable "region" {
  description = "GCP Region"
  type        = string
  default     = "us-central1"
}

variable "service_name" {
  description = "Name of the service"
  type        = string
}

variable "environment" {
  description = "Environment (dev, staging, prod)"
  type        = string
  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "Environment must be dev, staging, or prod."
  }
}

variable "image_tag" {
  description = "Docker image tag"
  type        = string
}

variable "cpu_limit" {
  description = "CPU limit"
  type        = string
  default     = "1000m"
}

variable "memory_limit" {
  description = "Memory limit"
  type        = string
  default     = "512Mi"
}

variable "min_instances" {
  description = "Minimum number of instances"
  type        = number
  default     = 0
}

variable "max_instances" {
  description = "Maximum number of instances"
  type        = number
  default     = 10
}

variable "hasura_endpoint" {
  description = "Hasura GraphQL endpoint"
  type        = string
}

variable "event_topic_name" {
  description = "Pub/Sub topic for events"
  type        = string
}
```

### outputs.tf
```hcl
output "service_url" {
  description = "Cloud Run service URL"
  value       = google_cloud_run_service.service.status[0].url
}

output "service_account_email" {
  description = "Service account email"
  value       = google_service_account.service.email
}

output "tasks_queue_name" {
  description = "Cloud Tasks queue name"
  value       = google_cloud_tasks_queue.tasks.name
}
```

### backend.tf
```hcl
terraform {
  backend "gcs" {
    bucket = "henry-terraform-state"
    prefix = "services/contract-agent"
  }

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 5.0"
    }
  }

  required_version = ">= 1.5"
}
```

## Workspace Pattern

### Initialize Workspaces
```bash
# Initialize Terraform
terraform init

# Create workspaces
terraform workspace new dev
terraform workspace new prod

# Select workspace
terraform workspace select dev
```

### Workspace-Specific Variables

#### dev.tfvars
```hcl
project_id         = "henry-dev-345721"
region            = "us-central1"
service_name      = "contract-agent"
environment       = "dev"
image_tag         = "latest"
cpu_limit         = "1000m"
memory_limit      = "512Mi"
min_instances     = 0
max_instances     = 5
hasura_endpoint   = "https://hasura-dev.henrymeds.com/v1/graphql"
event_topic_name  = "contract-events-dev"
```

#### prod.tfvars
```hcl
project_id         = "henry-prod-345721"
region            = "us-central1"
service_name      = "contract-agent"
environment       = "prod"
image_tag         = "v1.2.3"
cpu_limit         = "2000m"
memory_limit      = "1Gi"
min_instances     = 2
max_instances     = 20
hasura_endpoint   = "https://hasura.henrymeds.com/v1/graphql"
event_topic_name  = "contract-events-prod"
```

## Deployment Pattern

### Local Deployment Script
```bash
#!/bin/bash
# scripts/deploy-local.sh

set -e

NUGET_USER=$1
NUGET_PASS=$2
APPLY_TERRAFORM=${3:-false}

# Authenticate with GCloud
gcloud auth application-default login

# Select workspace
terraform workspace select dev

# Build Docker image
docker build \
  --build-arg NUGET_USER=$NUGET_USER \
  --build-arg NUGET_PASS=$NUGET_PASS \
  -t gcr.io/henry-dev-345721/contract-agent:latest \
  .

# Push to GCR
docker push gcr.io/henry-dev-345721/contract-agent:latest

# Apply Terraform
if [ "$APPLY_TERRAFORM" = "true" ]; then
  cd terraform
  terraform plan -var-file="dev.tfvars" -out=tfplan
  terraform apply tfplan
  cd ..
fi

echo "Deployment complete!"
```

## Cloud Build Integration

### cloudbuild.yaml
```yaml
steps:
  # Run tests
  - name: 'gcr.io/cloud-builders/dotnet'
    args: ['test', '--logger', 'console;verbosity=detailed']

  # Build Docker image
  - name: 'gcr.io/cloud-builders/docker'
    args:
      - 'build'
      - '-t'
      - 'gcr.io/$PROJECT_ID/contract-agent:$SHORT_SHA'
      - '--build-arg'
      - 'NUGET_USER=${_NUGET_USER}'
      - '--build-arg'
      - 'NUGET_PASS=${_NUGET_PASS}'
      - '.'

  # Push Docker image
  - name: 'gcr.io/cloud-builders/docker'
    args: ['push', 'gcr.io/$PROJECT_ID/contract-agent:$SHORT_SHA']

  # Terraform plan
  - name: 'hashicorp/terraform:1.5'
    dir: 'terraform'
    args:
      - 'plan'
      - '-var=image_tag=$SHORT_SHA'
      - '-var-file=${_ENVIRONMENT}.tfvars'
      - '-out=tfplan'

  # Terraform apply
  - name: 'hashicorp/terraform:1.5'
    dir: 'terraform'
    args: ['apply', 'tfplan']

substitutions:
  _ENVIRONMENT: dev
  _NUGET_USER: ${_GITHUB_USER}
  _NUGET_PASS: ${_GITHUB_PAT}

options:
  logging: CLOUD_LOGGING_ONLY
```

## Common Patterns

### 1. Canary Deployment (Non-Prod)
```hcl
resource "google_cloud_run_service" "service" {
  # ... other config ...

  traffic {
    # 90% to stable revision
    percent         = 90
    revision_name   = "service-stable"
  }

  traffic {
    # 10% to canary revision
    percent         = 10
    latest_revision = true
    tag             = "canary"
  }
}
```

### 2. VPC Connector (Private Services)
```hcl
resource "google_vpc_access_connector" "connector" {
  name          = "${var.service_name}-connector"
  region        = var.region
  network       = "default"
  ip_cidr_range = "10.8.0.0/28"
}

resource "google_cloud_run_service" "service" {
  # ... other config ...

  template {
    metadata {
      annotations = {
        "run.googleapis.com/vpc-access-connector" = google_vpc_access_connector.connector.name
        "run.googleapis.com/vpc-access-egress"    = "private-ranges-only"
      }
    }
  }
}
```

### 3. Cloud SQL Connection
```hcl
resource "google_cloud_run_service" "service" {
  # ... other config ...

  template {
    metadata {
      annotations = {
        "run.googleapis.com/cloudsql-instances" = var.cloudsql_instance
      }
    }

    spec {
      containers {
        env {
          name  = "DB_CONNECTION_NAME"
          value = var.cloudsql_instance
        }
      }
    }
  }
}
```

## Best Practices

1. **State Management**: Use GCS backend with locking
2. **Workspaces**: Separate dev/prod with workspace-specific tfvars
3. **Secrets**: Create secrets in Terraform, versions manually
4. **Service Accounts**: Minimal permissions per service
5. **Monitoring**: Enable Cloud Logging and Monitoring
6. **Autoscaling**: Set min/max instances based on traffic
7. **Health Checks**: Implement /health endpoint
8. **Versioning**: Use semantic versioning for image tags

## See Also

- [Cloud Run Docs](https://cloud.google.com/run/docs)
- [Terraform GCP Provider](https://registry.terraform.io/providers/hashicorp/google/latest/docs)
- [Event Handler Pattern](../../backend/event-handler-service/README.md)
