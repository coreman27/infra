# Iterable Integration Pattern

## Overview

Iterable is Henry Meds' email and messaging platform for customer communications, campaigns, and lifecycle marketing.

## Key Concepts

- **Users**: Customer profiles with attributes
- **Events**: User actions and behaviors
- **Campaigns**: One-time email sends
- **Workflows**: Automated journey flows
- **Templates**: Email/SMS templates
- **Lists**: User segments

## Architecture

```
Henry Meds Service
    ↓
IterableService.cs
    ↓
Henry.Iterable.Client (NuGet package)
    ↓
Iterable REST API
```

## Configuration

```json
{
  "Iterable": {
    "ApiKey": "***",  // From Secret Manager
    "ApiUrl": "https://api.iterable.com/api"
  }
}
```

## Service Interface

```csharp
public interface IIterableService
{
    // Users
    Task UpdateUserAsync(string email, object dataFields);
    Task GetUserAsync(string email);
    Task DeleteUserAsync(string email);
    
    // Events
    Task TrackEventAsync(string email, string eventName, object? data = null);
    Task TrackPurchaseAsync(string email, decimal total, object[] items);
    
    // Campaigns
    Task TriggerCampaignAsync(string campaignId, string email, object? data = null);
    
    // Workflows
    Task TriggerWorkflowAsync(string workflowId, string email, object? data = null);
}
```

## Implementation

```csharp
using Henry.Iterable.Client;

public class IterableService : IIterableService
{
    private readonly IterableClient _client;
    private readonly ILogger<IterableService> _logger;

    public IterableService(
        IConfiguration configuration,
        ILogger<IterableService> logger)
    {
        var apiKey = configuration["ITERABLE_API_KEY"]!;
        _client = new IterableClient(apiKey);
        _logger = logger;
    }

    public async Task UpdateUserAsync(string email, object dataFields)
    {
        try
        {
            await _client.Users.UpdateAsync(new UpdateUserRequest
            {
                Email = email,
                DataFields = dataFields,
                MergeNestedObjects = true
            });

            _logger.LogInformation(
                "Updated Iterable user: {Email}",
                email
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Iterable user: {Email}", email);
            throw;
        }
    }

    public async Task TrackEventAsync(string email, string eventName, object? data = null)
    {
        try
        {
            await _client.Events.TrackAsync(new TrackEventRequest
            {
                Email = email,
                EventName = eventName,
                DataFields = data,
                CreatedAt = DateTime.UtcNow
            });

            _logger.LogInformation(
                "Tracked event: {EventName} for {Email}",
                eventName, email
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to track event: {EventName} for {Email}",
                eventName, email
            );
            throw;
        }
    }

    public async Task TriggerWorkflowAsync(
        string workflowId,
        string email,
        object? data = null)
    {
        try
        {
            await _client.Workflows.TriggerAsync(new TriggerWorkflowRequest
            {
                WorkflowId = workflowId,
                Email = email,
                DataFields = data
            });

            _logger.LogInformation(
                "Triggered workflow {WorkflowId} for {Email}",
                workflowId, email
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to trigger workflow {WorkflowId} for {Email}",
                workflowId, email
            );
            throw;
        }
    }
}
```

## Common Operations

### 1. Update User Profile
```csharp
await _iterable.UpdateUserAsync(
    email: "customer@example.com",
    dataFields: new
    {
        firstName = "John",
        lastName = "Doe",
        customerId = "cus_123",
        treatmentType = "weightmanagement",
        subscriptionStatus = "active",
        contractEndDate = "2024-06-30"
    }
);
```

### 2. Track Custom Event
```csharp
await _iterable.TrackEventAsync(
    email: "customer@example.com",
    eventName: "contract_activated",
    data: new
    {
        contractId = "con_456",
        startDate = "2024-01-01",
        endDate = "2024-06-30",
        lengthMonths = 6
    }
);
```

### 3. Track Purchase
```csharp
await _iterable.TrackPurchaseAsync(
    email: "customer@example.com",
    total: 299.99m,
    items: new[]
    {
        new
        {
            id = "weightmanagement-6month",
            name = "Weight Management - 6 Month Contract",
            price = 299.99m,
            quantity = 1
        }
    }
);
```

### 4. Trigger Workflow
```csharp
// Welcome workflow after signup
await _iterable.TriggerWorkflowAsync(
    workflowId: "12345",
    email: "customer@example.com",
    data: new
    {
        firstName = "John",
        treatmentType = "Weight Management"
    }
);
```

### 5. Trigger Campaign
```csharp
// One-time email campaign
await _iterable.TriggerCampaignAsync(
    campaignId: "67890",
    email: "customer@example.com",
    data: new
    {
        contractEndDate = "June 30, 2024",
        renewalLink = "https://app.henrymeds.com/renew"
    }
);
```

