# Henry Meds Platform Architecture

## System Overview

Henry Meds is a telehealth platform built on Google Cloud Platform (GCP) using event-driven microservices architecture. The platform handles customer subscriptions, contracts, billing, clinical workflows, and customer support.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                          CLIENT LAYER                                │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐        │
│  │ Customer Portal│  │ Provider Portal│  │  CCT Portal    │        │
│  │ (React/Vite)   │  │ (React/Vite)   │  │ (React/Vite)   │        │
│  └────────┬───────┘  └────────┬───────┘  └────────┬───────┘        │
└───────────┼──────────────────┼──────────────────┼────────────────────┘
            │                  │                  │
            │                  │                  │
┌───────────▼──────────────────▼──────────────────▼────────────────────┐
│                        API GATEWAY LAYER                              │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │              Hasura GraphQL Engine                            │   │
│  │  - Auto-generated GraphQL API                                 │   │
│  │  - JWT Authentication (Firebase)                              │   │
│  │  - Row-level security                                         │   │
│  │  - Event triggers                                             │   │
│  └────────┬─────────────────────────────────────────────────────┘   │
└───────────┼──────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        DATABASE LAYER                                │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │              PostgreSQL (Cloud SQL)                           │   │
│  │  Schemas:                                                     │   │
│  │  - chargebee: Subscription, invoice, customer data            │   │
│  │  - public: Contracts, treatments, customer records            │   │
│  │  - healthcare: Clinical data, prescriptions                   │   │
│  └──────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        EVENT BUS LAYER                               │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │              Google Pub/Sub                                   │   │
│  │  Topics:                                                      │   │
│  │  - contract-events: Contract lifecycle events                 │   │
│  │  - invoice-events: Payment and invoice events                 │   │
│  │  - customer-events: Customer profile changes                  │   │
│  │  - prescription-events: Prescription fulfillment              │   │
│  └────────┬─────────────────────────────────────────────────────┘   │
└───────────┼──────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    MICROSERVICES LAYER                               │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │              Cloud Run Services (.NET)                       │    │
│  │                                                              │    │
│  │  Core Services:                                              │    │
│  │  - ContractAgent: Contract lifecycle automation             │    │
│  │  - SubscriptionChange: Subscription management               │    │
│  │  - ChargebeeAgent: Billing integration                       │    │
│  │  - NotificationAgent: Email/SMS notifications                │    │
│  │  - PrescriptionAgent: Pharmacy integration                   │    │
│  │                                                              │    │
│  │  Support Services:                                           │    │
│  │  - ReferralAgent: Referral program management                │    │
│  │  - ZendeskAgent: Support ticket integration                  │    │
│  │  - EventRouting: Event routing and transformation            │    │
│  │  - IdentityPlatform: User management                         │    │
│  │  - DataDeletionService: GDPR compliance                      │    │
│  └──────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    INTEGRATION LAYER                                 │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐             │
│  │  Chargebee   │  │   Iterable   │  │   Zendesk    │             │
│  │  (Billing)   │  │  (Email/SMS) │  │  (Support)   │             │
│  └──────────────┘  └──────────────┘  └──────────────┘             │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐             │
│  │  Kameleoon   │  │   Firebase   │  │  Chargebee   │             │
│  │(Feature Flags│  │    (Auth)    │  │    ETL       │             │
│  └──────────────┘  └──────────────┘  └──────────────┘             │
└─────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    SCHEDULED JOBS LAYER                              │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │              Cloud Scheduler + Cloud Tasks                    │   │
│  │                                                              │   │
│  │  - Chargebee ETL: Every 1 minute                             │   │
│  │  - Contract Auto-Renewal: Daily check                        │   │
│  │  - Payment Retry: Based on schedule                          │   │
│  │  - Data Cleanup: Weekly                                      │   │
│  └──────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

## Core Architecture Principles

### 1. Event-Driven Architecture
- Database changes trigger Hasura events
- Events published to Pub/Sub
- Microservices subscribe and react
- Loose coupling between services
- Asynchronous processing

### 2. GraphQL-First Data Access
- Single GraphQL endpoint for all data
- Auto-generated from database schema
- Type-safe client generation
- Row-level security via permissions
- Real-time subscriptions (if needed)

### 3. Microservices
- Single responsibility per service
- Independent deployment
- Horizontal scaling
- Cloud Run for stateless containers
- Service-to-service via Hasura GraphQL

### 4. Infrastructure as Code
- All infrastructure in Terraform
- Version controlled
- Reproducible environments
- Declarative configuration

### 5. CI/CD Automation
- Cloud Build pipelines
- Automated testing
- Container builds
- Infrastructure provisioning
- Deployment on merge

## Data Flow Patterns

### 1. Contract Lifecycle Flow

```
Customer signs up
    ↓
Frontend → Hasura → Insert contract (status: future)
    ↓
Hasura Event Trigger → Pub/Sub
    ↓
ContractAgent receives event
    ↓
- Updates Chargebee subscription (cf_contract_id)
- Schedules auto-renewal task (Cloud Tasks)
- Publishes contract.created event
    ↓
Iterable receives event → Welcome email
Zendesk receives event → Create customer record
```

### 2. Invoice Payment Flow

```
Chargebee processes payment
    ↓
Chargebee ETL syncs invoice to DB (every 1 minute)
    ↓
Hasura detects invoice update
    ↓
Hasura Event Trigger → Pub/Sub
    ↓
InvoiceEventHandler receives event
    ↓
If payment_succeeded:
  - Track event in Iterable
  - Update customer lifetime value
    ↓
If payment_failed:
  - Create Zendesk ticket
  - Trigger payment recovery workflow (Iterable)
  - Schedule retry (Cloud Tasks)
```

### 3. Contract Auto-Renewal Flow

```
Cloud Tasks scheduled task fires (EndDate - 1 day)
    ↓
ContractAgent → HandleAutoRenewContractAsync
    ↓
Query Hasura for contract and subscription
    ↓
Check if treatment has upfront payment option
    ↓
If upfront option exists:
  - Mark contract as completed
  - Reset subscription to monthly (Chargebee)
  - Clear contract metadata
    ↓
If no upfront option:
  - Create new 6-month contract
  - Mark old contract as renewed
  - Update Chargebee with new contract ID
    ↓
Publish contract.renewed or contract.completed event
    ↓
Iterable sends renewal confirmation email
```

## Service Communication Patterns

### 1. Event Publishing (Pub/Sub)
```csharp
// ContractAgent publishes event
await _eventPublisher.PublishAsync("contract.activated", new
{
    contractId = contract.Id,
    customerId = contract.CustomerId,
    startDate = contract.StartDate
});

// NotificationAgent subscribes and sends email
public async Task HandleContractActivated(ContractActivatedEvent evt)
{
    await _iterable.TriggerWorkflowAsync(
        "contract_activated",
        evt.CustomerId
    );
}
```

### 2. GraphQL Queries
```csharp
// Service queries Hasura
var result = await _hasura.QueryAsync<ContractResult>("""
    query GetContract($id: String!) {
      contract: chargebee_contract_by_pk(id: $id) {
        id
        status
        customer { email }
      }
    }
    """,
    new { id = contractId }
);
```

### 3. External API Calls
```csharp
// Service calls Chargebee
await _chargebee.UpdateSubscriptionAsync(
    subscriptionId,
    new { cf_contract_id = contractId }
);

// Service calls Iterable
await _iterable.TrackEventAsync(
    email,
    "contract_created",
    new { contractId }
);
```

## Security Architecture

### 1. Authentication
- Frontend: Firebase Auth (JWT tokens)
- Backend: Service account authentication
- Hasura: JWT verification + custom claims

### 2. Authorization
- Hasura: Row-level security via permissions
- Roles: user, cct, provider, service, admin
- Column-level access control

### 3. Data Protection
- Encryption at rest (Cloud SQL, Secret Manager)
- Encryption in transit (TLS everywhere)
- Secrets in Secret Manager (never in code)
- PHI/PII compliance

### 4. Network Security
- VPC for private services
- Cloud Run with authentication required
- No public database access
- Service accounts with minimal permissions

## Deployment Architecture

### Environments

1. **Development**
   - Project: henry-dev-345721
   - Auto-deploy on push to non-main branches
   - Canary deployments (10% traffic)
   - Lower resource limits

2. **Staging**
   - Project: henry-dev-345721
   - Deploy on merge to develop
   - Production-like data (anonymized)
   - Full E2E test suite

3. **Production**
   - Project: henry-prod-345721
   - Deploy on merge to main
   - Blue/green deployment
   - Higher resource limits
   - Monitoring and alerts

### CI/CD Pipeline

```
Code Push → GitHub
    ↓
Cloud Build Trigger
    ↓
1. Run unit tests
2. Build Docker image
3. Push to Artifact Registry
4. Terraform plan
5. Terraform apply (infrastructure)
6. Deploy to Cloud Run
7. Run E2E tests
8. Notify team (Slack)
```

## Monitoring & Observability

### 1. Logging
- Structured JSON logs (all services)
- Cloud Logging
- Log-based metrics
- Error Reporting integration

### 2. Metrics
- Cloud Monitoring
- Service-level indicators (SLIs)
- Request latency, error rate, throughput
- Custom business metrics

### 3. Alerting
- Payment failures
- ETL job failures
- High error rates
- Service downtime
- Database connection issues

### 4. Tracing
- Cloud Trace
- Request flow across services
- Performance bottleneck identification

## Scaling Strategy

### 1. Horizontal Scaling
- Cloud Run autoscaling (0-100 instances)
- Based on CPU and request concurrency
- Stateless services enable infinite scale

### 2. Database Scaling
- Cloud SQL automatic storage increase
- Read replicas for read-heavy queries
- Connection pooling (PgBouncer)

### 3. Caching
- Application-level caching (Redis future)
- CDN for static assets
- GraphQL query result caching

## Disaster Recovery

### 1. Backups
- Database: Automated daily backups (7-day retention)
- Point-in-time recovery (PITR)
- Configuration in version control

### 2. Monitoring
- Uptime checks
- Error rate alerts
- Data integrity checks

### 3. Incident Response
- On-call rotation
- Runbooks for common issues
- Post-mortem process

## Technology Choices

### Why Cloud Run?
- Serverless (no infrastructure management)
- Auto-scaling (including to zero)
- Pay per use
- Container-based (any language)
- Fast cold starts

### Why Hasura?
- Auto-generated GraphQL API
- No backend code for CRUD
- Built-in permissions
- Event triggers
- Real-time capabilities

### Why Pub/Sub?
- At-least-once delivery
- Decoupling of services
- Async processing
- Scalable message queue
- Dead letter queues

### Why PostgreSQL?
- ACID compliance
- Rich query capabilities
- JSON support (JSONB)
- Mature ecosystem
- Hasura integration

### Why Terraform?
- Infrastructure as code
- Version controlled
- Multi-environment support
- Provider ecosystem
- State management

## Future Enhancements

### Planned
- Redis caching layer
- GraphQL federation
- Real-time notifications (WebSockets)
- Machine learning models (vertex AI)
- Mobile applications

### Under Consideration
- Kubernetes (GKE) for complex workloads
- Service mesh (Istio)
- Event sourcing patterns
- CQRS for read-heavy operations

## Key Takeaways

1. **Event-Driven**: All business logic triggered by database events
2. **GraphQL-First**: Single API for all data access
3. **Microservices**: Small, focused, independently deployable
4. **Serverless**: Cloud Run for automatic scaling
5. **Infrastructure as Code**: Everything in Terraform
6. **Security**: Authentication, authorization, encryption everywhere
7. **Monitoring**: Comprehensive logging and alerting
8. **CI/CD**: Automated testing and deployment

## See Also

- [Event Handler Pattern](../backend/event-handler-service/README.md)
- [Hasura Migrations](../data/hasura-migrations/README.md)
- [Terraform Infrastructure](../infrastructure/terraform/README.md)
- [Frontend Applications](../frontend/react-vite-portal/README.md)
- [Integration Patterns](../integrations/)
