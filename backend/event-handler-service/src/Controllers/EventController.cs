using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Henry.EventHandlerService.Controllers;

/// <summary>
/// Receives Pub/Sub push messages from Google Cloud.
/// This is the entry point for all Hasura database events.
/// </summary>
[ApiController]
[Route("api")]
public class EventController : ControllerBase
{
    private readonly ILogger<EventController> _logger;
    private readonly IServiceProvider _serviceProvider;

    public EventController(
        ILogger<EventController> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Health check endpoint for Cloud Run
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Receives Pub/Sub push messages containing Hasura events.
    /// ALWAYS returns 200 to acknowledge message (even on business logic errors).
    /// Only returns 5xx for transient infrastructure errors that should trigger retry.
    /// </summary>
    [HttpPost("events")]
    public async Task<IActionResult> ReceiveEvent([FromBody] PubSubMessage message)
    {
        try
        {
            // Decode the Pub/Sub message data (base64-encoded JSON)
            var eventJson = System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(message.Message.Data)
            );

            _logger.LogInformation("Received event: {MessageId}", message.Message.MessageId);

            // Parse the Hasura event wrapper
            var hasuraEvent = JsonSerializer.Deserialize<HasuraEventWrapper>(
                eventJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (hasuraEvent?.Event == null)
            {
                _logger.LogWarning("Invalid event format, skipping");
                return Ok(); // Still ack to prevent retry
            }

            // Route to appropriate handler based on event type
            await RouteEventAsync(hasuraEvent);

            _logger.LogInformation(
                "Event processed successfully: {EventId}",
                hasuraEvent.Id
            );

            return Ok();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse event JSON");
            return Ok(); // Ack bad messages to prevent infinite retry
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing event");
            
            // Return 5xx only for transient errors that should trigger retry
            // (e.g., database connection lost, external API timeout)
            // Return 200 for business logic errors to prevent retry
            if (IsTransientError(ex))
            {
                return StatusCode(500);
            }

            return Ok(); // Ack to prevent retry
        }
    }

    private async Task RouteEventAsync(HasuraEventWrapper wrapper)
    {
        var tableName = wrapper.Table.Name;
        var operation = wrapper.Event.Op;

        _logger.LogInformation(
            "Routing event: table={Table}, op={Operation}, trigger={Trigger}",
            tableName, operation, wrapper.Trigger.Name
        );

        try
        {
            switch (tableName)
            {
                case "contract":
                    var contractHandler = _serviceProvider.GetRequiredService<IContractEventHandler>();
                    await contractHandler.HandleAsync(wrapper);
                    break;

                case "subscription":
                    var subscriptionHandler = _serviceProvider.GetRequiredService<ISubscriptionEventHandler>();
                    await subscriptionHandler.HandleAsync(wrapper);
                    break;

                case "invoice":
                    var invoiceHandler = _serviceProvider.GetRequiredService<IInvoiceEventHandler>();
                    await invoiceHandler.HandleAsync(wrapper);
                    break;

                case "customer":
                    var customerHandler = _serviceProvider.GetRequiredService<ICustomerEventHandler>();
                    await customerHandler.HandleAsync(wrapper);
                    break;

                default:
                    _logger.LogWarning("No handler registered for table: {Table}", tableName);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error in event handler for table {Table}",
                tableName
            );
            throw;
        }
    }

    private bool IsTransientError(Exception ex)
    {
        // Add more transient error types as needed
        return ex is HttpRequestException ||
               ex is TimeoutException ||
               ex.Message.Contains("database") ||
               ex.Message.Contains("connection");
    }
}

#region Pub/Sub Models

/// <summary>
/// Pub/Sub message envelope from Google Cloud
/// </summary>
public class PubSubMessage
{
    public Message Message { get; set; } = new();
    public string Subscription { get; set; } = string.Empty;
}

public class Message
{
    public string Data { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public DateTime PublishTime { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
}

#endregion

#region Hasura Event Models

/// <summary>
/// Hasura event wrapper (outer envelope)
/// See: https://hasura.io/docs/latest/event-triggers/payload/
/// </summary>
public class HasuraEventWrapper
{
    public string Id { get; set; } = string.Empty;
    public HasuraTable Table { get; set; } = new();
    public HasuraEventData Event { get; set; } = new();
    public HasuraTrigger Trigger { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class HasuraTable
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class HasuraEventData
{
    /// <summary>
    /// Operation type: INSERT, UPDATE, DELETE
    /// </summary>
    public string Op { get; set; } = string.Empty;
    
    /// <summary>
    /// Old row data (for UPDATE and DELETE)
    /// </summary>
    public JsonElement? Old { get; set; }
    
    /// <summary>
    /// New row data (for INSERT and UPDATE)
    /// </summary>
    public JsonElement? New { get; set; }
}

public class HasuraTrigger
{
    public string Name { get; set; } = string.Empty;
}

#endregion

#region Event Handler Interfaces

public interface IContractEventHandler
{
    Task HandleAsync(HasuraEventWrapper evt);
}

public interface ISubscriptionEventHandler
{
    Task HandleAsync(HasuraEventWrapper evt);
}

public interface IInvoiceEventHandler
{
    Task HandleAsync(HasuraEventWrapper evt);
}

public interface ICustomerEventHandler
{
    Task HandleAsync(HasuraEventWrapper evt);
}

#endregion