## Event Patterns

### Contract Events
```csharp
// Contract created
await _iterable.TrackEventAsync(email, "contract_created", new
{
    contractId,
    treatmentType,
    lengthMonths,
    startDate,
    endDate
});

// Contract activated
await _iterable.TrackEventAsync(email, "contract_activated", new
{
    contractId,
    activationDate
});

// Contract completing soon (trigger renewal workflow)
await _iterable.TriggerWorkflowAsync(
    workflowId: "contract_renewal_reminder",
    email: email,
    data: new
    {
        contractId,
        endDate,
        renewalLink = $"https://app.henrymeds.com/renew/{contractId}"
    }
);

// Contract renewed
await _iterable.TrackEventAsync(email, "contract_renewed", new
{
    oldContractId,
    newContractId,
    renewalDate
});
```

### Subscription Events
```csharp
// Subscription created
await _iterable.TrackEventAsync(email, "subscription_created", new
{
    subscriptionId,
    planName,
    price
});

// Payment succeeded
await _iterable.TrackEventAsync(email, "payment_succeeded", new
{
    invoiceId,
    amount,
    date
});

// Payment failed
await _iterable.TriggerWorkflowAsync(
    workflowId: "payment_failed_recovery",
    email: email,
    data: new
    {
        invoiceId,
        amount,
        retryDate
    }
);
```

## User Profile Sync Pattern

```csharp
public class CustomerEventHandler : IEventHandler
{
    private readonly IIterableService _iterable;

    public async Task HandleAsync(HasuraEvent<Customer> evt)
    {
        var customer = evt.Event.Data.New;

        // Sync customer profile to Iterable
        await _iterable.UpdateUserAsync(
            email: customer.Email,
            dataFields: new
            {
                customerId = customer.Id,
                firstName = customer.FirstName,
                lastName = customer.LastName,
                phoneNumber = customer.PhoneNumber,
                dateOfBirth = customer.DateOfBirth,
                state = customer.State,
                createdAt = customer.CreatedAt,
                
                // Custom fields
                treatmentType = customer.TreatmentType,
                subscriptionStatus = customer.SubscriptionStatus,
                hasActiveContract = customer.HasActiveContract
            }
        );
    }
}
```

## Workflow Integration

### Common Workflows

1. **Welcome Series**: After customer signs up
2. **Onboarding**: Guide through first steps
3. **Renewal Reminders**: Before contract ends
4. **Win-Back**: After cancellation
5. **Payment Failed**: Recovery flow
6. **Engagement**: Re-activate inactive users

### Trigger Example
```csharp
public async Task HandleContractEndingAsync(Contract contract)
{
    var customer = await _hasura.GetCustomerAsync(contract.CustomerId);
    
    // Trigger 30 days before end
    if (contract.EndDate.AddDays(-30) == DateTime.Today)
    {
        await _iterable.TriggerWorkflowAsync(
            workflowId: "contract_renewal_30_days",
            email: customer.Email,
            data: new
            {
                firstName = customer.FirstName,
                contractEndDate = contract.EndDate.ToString("MMMM dd, yyyy"),
                currentPlan = contract.PlanName,
                renewalLink = $"https://app.henrymeds.com/renew/{contract.Id}"
            }
        );
    }
}
```

## Testing

### Mock Service
```csharp
public class MockIterableService : IIterableService
{
    public List<(string Email, string EventName, object? Data)> TrackedEvents { get; } = new();

    public Task TrackEventAsync(string email, string eventName, object? data = null)
    {
        TrackedEvents.Add((email, eventName, data));
        return Task.CompletedTask;
    }
}

// Test
[Test]
public async Task ContractActivated_TracksIterableEvent()
{
    // Arrange
    var iterable = new MockIterableService();
    var handler = new ContractEventHandler(iterable, ...);

    // Act
    await handler.HandleContractActivatedAsync(contract);

    // Assert
    Assert.That(iterable.TrackedEvents, Has.Count.EqualTo(1));
    Assert.That(iterable.TrackedEvents[0].EventName, Is.EqualTo("contract_activated"));
}
```

## Best Practices

1. **Email Validation**: Always validate email format
2. **Idempotency**: Use dataFields with IDs for deduplication
3. **Error Handling**: Retry transient failures
4. **Data Privacy**: Don't send PHI in event data
5. **Testing**: Mock service for unit tests
6. **Rate Limiting**: Respect API rate limits
7. **Batching**: Batch user updates when possible

## See Also

- [Iterable API Docs](https://api.iterable.com/api/docs)
- [Event Handler Pattern](../../backend/event-handler-service/README.md)
- [Customer Events](../event-flows/customer-lifecycle.md)
