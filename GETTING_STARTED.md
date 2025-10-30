# Getting Started with Henry Meds Platform

This guide will help you understand the Henry Meds platform architecture and get started with development.

## Quick Reference

### Common Tasks

| Task | Command/Location |
|------|------------------|
| Run backend service locally | `./scripts/deploy-local.sh $NUGET_USER $NUGET_PASS false` |
| Build .NET project | `dotnet build` |
| Run .NET tests | `dotnet test` |
| Run frontend dev server | `yarn dev` |
| Generate GraphQL types | `yarn generate-dev` |
| Run frontend tests | `yarn test` |
| Run E2E tests | `yarn test:e2e` |
| Deploy infrastructure | `terraform apply -var-file="dev.tfvars"` |
| View logs | Cloud Logging console or `gcloud logging read` |

### Key Repositories

- **Backend Services**: Henry.ContractAgent, Henry.ChargebeeAgent, etc.
- **Frontend Apps**: customer-portal, provider-portal, cct-portal
- **Data**: HasuraMigrations, chargebee-etl
- **Infrastructure**: henry-infra
- **Libraries**: Henry.Common.Dotnet, iterable-client-dotnet

## Development Workflow

### 1. Backend Service Development

#### Initial Setup
```bash
# Clone repository
git clone https://github.com/HenryMeds/Henry.ContractAgent.git
cd Henry.ContractAgent

# Authenticate with GCloud
gcloud auth application-default login

# Initialize Terraform
cd terraform
terraform init
terraform workspace new dev
terraform workspace select dev
cd ..

# Configure NuGet for private packages
dotnet nuget add source https://nuget.pkg.github.com/HenryMeds/index.json \
  -n github -u $GITHUB_USERNAME -p $GITHUB_PAT \
  --store-password-in-clear-text
```

#### Local Development
```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run tests
dotnet test

# Run locally (with hot reload)
dotnet watch run

# Test HTTP endpoints (use .http files)
# Install REST Client extension in VS Code
# Open requests.http and click "Send Request"
```

#### Deploy to Dev
```bash
# Deploy with Terraform
./scripts/deploy-local.sh $NUGET_USER $NUGET_PASS true

# Or via Cloud Build (push to branch)
git push origin feature/my-feature
```

### 2. Frontend Application Development

#### Initial Setup
```bash
# Clone repository
git clone https://github.com/HenryMeds/customer-portal.git
cd customer-portal

# Install dependencies
yarn install

# Copy environment file
cp env/dev/.env .env.local

# Generate GraphQL types
yarn generate-dev
```

#### Local Development
```bash
# Run dev server (with hot reload)
yarn dev
# Opens https://localhost:3000

# Run tests (watch mode)
yarn test

# Run E2E tests
yarn test:e2e

# Type checking
yarn tsc --noEmit

# Linting
yarn lint
```

#### After GraphQL Schema Changes
```bash
# Regenerate types
yarn generate-dev

# Update components using new types
# Types are in src/types/generated/graphql.ts
```

#### Deploy to Dev
```bash
# Push to branch (Cloud Build auto-deploys)
git push origin feature/my-feature

# Check deployment status
gcloud run services describe customer-portal --region=us-central1

# View logs
gcloud logging read "resource.type=cloud_run_revision AND resource.labels.service_name=customer-portal" --limit=50
```

### 3. Database Migrations (Hasura)

#### Create Migration
```bash
cd HasuraMigrations

# Create new migration
hasura migrate create add_contract_auto_renew_column --database-name default

# Edit migration file
# migrations/default/1234567890_add_contract_auto_renew_column/up.sql
```

Example migration:
```sql
-- up.sql
ALTER TABLE chargebee.contract
ADD COLUMN auto_renew BOOLEAN NOT NULL DEFAULT false;

CREATE INDEX idx_contract_auto_renew 
ON chargebee.contract(auto_renew) 
WHERE auto_renew = true;
```

#### Apply Migration
```bash
# Apply to dev
hasura migrate apply --database-name default --endpoint https://hasura-dev.henrymeds.com

# Track table changes in Hasura
hasura metadata apply --endpoint https://hasura-dev.henrymeds.com
```

