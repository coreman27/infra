# Zendesk Integration Pattern

## Overview

Zendesk is Henry Meds' customer support platform for ticket management, live chat, and customer service operations.

## Key Concepts

- **Tickets**: Support requests from customers
- **Users**: Customer profiles in Zendesk
- **Organizations**: Company/account grouping
- **Tags**: Categorization and routing
- **Custom Fields**: Additional ticket metadata

## Architecture

```
Henry Meds Service
    ↓
ZendeskService.cs
    ↓
Zendesk .NET SDK
    ↓
Zendesk API
```

## Configuration

```json
{
  "Zendesk": {
    "Subdomain": "henrymeds",
    "Email": "api@henrymeds.com",
    "ApiToken": "***"
  }
}
```

## Service Interface

```csharp
public interface IZendeskService
{
    // Tickets
    Task<Ticket> CreateTicketAsync(CreateTicketRequest request);
    Task<Ticket> GetTicketAsync(long ticketId);
    Task UpdateTicketAsync(long ticketId, object updates);
    Task AddCommentAsync(long ticketId, string comment, bool isPublic = true);
    
    // Users
    Task<User> CreateOrUpdateUserAsync(string email, object userFields);
    Task<User> GetUserByEmailAsync(string email);
    
    // Organizations
    Task<Organization> GetOrganizationAsync(long orgId);
}
```

## Implementation

```csharp
using ZendeskApi.Client;
using ZendeskApi.Client.Models;

public class ZendeskService : IZendeskService
{
    private readonly IZendeskClient _client;
    private readonly ILogger<ZendeskService> _logger;

    public ZendeskService(
        IConfiguration configuration,
        ILogger<ZendeskService> logger)
    {
        var subdomain = configuration["Zendesk:Subdomain"]!;
        var email = configuration["Zendesk:Email"]!;
        var apiToken = configuration["ZENDESK_API_TOKEN"]!;

        _client = new ZendeskClient(subdomain, email, apiToken);
        _logger = logger;
    }

    public async Task<Ticket> CreateTicketAsync(CreateTicketRequest request)
    {
        try
        {
            var ticket = new Ticket
            {
                Subject = request.Subject,
                Comment = new Comment
                {
                    Body = request.Description,
                    Public = true
                },
                Priority = request.Priority,
                Tags = request.Tags,
                CustomFields = request.CustomFields,
                Requester = new Requester
                {
                    Email = request.RequesterEmail,
                    Name = request.RequesterName
                }
            };

            var result = await _client.Tickets.CreateTicketAsync(ticket);

            _logger.LogInformation(
                "Created Zendesk ticket {TicketId} for {Email}",
                result.Id, request.RequesterEmail
            );

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Zendesk ticket");
            throw;
        }
    }

    public async Task AddCommentAsync(long ticketId, string comment, bool isPublic = true)
    {
        try
        {
            await _client.Tickets.UpdateTicketAsync(new Ticket
            {
                Id = ticketId,
                Comment = new Comment
                {
                    Body = comment,
                    Public = isPublic
                }
            });

            _logger.LogInformation(
                "Added comment to Zendesk ticket {TicketId}",
                ticketId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to add comment to ticket {TicketId}",
                ticketId
            );
            throw;
        }
    }

    public async Task<User> CreateOrUpdateUserAsync(string email, object userFields)
    {
        try
        {
            // Check if user exists
            var existingUser = await GetUserByEmailAsync(email);

            if (existingUser != null)
            {
                // Update existing
                var updated = await _client.Users.UpdateUserAsync(new User
                {
                    Id = existingUser.Id,
                    UserFields = userFields
                });
                return updated;
            }
            else
            {
                // Create new
                var created = await _client.Users.CreateUserAsync(new User
                {
                    Email = email,
                    UserFields = userFields
                });
                return created;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create/update Zendesk user: {Email}", email);
            throw;
        }
    }
}

public class CreateTicketRequest
{
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RequesterEmail { get; set; } = string.Empty;
    public string? RequesterName { get; set; }
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    public List<string>? Tags { get; set; }
    public Dictionary<string, object>? CustomFields { get; set; }
}
```

## Common Operations

### 1. Create Support Ticket
```csharp
var ticket = await _zendesk.CreateTicketAsync(new CreateTicketRequest
{
    Subject = "Issue with contract renewal",
    Description = "Customer unable to renew contract through portal",
    RequesterEmail = "customer@example.com",
    RequesterName = "John Doe",
    Priority = TicketPriority.High,
    Tags = new List<string> { "contract", "renewal", "portal" },
    CustomFields = new Dictionary<string, object>
    {
        ["customer_id"] = "cus_123",
        ["contract_id"] = "con_456",
        ["treatment_type"] = "weightmanagement"
    }
});
```

### 2. Add Internal Note
```csharp
await _zendesk.AddCommentAsync(
    ticketId: 12345,
    comment: "Contract team: Customer's contract expires in 3 days. Auto-renewal disabled.",
    isPublic: false  // Internal note
);
```

### 3. Update User Profile
```csharp
await _zendesk.CreateOrUpdateUserAsync(
    email: "customer@example.com",
    userFields: new
    {
        customer_id = "cus_123",
        subscription_status = "active",
        treatment_type = "weightmanagement",
        contract_end_date = "2024-06-30",
        lifetime_value = 1799.94
    }
);
```

## Event-Driven Ticket Creation

