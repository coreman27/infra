using Google.Cloud.PubSub.V1;
using System.Text.Json;

namespace Henry.EventHandlerService.Services;

/// <summary>
/// Service for publishing events to Pub/Sub
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(string eventType, object data);
}

public class EventPublisher : IEventPublisher
{
    private readonly PublisherClient _publisher;
    private readonly ILogger<EventPublisher> _logger;

    public EventPublisher(
        IConfiguration configuration,
        ILogger<EventPublisher> logger)
    {
        var projectId = configuration["GCP_PROJECT_ID"]!;
        var topicId = configuration["EVENT_TOPIC_ID"]!;
        
        var topicName = TopicName.FromProjectTopic(projectId, topicId);
        _publisher = PublisherClient.CreateAsync(topicName).GetAwaiter().GetResult();
        _logger = logger;
    }

    public async Task PublishAsync(string eventType, object data)
    {
        try
        {
            var eventData = new
            {
                type = eventType,
                data,
                timestamp = DateTime.UtcNow,
                id = Guid.NewGuid().ToString()
            };

            var json = JsonSerializer.Serialize(eventData);
            var message = new PubsubMessage
            {
                Data = Google.Protobuf.ByteString.CopyFromUtf8(json),
                Attributes =
                {
                    { "eventType", eventType }
                }
            };

            var messageId = await _publisher.PublishAsync(message);

            _logger.LogInformation(
                "Published event {EventType} with message ID {MessageId}",
                eventType, messageId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish event {EventType}",
                eventType
            );
            throw;
        }
    }
}