#### Update Hasura Metadata
```bash
# Export current metadata (after making changes in console)
hasura metadata export

# Review changes
git diff metadata/

# Apply to environment
hasura metadata apply --endpoint https://hasura-dev.henrymeds.com

# Commit to git
git add metadata/ migrations/
git commit -m "Add auto_renew column to contract table"
```

### 4. Adding New Event Handler

#### 1. Create Handler Class
```csharp
// src/Handlers/ContractEventHandler.cs
public class ContractEventHandler : IEventHandler
{
    private readonly IHasuraService _hasura;
    private readonly IChargebeeService _chargebee;
    
    public async Task HandleAsync(HasuraEventWrapper evt)
    {
        if (evt.Event.Op != "UPDATE") return;
        
        var contract = evt.Event.New.Deserialize<Contract>();
        
        // Your business logic here
    }
}
```

#### 2. Register in DI Container
```csharp
// Program.cs
services.AddScoped<IContractEventHandler, ContractEventHandler>();
```

#### 3. Route in Controller
```csharp
// Controllers/EventController.cs
case "contract":
    var handler = _serviceProvider.GetRequiredService<IContractEventHandler>();
    await handler.HandleAsync(wrapper);
    break;
```

#### 4. Configure Hasura Event Trigger
```yaml
# metadata/databases/default/tables/chargebee_contract.yaml
event_triggers:
  - name: contract_events
    definition:
      enable_manual: false
      update:
        columns:
          - status
          - auto_renew
    webhook_from_env: CONTRACT_EVENT_WEBHOOK_URL
```

#### 5. Write Tests
```csharp
[Test]
public async Task HandleContractUpdate_CallsChargebee()
{
    // Arrange
    var mockChargebee = new Mock<IChargebeeService>();
    var handler = new ContractEventHandler(mockChargebee.Object);

    // Act
    await handler.HandleAsync(testEvent);

    // Assert
    mockChargebee.Verify(x => x.UpdateSubscriptionAsync(
        It.IsAny<string>(),
        It.IsAny<object>()
    ), Times.Once);
}
```

## Common Patterns

### 1. Query Hasura from Backend
```csharp
var result = await _hasura.QueryAsync<Result<Contract>>("""
    query GetContract($id: String!) {
      Results: chargebee_contract_by_pk(id: $id) {
        id
        status
        customer {
          email
          first_name
        }
      }
    }
    """,
    new { id = contractId }
);

var contract = result?.Results;
```

### 2. Update Data via Hasura
```csharp
await _hasura.MutateAsync<Result<Contract>>("""
    mutation UpdateContract($id: String!, $status: String!) {
      Results: update_chargebee_contract_by_pk(
        pk_columns: { id: $id }
        _set: { status: $status, updated_at: "now()" }
      ) {
        id
        status
      }
    }
    """,
    new { id = contractId, status = "completed" }
);
```

### 3. Call Chargebee API
```csharp
await _chargebee.UpdateSubscriptionAsync(
    subscriptionId,
    new
    {
        cf_contract_id = contractId,
        cf_contract_length = 6
    }
);
```

### 4. Publish Event to Pub/Sub
```csharp
await _eventPublisher.PublishAsync("contract.completed", new
{
    contractId = contract.Id,
    customerId = contract.CustomerId,
    completedAt = DateTime.UtcNow
});
```

### 5. Schedule Cloud Task
```csharp
await _cloudTask.ScheduleTaskAsync(
    taskName: $"contract-renewal-{contractId}",
    url: "/api/tasks/auto-renew-contract",
    payload: new { contractId },
    scheduleTime: contract.EndDate.AddDays(-1)
);
```

### 6. Send Email via Iterable
```csharp
await _iterable.TriggerWorkflowAsync(
    workflowId: "contract_renewal_reminder",
    email: customer.Email,
    data: new
    {
        firstName = customer.FirstName,
        contractEndDate = contract.EndDate.ToString("MMMM dd, yyyy")
    }
);
```

## Debugging

### Backend Service Debugging

#### View Logs
```bash
# Cloud Logging
gcloud logging read "resource.type=cloud_run_revision AND resource.labels.service_name=contract-agent" --limit=50 --format=json

# Or in Cloud Console
https://console.cloud.google.com/logs/query
```

#### Debug Locally
```bash
# Run with debugger in VS Code
# Add breakpoints
# Press F5 or Run > Start Debugging

# Or attach to running process
dotnet attach
```