### Payment Failed Ticket
```csharp
public class PaymentFailedEventHandler : IEventHandler
{
    private readonly IZendeskService _zendesk;
    private readonly IHasuraService _hasura;

    public async Task HandleAsync(HasuraEvent<Invoice> evt)
    {
        var invoice = evt.Event.Data.New;

        if (invoice.Status != "payment_failed")
            return;

        var customer = await _hasura.GetCustomerAsync(invoice.CustomerId);

        // Create high-priority ticket for billing team
        await _zendesk.CreateTicketAsync(new CreateTicketRequest
        {
            Subject = $"Payment Failed - Invoice {invoice.Id}",
            Description = $@"
                Customer: {customer.Email}
                Invoice ID: {invoice.Id}
                Amount: ${invoice.AmountDue / 100:F2}
                Attempt Date: {invoice.AttemptDate}
                
                Action needed: Contact customer to resolve payment issue.
            ",
            RequesterEmail = customer.Email,
            RequesterName = $"{customer.FirstName} {customer.LastName}",
            Priority = TicketPriority.High,
            Tags = new List<string> { "payment_failed", "billing", "auto_created" },
            CustomFields = new Dictionary<string, object>
            {
                ["customer_id"] = customer.Id,
                ["invoice_id"] = invoice.Id,
                ["subscription_id"] = invoice.SubscriptionId
            }
        });
    }
}
```

### Contract Cancellation Ticket
```csharp
public async Task HandleContractCancellationAsync(Contract contract)
{
    var customer = await _hasura.GetCustomerAsync(contract.CustomerId);

    await _zendesk.CreateTicketAsync(new CreateTicketRequest
    {
        Subject = $"Contract Cancelled - {customer.Email}",
        Description = $@"
            Customer has cancelled their contract.
            
            Customer ID: {customer.Id}
            Contract ID: {contract.Id}
            Treatment: {contract.TreatmentType}
            Contract Length: {contract.LengthMonths} months
            Cancellation Date: {DateTime.UtcNow:yyyy-MM-dd}
            
            Recommended Action: Retention team follow-up
        ",
        RequesterEmail = customer.Email,
        RequesterName = $"{customer.FirstName} {customer.LastName}",
        Priority = TicketPriority.Normal,
        Tags = new List<string> { "cancellation", "retention", "auto_created" },
        CustomFields = new Dictionary<string, object>
        {
            ["customer_id"] = customer.Id,
            ["contract_id"] = contract.Id,
            ["treatment_type"] = contract.TreatmentType
        }
    });
}
```

## Custom Fields

Henry Meds uses custom fields to track additional context:

### User Custom Fields
- `customer_id`: Henry Meds customer ID
- `subscription_status`: active, cancelled, past_due
- `treatment_type`: weightmanagement, etc.
- `contract_end_date`: Current contract end date
- `lifetime_value`: Total revenue from customer

### Ticket Custom Fields
- `customer_id`: Link to customer
- `contract_id`: Related contract
- `invoice_id`: Related invoice
- `subscription_id`: Related subscription
- `treatment_type`: Treatment category

## Webhook Integration

### Receive Zendesk Events
```csharp
[HttpPost("webhooks/zendesk")]
public async Task<IActionResult> ReceiveWebhook()
{
    using var reader = new StreamReader(Request.Body);
    var body = await reader.ReadToEndAsync();
    
    var webhook = JsonSerializer.Deserialize<ZendeskWebhook>(body);
    
    switch (webhook.Type)
    {
        case "ticket.created":
            await HandleTicketCreatedAsync(webhook);
            break;
            
        case "ticket.updated":
            await HandleTicketUpdatedAsync(webhook);
            break;
            
        case "ticket.solved":
            await HandleTicketSolvedAsync(webhook);
            break;
    }

    return Ok();
}
```

## Testing

### Mock Service
```csharp
public class MockZendeskService : IZendeskService
{
    public List<CreateTicketRequest> CreatedTickets { get; } = new();

    public Task<Ticket> CreateTicketAsync(CreateTicketRequest request)
    {
        CreatedTickets.Add(request);
        
        return Task.FromResult(new Ticket
        {
            Id = Random.Shared.NextInt64(10000, 99999),
            Subject = request.Subject
        });
    }
}

// Test
[Test]
public async Task PaymentFailed_CreatesZendeskTicket()
{
    // Arrange
    var zendesk = new MockZendeskService();
    var handler = new PaymentFailedEventHandler(zendesk, ...);

    // Act
    await handler.HandleAsync(paymentFailedEvent);

    // Assert
    Assert.That(zendesk.CreatedTickets, Has.Count.EqualTo(1));
    Assert.That(zendesk.CreatedTickets[0].Subject, Contains.Substring("Payment Failed"));
    Assert.That(zendesk.CreatedTickets[0].Priority, Is.EqualTo(TicketPriority.High));
}
```

## Best Practices

1. **Automation**: Auto-create tickets for critical events
2. **Tagging**: Use consistent tags for routing and reporting
3. **Custom Fields**: Sync customer context for support team
4. **Priority**: High for urgent issues, Normal for informational
5. **Internal Notes**: Use for team coordination
6. **Testing**: Mock service for unit tests
7. **Rate Limiting**: Respect API rate limits

## See Also

- [Zendesk API Docs](https://developer.zendesk.com/api-reference/)
- [Event Handler Pattern](../../backend/event-handler-service/README.md)
- [Customer Support Workflows](../docs/support-workflows.md)
