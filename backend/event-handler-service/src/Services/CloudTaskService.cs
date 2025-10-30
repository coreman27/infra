using Google.Cloud.Tasks.V2;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Text.Json;

namespace Henry.EventHandlerService.Services;

/// <summary>
/// Service for scheduling Cloud Tasks
/// </summary>
public interface ICloudTaskService
{
    Task ScheduleTaskAsync(
        string taskName,
        string url,
        object payload,
        DateTime scheduleTime);
}

public class CloudTaskService : ICloudTaskService
{
    private readonly CloudTasksClient _client;
    private readonly string _projectId;
    private readonly string _location;
    private readonly string _queueName;
    private readonly string _serviceUrl;
    private readonly ILogger<CloudTaskService> _logger;

    public CloudTaskService(
        IConfiguration configuration,
        ILogger<CloudTaskService> logger)
    {
        _client = CloudTasksClient.Create();
        _projectId = configuration["GCP_PROJECT_ID"]!;
        _location = configuration["GCP_LOCATION"] ?? "us-central1";
        _queueName = configuration["CLOUD_TASK_QUEUE"]!;
        _serviceUrl = configuration["SERVICE_URL"]!;
        _logger = logger;
    }

    public async Task ScheduleTaskAsync(
        string taskName,
        string url,
        object payload,
        DateTime scheduleTime)
    {
        var queuePath = new QueueName(_projectId, _location, _queueName);
        var taskPath = new Google.Cloud.Tasks.V2.TaskName(_projectId, _location, _queueName, taskName);

        try
        {
            var task = new Google.Cloud.Tasks.V2.Task
            {
                Name = taskPath.ToString(),
                ScheduleTime = Timestamp.FromDateTime(scheduleTime.ToUniversalTime()),
                HttpRequest = new HttpRequest
                {
                    HttpMethod = HttpMethod.Post,
                    Url = $"{_serviceUrl}{url}",
                    Headers =
                    {
                        { "Content-Type", "application/json" }
                    },
                    Body = ByteString.CopyFromUtf8(JsonSerializer.Serialize(payload))
                }
            };

            var response = await _client.CreateTaskAsync(queuePath, task);

            _logger.LogInformation(
                "Scheduled task {TaskName} at {ScheduleTime}",
                taskName, scheduleTime
            );
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            _logger.LogInformation(
                "Task {TaskName} already exists, skipping",
                taskName
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to schedule task {TaskName}",
                taskName
            );
            throw;
        }
    }
}