#### Test Hasura Queries
```bash
# Use Hasura Console
https://hasura-dev.henrymeds.com/console

# Or use GraphQL Playground
https://hasura-dev.henrymeds.com/v1/graphql
```

### Frontend Debugging

#### Browser DevTools
```javascript
// Add debugger statement
debugger;

// Console logging
console.log('Contract:', contract);

// React DevTools
// Install browser extension
```

#### Network Requests
```javascript
// Check GraphQL requests in Network tab
// Filter by "graphql"
// Inspect request/response
```

#### E2E Test Debugging
```bash
# Run with UI mode
yarn playwright test --ui

# Debug specific test
yarn playwright test --debug contracts.spec.ts

# View trace
yarn playwright show-trace trace.zip
```

## Troubleshooting

### Common Issues

#### 1. NuGet Restore Fails
```bash
# Error: Unable to load the service index
# Solution: Check GitHub PAT has read:packages scope
dotnet nuget list source
dotnet nuget remove source github
dotnet nuget add source https://nuget.pkg.github.com/HenryMeds/index.json \
  -n github -u $USERNAME -p $PAT --store-password-in-clear-text
```

#### 2. GraphQL Type Generation Fails
```bash
# Error: Authentication required
# Solution: Run gcloud auth
gcloud auth application-default login

# Then retry
yarn generate-dev
```

#### 3. Terraform Apply Fails
```bash
# Error: Workspace not initialized
terraform workspace list
terraform workspace select dev

# Error: Backend not configured
terraform init

# Error: Secret version not found
# Solution: Manually create secret version in Secret Manager
```

#### 4. E2E Tests Fail
```bash
# Check service account impersonation
gcloud config get-value auth/impersonate_service_account

# Unset if needed
gcloud config unset auth/impersonate_service_account

# Check test artifacts in GCS
gsutil ls gs://customer-portal-dev-e2e-artifacts/
```

#### 5. Cloud Run Deploy Fails
```bash
# Check build logs
gcloud builds list --limit=5
gcloud builds log <BUILD_ID>

# Check service status
gcloud run services describe contract-agent --region=us-central1

# Check revisions
gcloud run revisions list --service=contract-agent --region=us-central1
```

## Best Practices

### Code Quality
- Follow C# naming conventions
- Use async/await throughout
- Handle errors gracefully
- Write unit tests (80%+ coverage)
- Use strongly-typed models
- Avoid magic strings/numbers

### Security
- Never commit secrets
- Use Secret Manager for sensitive data
- Validate all input
- Use parameterized queries
- Implement rate limiting
- Log security events

### Performance
- Use async operations
- Batch database queries
- Cache frequently accessed data
- Optimize GraphQL queries
- Monitor response times
- Set appropriate timeouts

### Deployment
- Test locally before deploying
- Run tests in CI/CD
- Deploy to dev first
- Monitor after deployment
- Have rollback plan
- Document changes

## Resources

### Documentation
- [Architecture Overview](./ARCHITECTURE.md)
- [Event Handler Pattern](../backend/event-handler-service/README.md)
- [Hasura Migrations](../data/hasura-migrations/README.md)
- [Frontend Apps](../frontend/react-vite-portal/README.md)
- [Terraform](../infrastructure/terraform/README.md)

### External Resources
- [.NET Documentation](https://docs.microsoft.com/en-us/dotnet/)
- [React Documentation](https://react.dev/)
- [Hasura Documentation](https://hasura.io/docs/)
- [Google Cloud Documentation](https://cloud.google.com/docs)
- [Chargebee API](https://apidocs.chargebee.com/)

### Internal Resources
- Slack: #engineering
- Wiki: Internal confluence
- Runbooks: henry-infra/runbooks/
- On-call rotation: PagerDuty

## Getting Help

1. Check this documentation
2. Search existing code for similar patterns
3. Ask in #engineering Slack channel
4. Review related pull requests
5. Pair with senior engineer
6. Escalate to tech lead

## Next Steps

1. Set up local development environment
2. Clone a repository and run it locally
3. Make a small change and deploy to dev
4. Review architecture documentation
5. Explore integration patterns
6. Start contributing!

Welcome to the Henry Meds engineering team! 🚀
