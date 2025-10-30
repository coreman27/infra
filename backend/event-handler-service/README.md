# Event Handler Service Pattern

## Overview

Event handler services are .NET microservices that respond to Hasura database events via Google Pub/Sub. This is the primary pattern for asynchronous business logic in the Henry Meds platform.

## Architecture

```
Database Change
    ↓
Hasura Event Trigger
    ↓
Google Pub/Sub Topic
    ↓
Cloud Run Service (this pattern)
    ↓
Business Logic + External APIs
    ↓
Database Updates via Hasura GraphQL
```

## Key Characteristics

- **Trigger**: Hasura database events (INSERT, UPDATE, DELETE)
- **Transport**: Google Pub/Sub
- **Hosting**: Google Cloud Run
- **Idempotency**: Events can be retried safely
- **Authentication**: Service account with Pub/Sub subscription
- **Response**: Always return 200 to acknowledge message

## Project Structure

```
EventHandlerService/
├── src/
│   ├── Controllers/
│   │   └── EventController.cs          # Receives Pub/Sub messages
│   ├── Handlers/
│   │   ├── IEventHandler.cs            # Handler interface
│   │   └── ContractEventHandler.cs     # Example handler
│   ├── Services/
│   │   ├── IHasuraService.cs           # GraphQL client
│   │   ├── HasuraService.cs
│   │   ├── IChargebeeService.cs        # External API
│   │   └── ChargebeeService.cs
│   ├── Models/
│   │   ├── HasuraEvent.cs              # Event wrapper
│   │   └── Contract.cs                 # Domain model
│   └── Program.cs
├── terraform/
│   ├── main.tf                         # Cloud Run + Pub/Sub
│   └── variables.tf
├── scripts/
│   └── deploy-local.sh
├── Dockerfile
├── cloudbuild.yaml
└── EventHandlerService.csproj
```

## Core Components

### 1. Event Controller
Receives Pub/Sub messages and routes to appropriate handler.

### 2. Event Handler
Implements business logic for specific event types.

### 3. Hasura Service
Client for querying/mutating data via GraphQL.

### 4. External Service Integrations
Chargebee, Iterable, Zendesk, etc.

### 5. Event Publisher
Publishes new events to Pub/Sub for other services.

## Event Flow

1. **Database Change**: Row inserted/updated in PostgreSQL
2. **Hasura Trigger**: Configured event trigger fires
3. **Pub/Sub Message**: Event published to topic
4. **Cloud Run Invocation**: Service receives POST request
5. **Event Parsing**: Extract event data and type
6. **Handler Routing**: Route to appropriate handler
7. **Business Logic**: Execute domain logic
8. **Side Effects**: Call external APIs, update database
9. **Event Publishing**: Publish downstream events
10. **Acknowledgment**: Return 200 to Pub/Sub

## Implementation Guidelines

### Idempotency
- Use event IDs to deduplicate
- Check current state before mutations
- Use database constraints to prevent duplicates

### Error Handling
- Log all errors with structured logging
- Return 200 even on business logic errors (prevents retry)
- Return 5xx only for transient infrastructure errors
- Use dead letter queue for failed messages

### Performance
- Keep handlers fast (<60s timeout)
- Use async/await throughout
- Batch operations when possible
- Cache frequently accessed data

### Security
- Validate event signatures
- Use service accounts with minimal permissions
- Never log sensitive data (PII, PHI)
- Encrypt data at rest and in transit

## Example: Contract Event Handler

```csharp
public class ContractEventHandler : IEventHandler<Contract>
{
    private readonly IHasuraService _hasura;
    private readonly IChargebeeService _chargebee;
    private readonly ILogger<ContractEventHandler> _logger;

    public async Task HandleAsync(HasuraEvent<Contract> evt)
    {
        var contract = evt.Event.Data.New;
        
        // Idempotency check
        if (contract.Status != ContractStatus.Active)
        {
            _logger.LogInformation("Contract {Id} not active, skipping", contract.Id);
            return;
        }

        // Business logic
        var subscription = await _chargebee.UpdateSubscriptionAsync(
            contract.SubscriptionId,
            new { cf_contract_id = contract.Id }
        );

        // Update database
        await _hasura.UpdateContractAsync(contract.Id, new {
            chargebee_subscription_id = subscription.Id
        });

        // Publish event
        await _eventPublisher.PublishAsync("contract.activated", contract);
        
        _logger.LogInformation("Contract {Id} activated", contract.Id);
    }
}
```

## Testing

### Unit Tests
- Mock all external dependencies
- Test handler logic in isolation
- Verify error handling paths

### Integration Tests
- Use testcontainers for PostgreSQL
- Mock external APIs
- Test event deserialization

### E2E Tests
- Deploy to dev environment
- Trigger database changes
- Verify side effects

## Deployment

### Local Development
```bash
./scripts/deploy-local.sh $NUGET_USER $NUGET_PASS true
```

### CI/CD (Cloud Build)
```yaml
steps:
  - name: 'gcr.io/cloud-builders/dotnet'
    args: ['test']
  - name: 'gcr.io/cloud-builders/docker'
    args: ['build', '-t', 'gcr.io/$PROJECT_ID/service:$SHORT_SHA', '.']
  - name: 'gcr.io/cloud-builders/docker'
    args: ['push', 'gcr.io/$PROJECT_ID/service:$SHORT_SHA']
  - name: 'hashicorp/terraform'
    dir: 'terraform'
    args: ['apply', '-auto-approve']
```

## Monitoring

- **Cloud Logging**: Structured JSON logs
- **Cloud Monitoring**: CPU, memory, request count
- **Error Reporting**: Automatic error tracking
- **Pub/Sub Metrics**: Message age, undelivered count

## Common Patterns

### 1. Event Deduplication
```csharp
if (await _cache.ExistsAsync($"event:{evt.Id}"))
{
    return; // Already processed
}
await _cache.SetAsync($"event:{evt.Id}", true, TimeSpan.FromHours(24));
```

### 2. Retry with Backoff
```csharp
var policy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(3, retryAttempt => 
        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

await policy.ExecuteAsync(() => _chargebee.UpdateSubscriptionAsync(...));
```

### 3. Dead Letter Queue
```yaml
# terraform/main.tf
resource "google_pubsub_subscription" "dead_letter" {
  name  = "contract-events-dlq"
  topic = google_pubsub_topic.contract_events.name
  
  dead_letter_policy {
    dead_letter_topic     = google_pubsub_topic.dlq.id
    max_delivery_attempts = 5
  }
}
```

## See Also

- [Hasura Event Triggers](../data/hasura-migrations/README.md)
- [Event Publishing](../data/event-routing/README.md)
- [Chargebee Integration](../integrations/chargebee/README.md)
- [Infrastructure Setup](../infrastructure/terraform/README.md)
