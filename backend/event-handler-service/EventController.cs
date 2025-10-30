using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Henry.EventHandlerService.Controllers;

/// <summary>
/// Receives Pub/Sub push messages from Google Cloud.
/// This controller is invoked by Pub/Sub whenever a message is published to the subscription.
/// </summary>
[ApiController]
[Route("api")]
public class EventController : ControllerBase
{
    private readonly ILogger<EventController> _logger;
    private readonly IServiceProvider _serviceProvider;

    public EventController(
        ILogger<EventController> _logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Receives Pub/Sub push messages.
    /// Always returns 200 to acknowledge message receipt (even on business logic errors).
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

            _logger.LogInformation("Received event: {EventJson}", eventJson);

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
            // Return 5xx only for transient errors (DB connection, external API timeout)
            // Return 200 for business logic errors to prevent retry
            return StatusCode(500);
        }
    }

    private async Task RouteEventAsync(HasuraEventWrapper wrapper)
    {
        // Route based on table name and operation
        var tableName = wrapper.Table.Name;
        var operation = wrapper.Event.Op;

        _logger.LogInformation(
            "Routing event: table={Table}, op={Operation}, id={Id}",
            tableName, operation, wrapper.Event.Data.New?.Id ?? wrapper.Event.Data.Old?.Id
        );

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

            default:
                _logger.LogWarning("No handler registered for table: {Table}", tableName);
                break;
        }
    }
}

/// <summary>
/// Pub/Sub message envelope
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

/// <summary>
/// Hasura event wrapper (outer envelope)
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
    public string Op { get; set; } = string.Empty; // INSERT, UPDATE, DELETE
    public JsonElement? Old { get; set; }
    public JsonElement? New { get; set; }
}

public class HasuraTrigger
{
    public string Name { get; set; } = string.Empty;
}
