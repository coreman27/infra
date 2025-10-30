# Chargebee Integration Pattern

## Overview

Chargebee is Henry Meds' subscription billing and payment processing platform. This integration handles subscription management, invoicing, payments, and custom fields for business logic.

## Key Concepts

### Subscription Model
- **Subscription**: Customer's recurring payment plan
- **Item Price**: SKU/price plan (e.g., "weightmanagement-monthly")
- **Custom Fields**: Store Henry Meds metadata (cf_contract_id, cf_contract_length)
- **Invoices**: Generated automatically based on billing cycle
- **Payment Methods**: Stored payment information

### Integration Points

1. **Subscription Creation**: When customer signs up
2. **Subscription Updates**: Change plan, update custom fields
3. **Invoice Events**: Payment success/failure webhooks
4. **Cancellation**: End subscription
5. **Reactivation**: Resume canceled subscription

## Architecture

```
Henry Meds Service
    ↓
ChargebeeService.cs
    ↓
Chargebee .NET SDK (ChargeBee.Api)
    ↓
Chargebee REST API
    ↓
Webhook Events
    ↓
ChargebeeEventHandler.cs
    ↓
Business Logic
```

## Configuration

### Environment Variables
```bash
CHARGEBEE_SITE=henrymeds-test  # Site name
CHARGEBEE_API_KEY=***          # API key (Secret Manager)
```

### appsettings.json
```json
{
  "Chargebee": {
    "Site": "henrymeds-test",
    "Environment": "test", // or "live"
    "Timeout": 60000
  }
}
```

## Implementation

### Service Interface
```csharp
public interface IChargebeeService
{
    // Subscriptions
    Task<Subscription> GetSubscriptionAsync(string subscriptionId);
    Task<Subscription> CreateSubscriptionAsync(CreateSubscriptionRequest request);
    Task<Subscription> UpdateSubscriptionAsync(string subscriptionId, object updates);
    Task CancelSubscriptionAsync(string subscriptionId, bool endOfTerm = true);
    Task ReactivateSubscriptionAsync(string subscriptionId);
    
    // Invoices
    Task<Invoice> GetInvoiceAsync(string invoiceId);
    Task<List<Invoice>> GetInvoicesForSubscriptionAsync(string subscriptionId);
    
    // Customers
    Task<Customer> GetCustomerAsync(string customerId);
    Task<Customer> CreateCustomerAsync(CreateCustomerRequest request);
    
    // Payment Methods
    Task<PaymentMethod> GetPaymentMethodAsync(string customerId);
    Task DeletePaymentMethodAsync(string customerId);
    
    // Hosted Pages (checkout)
    Task<HostedPage> GenerateCheckoutPageAsync(CheckoutRequest request);
}
```

### Service Implementation
```csharp
using ChargeBee.Api;
using ChargeBee.Models;

public class ChargebeeService : IChargebeeService
{
    private readonly ILogger<ChargebeeService> _logger;
    private readonly string _site;
    private readonly string _apiKey;

    public ChargebeeService(
        IConfiguration configuration,
        ILogger<ChargebeeService> logger)
    {
        _logger = logger;
        _site = configuration["Chargebee:Site"]!;
        _apiKey = configuration["CHARGEBEE_API_KEY"]!;
        
        // Initialize Chargebee SDK
        ApiConfig.Configure(_site, _apiKey);
    }

    public async Task<Subscription> GetSubscriptionAsync(string subscriptionId)
    {
        try
        {
            var result = await Subscription.Retrieve(subscriptionId).Request();
            return result.SubscriptionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve subscription {Id}", subscriptionId);
            throw;
        }
    }

    public async Task<Subscription> UpdateSubscriptionAsync(
        string subscriptionId,
        object updates)
    {
        try
        {
            // Build update request dynamically
            var request = Subscription.Update(subscriptionId);
            
            // Apply updates via reflection (or strongly typed)
            var properties = updates.GetType().GetProperties();
            foreach (var prop in properties)
            {
                var value = prop.GetValue(updates);
                var paramName = ConvertToSnakeCase(prop.Name);
                
                if (paramName.StartsWith("cf_"))
                {
                    // Custom field
                    request.Param(paramName, value);
                }
                else
                {
                    request.Param(paramName, value);
                }
            }

            var result = await request.Request();
            
            _logger.LogInformation(
                "Updated subscription {Id}: {@Updates}",
                subscriptionId, updates
            );

            return result.SubscriptionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to update subscription {Id}",
                subscriptionId
            );
            throw;
        }
    }

    public async Task<Invoice> GetInvoiceAsync(string invoiceId)
    {
        try
        {
            var result = await Invoice.Retrieve(invoiceId).Request();
            return result.InvoiceResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve invoice {Id}", invoiceId);
            throw;
        }
    }

    private string ConvertToSnakeCase(string input)
    {
        return string.Concat(
            input.Select((x, i) => i > 0 && char.IsUpper(x) 
                ? "_" + x.ToString() 
                : x.ToString())
        ).ToLower();
    }
}
```

## Common Operations

