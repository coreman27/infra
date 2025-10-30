# Henry Meds Platform Architecture Templates

This repository contains comprehensive templates and examples of the core architectural patterns used in the Henry Meds platform. This serves as a reference implementation for key integration patterns, service architectures, and infrastructure configurations.

## Repository Structure

```
infra/
├── backend/                          # Backend service templates
│   ├── event-handler-service/        # Hasura event handler pattern (.NET)
│   ├── cloud-run-api/               # Cloud Run API service pattern (.NET)
│   ├── scheduled-job/               # Cloud Scheduler job pattern (.NET)
│   └── common-libraries/            # Shared .NET libraries
├── frontend/                         # Frontend application templates
│   ├── react-vite-portal/           # Portal app pattern (React/Vite/TypeScript)
│   └── common-components/           # Shared React components
├── data/                            # Data layer templates
│   ├── hasura-migrations/           # Hasura migration patterns
│   ├── etl-service/                 # Chargebee ETL pattern (Node.js)
│   └── event-routing/               # Event routing configuration
├── infrastructure/                   # Infrastructure as code
│   ├── terraform/                   # Terraform templates
│   ├── cloud-build/                 # CI/CD pipeline templates
│   └── docker/                      # Dockerfile templates
├── integrations/                     # Third-party service integrations
│   ├── chargebee/                   # Chargebee SDK integration
│   ├── kameleoon/                   # Kameleoon feature flags
│   ├── iterable/                    # Iterable email/messaging
│   ├── zendesk/                     # Zendesk support
│   └── identity-platform/           # Firebase Identity Platform
└── docs/                            # Architecture documentation
    ├── architecture-diagrams/
    ├── event-flows/
    └── integration-guides/
```

## Technology Stack

### Backend
- **Language**: C# / .NET 8.0
- **Hosting**: Google Cloud Run (containerized microservices)
- **Event System**: Hasura Events → Google Pub/Sub → Cloud Run
- **Database**: PostgreSQL with Hasura GraphQL Engine
- **Package Management**: NuGet (GitHub Packages)

### Frontend
- **Language**: TypeScript
- **Framework**: React 18
- **Build Tool**: Vite
- **Package Manager**: Yarn
- **Type Generation**: GraphQL Code Generator
- **Testing**: Vitest (unit), Playwright (E2E)
- **Hosting**: Cloud Run with nginx

### Data & Infrastructure
- **Database**: PostgreSQL (Cloud SQL)
- **GraphQL Layer**: Hasura
- **Event Bus**: Google Pub/Sub
- **Infrastructure**: Terraform
- **CI/CD**: Google Cloud Build
- **Container Registry**: Google Artifact Registry
- **Secrets**: Google Secret Manager

### Key Third-Party Integrations
- **Chargebee**: Subscription billing and payment processing
- **Kameleoon**: Feature flags and A/B testing
- **Iterable**: Email and messaging campaigns
- **Zendesk**: Customer support ticketing
- **Firebase**: Identity and authentication

## Core Architectural Patterns

### 1. Event-Driven Microservices
- Hasura database events trigger Cloud Run services
- Services publish events back to Pub/Sub
- Asynchronous, decoupled architecture

### 2. GraphQL-First Data Layer
- Hasura provides auto-generated GraphQL API
- Row-level security via Hasura permissions
- Migrations managed via Hasura CLI

### 3. Contract-Based Integration
- Services communicate via well-defined contracts
- Event schemas defined in henry-infra/EventTypes
- Shared models in Henry.Common.Dotnet

### 4. Infrastructure as Code
- All infrastructure defined in Terraform
- Declarative configuration
- Environment parity (dev/staging/prod)

### 5. CI/CD Pipeline
- Cloud Build triggers on git push
- Automated testing, building, and deployment
- Canary deployments for non-prod branches

## Getting Started

Each template directory contains:
- **README.md**: Pattern explanation and usage guide
- **Source code**: Fully functional example implementation
- **Tests**: Unit and integration test examples
- **Configuration**: Required config files
- **Deployment**: CI/CD and infrastructure setup

## Use Cases

This repository is useful for:
- **Onboarding**: Understanding Henry Meds architecture
- **Reference**: Looking up integration patterns
- **New Services**: Starting point for new microservices
- **Documentation**: Architectural decision records
- **Knowledge Preservation**: Platform knowledge repository

## Key Design Principles

1. **Event-Driven**: Services react to events, not direct calls
2. **Stateless**: Services are stateless and horizontally scalable
3. **GraphQL-First**: Data access through Hasura GraphQL
4. **Infrastructure as Code**: All infrastructure version controlled
5. **Fail Fast**: Early validation and clear error messages
6. **Idempotency**: Operations can be safely retried
7. **Observability**: Structured logging and monitoring

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                          Frontend Apps                           │
│  (React/Vite TypeScript - Cloud Run + nginx)                    │
│  - customer-portal                                               │
│  - provider-portal                                               │
│  - cct-portal                                                    │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Hasura GraphQL Engine                         │
│  - Auto-generated GraphQL API                                    │
│  - Row-level security                                            │
│  - Database events                                               │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                      PostgreSQL Database                         │
│  - Customer data                                                 │
│  - Healthcare records                                            │
│  - Subscription/contract data                                    │
└─────────────────────────────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                       Event System                               │
│  Hasura Events → Pub/Sub → Cloud Run Services                   │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Backend Services (.NET)                       │
│  - ContractAgent: Contract lifecycle management                 │
│  - ChargebeeAgent: Billing integration                          │
│  - NotificationAgent: Email/SMS notifications                   │
│  - PrescriptionAgent: Pharmacy integration                      │
│  - SubscriptionChange: Subscription management                  │
│  - [30+ microservices]                                          │
└─────────────────────────────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                  Third-Party Integrations                        │
│  - Chargebee: Billing/payments                                  │
│  - Iterable: Email campaigns                                     │
│  - Zendesk: Support tickets                                      │
│  - Firebase: Authentication                                      │
│  - Kameleoon: Feature flags                                      │
└─────────────────────────────────────────────────────────────────┘
```

## Contributing

This is a living documentation repository. When creating new services or discovering new patterns:

1. Document the pattern here
2. Provide working code examples
3. Include tests and configuration
4. Update architecture diagrams

## License

Internal use only - Henry Meds proprietary
