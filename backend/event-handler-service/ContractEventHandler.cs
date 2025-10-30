using System.Text.Json;

namespace Henry.EventHandlerService.Handlers;

/// <summary>
/// Interface for event handlers
/// </summary>
public interface IEventHandler
{
    Task HandleAsync(HasuraEventWrapper evt);
}

/// <summary>
/// Example: Contract event handler
/// Handles INSERT, UPDATE, DELETE operations on the contract table
/// </summary>
public class ContractEventHandler : IEventHandler
{
    private readonly IHasuraService _hasura;
    private readonly IChargebeeService _chargebee;
    private readonly ICloudTaskService _cloudTask;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ContractEventHandler> _logger;

    public ContractEventHandler(
        IHasuraService hasura,
        IChargebeeService chargebee,
        ICloudTaskService cloudTask,
        IEventPublisher eventPublisher,
        ILogger<ContractEventHandler> logger)
    {
        _hasura = hasura;
        _chargebee = chargebee;
        _cloudTask = cloudTask;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task HandleAsync(HasuraEventWrapper evt)
    {
        var operation = evt.Event.Op;

        switch (operation)
        {
            case "INSERT":
                await HandleInsertAsync(evt);
                break;

            case "UPDATE":
                await HandleUpdateAsync(evt);
                break;

            case "DELETE":
                await HandleDeleteAsync(evt);
                break;

            default:
                _logger.LogWarning("Unknown operation: {Operation}", operation);
                break;
        }
    }

    private async Task HandleInsertAsync(HasuraEventWrapper evt)
    {
        var contract = evt.Event.New.Deserialize<Contract>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (contract == null)
        {
            _logger.LogWarning("Failed to deserialize contract from event");
            return;
        }

        _logger.LogInformation(
            "Processing new contract: id={Id}, status={Status}, customerId={CustomerId}",
            contract.Id, contract.Status, contract.CustomerId
        );

        // Example: When contract is created in "future" status,
        // update Chargebee subscription with contract metadata
        if (contract.Status == "future")
        {
            await _chargebee.UpdateSubscriptionAsync(
                contract.SubscriptionId,
                new
                {
                    cf_contract_id = contract.Id,
                    cf_contract_length = contract.LengthMonths
                }
            );

            _logger.LogInformation(
                "Updated Chargebee subscription {SubscriptionId} with contract {ContractId}",
                contract.SubscriptionId, contract.Id
            );
        }

        // Example: Schedule auto-renewal task for when contract ends
        if (contract.AutoRenew && contract.EndDate.HasValue)
        {
            var scheduleTime = contract.EndDate.Value.AddDays(-1);
            
            await _cloudTask.ScheduleTaskAsync(
                taskName: $"contract-renewal-{contract.Id}",
                url: "/api/tasks/auto-renew-contract",
                payload: new { contractId = contract.Id },
                scheduleTime: scheduleTime
            );

            _logger.LogInformation(
                "Scheduled auto-renewal task for contract {ContractId} at {ScheduleTime}",
                contract.Id, scheduleTime
            );
        }

        // Publish event for downstream services
        await _eventPublisher.PublishAsync("contract.created", new
        {
            contractId = contract.Id,
            customerId = contract.CustomerId,
            status = contract.Status,
            startDate = contract.StartDate,
            endDate = contract.EndDate
        });
    }

    private async Task HandleUpdateAsync(HasuraEventWrapper evt)
    {
        var oldContract = evt.Event.Old?.Deserialize<Contract>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );
        var newContract = evt.Event.New?.Deserialize<Contract>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (oldContract == null || newContract == null)
        {
            _logger.LogWarning("Failed to deserialize contract from event");
            return;
        }

        // Example: When status changes from "future" to "active"
        if (oldContract.Status == "future" && newContract.Status == "active")
        {
            _logger.LogInformation(
                "Contract {Id} activated, notifying customer {CustomerId}",
                newContract.Id, newContract.CustomerId
            );

            // Send activation notification
            await _eventPublisher.PublishAsync("contract.activated", new
            {
                contractId = newContract.Id,
                customerId = newContract.CustomerId,
                startDate = newContract.StartDate
            });
        }

        // Example: When status changes to "completed"
        if (newContract.Status == "completed" && oldContract.Status != "completed")
        {
            _logger.LogInformation(
                "Contract {Id} completed, clearing Chargebee metadata",
                newContract.Id
            );

            // Clear contract metadata from Chargebee subscription
            await _chargebee.UpdateSubscriptionAsync(
                newContract.SubscriptionId,
                new
                {
                    cf_contract_id = (string?)null,
                    cf_contract_length = (int?)null
                }
            );

            // Publish completion event
            await _eventPublisher.PublishAsync("contract.completed", new
            {
                contractId = newContract.Id,
                customerId = newContract.CustomerId,
                endDate = newContract.EndDate
            });
        }
    }

    private async Task HandleDeleteAsync(HasuraEventWrapper evt)
    {
        var contract = evt.Event.Old?.Deserialize<Contract>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (contract == null)
        {
            _logger.LogWarning("Failed to deserialize contract from event");
            return;
        }

        _logger.LogInformation(
            "Contract {Id} deleted, performing cleanup",
            contract.Id
        );

        // Cleanup operations (if needed)
        // Note: Be careful with DELETE events - ensure you want to trigger side effects
    }
}

/// <summary>
/// Contract domain model
/// </summary>
public class Contract
{
    public string Id { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int LengthMonths { get; set; }
    public bool AutoRenew { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