### 1. Create Subscription
```csharp
var subscription = await _chargebee.CreateSubscriptionAsync(new
{
    customer_id = "cus_123",
    plan_id = "weightmanagement-monthly",
    billing_cycles = 6,
    cf_contract_id = "con_456",
    cf_contract_length = 6
});
```

### 2. Update Custom Fields
```csharp
await _chargebee.UpdateSubscriptionAsync(
    "sub_789",
    new
    {
        cf_contract_id = "con_456",
        cf_contract_length = 6
    }
);
```

### 3. Clear Custom Fields
```csharp
await _chargebee.UpdateSubscriptionAsync(
    "sub_789",
    new
    {
        cf_contract_id = (string?)null,
        cf_contract_length = (int?)null
    }
);
```

### 4. Cancel Subscription
```csharp
// Cancel at end of term
await _chargebee.CancelSubscriptionAsync("sub_789", endOfTerm: true);

// Cancel immediately
await _chargebee.CancelSubscriptionAsync("sub_789", endOfTerm: false);
```

### 5. Change Plan
```csharp
await _chargebee.UpdateSubscriptionAsync(
    "sub_789",
    new
    {
        item_price_id = "weightmanagement-annual",
        billing_cycles = 1
    }
);
```

## Webhook Event Handling

### Event Types
- `subscription_created`
- `subscription_activated`
- `subscription_changed`
- `subscription_cancelled`
- `subscription_renewed`
- `invoice_generated`
- `payment_succeeded`
- `payment_failed`

### Event Handler
```csharp
[HttpPost("webhooks/chargebee")]
public async Task<IActionResult> ReceiveWebhook()
{
    using var reader = new StreamReader(Request.Body);
    var body = await reader.ReadToEndAsync();
    
    // Verify webhook signature
    var signature = Request.Headers["X-Chargebee-Signature"];
    if (!VerifyWebhookSignature(body, signature))
    {
        return Unauthorized();
    }

    var webhookEvent = JsonSerializer.Deserialize<ChargebeeWebhook>(body);
    
    switch (webhookEvent.EventType)
    {
        case "payment_succeeded":
            await HandlePaymentSucceededAsync(webhookEvent);
            break;
            
        case "payment_failed":
            await HandlePaymentFailedAsync(webhookEvent);
            break;
            
        case "subscription_cancelled":
            await HandleSubscriptionCancelledAsync(webhookEvent);
            break;
    }

    return Ok();
}
```

## Custom Fields

Henry Meds uses custom fields to store business logic metadata:

### cf_contract_id
- Stores the ID of the active contract
- Set when contract created
- Cleared when contract completes
- Used to link subscription back to contract

### cf_contract_length
- Stores the contract length in months (3 or 6)
- Used for business logic decisions
- Cleared when contract completes

### cf_original_item_price_id
- Stores original price plan before changes
- Used to restore pricing after contract

## Error Handling

```csharp
try
{
    var subscription = await _chargebee.UpdateSubscriptionAsync(...);
}
catch (ApiException ex) when (ex.ApiErrorCode == "resource_not_found")
{
    _logger.LogWarning("Subscription not found: {Id}", subscriptionId);
    return null;
}
catch (ApiException ex) when (ex.ApiErrorCode == "payment_processing")
{
    _logger.LogError(ex, "Payment processing error");
    throw new PaymentException("Payment failed", ex);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected Chargebee error");
    throw;
}
```

## Testing

### Mock Service
```csharp
public class MockChargebeeService : IChargebeeService
{
    private readonly Dictionary<string, Subscription> _subscriptions = new();

    public Task<Subscription> UpdateSubscriptionAsync(
        string subscriptionId,
        object updates)
    {
        if (!_subscriptions.TryGetValue(subscriptionId, out var sub))
        {
            throw new ApiException("resource_not_found");
        }

        // Apply updates
        var subscription = ApplyUpdates(sub, updates);
        _subscriptions[subscriptionId] = subscription;
        
        return Task.FromResult(subscription);
    }
}
```

### Integration Tests
```csharp
[Test]
public async Task UpdateSubscription_SetsCustomFields()
{
    // Arrange
    var service = new ChargebeeService(config, logger);
    var subscriptionId = "test_sub_123";

    // Act
    var result = await service.UpdateSubscriptionAsync(
        subscriptionId,
        new { cf_contract_id = "con_456" }
    );

    // Assert
    Assert.That(result.CustomFields["cf_contract_id"], Is.EqualTo("con_456"));
}
```

## Best Practices

1. **Idempotency**: Use custom fields to track state
2. **Retry Logic**: Implement exponential backoff
3. **Logging**: Log all API calls with parameters
4. **Error Handling**: Catch specific ApiException codes
5. **Webhooks**: Always verify signatures
6. **Testing**: Mock Chargebee for unit tests
7. **Rate Limiting**: Respect API rate limits (100 req/min)
8. **Caching**: Cache frequently accessed data

## See Also

- [Chargebee .NET SDK](https://github.com/chargebee/chargebee-dotnet)
- [Chargebee API Docs](https://apidocs.chargebee.com/docs/api)
- [ETL Service](../../data/etl-service/README.md)
- [Event Handler Pattern](../../backend/event-handler-service/README.md)
