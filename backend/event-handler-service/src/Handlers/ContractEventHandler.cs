using System.Text.Json;

namespace Henry.EventHandlerService.Handlers;

/// <summary>
/// Handles contract lifecycle events from Hasura
/// Based on Henry.ContractAgent patterns
/// </summary>
public class ContractEventHandler : IContractEventHandler
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

        _logger.LogInformation(
            "Processing contract event: op={Operation}, trigger={Trigger}",
            operation, evt.Trigger.Name
        );

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
        var contract = evt.Event.New?.Deserialize<Contract>(
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

        // When contract is created in "future" status,
        // update Chargebee subscription with contract metadata
        if (contract.Status == "future")
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to update Chargebee subscription {SubscriptionId}",
                    contract.SubscriptionId
                );
                throw;
            }
        }

        // Schedule auto-renewal task for when contract ends
        if (contract.AutoRenew && contract.EndDate.HasValue)
        {
            var scheduleTime = contract.EndDate.Value.AddDays(-1);
            
            try
            {
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
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to schedule auto-renewal for contract {ContractId}",
                    contract.Id
                );
                // Don't throw - contract is still created
            }
        }

        // Publish event for downstream services
        await _eventPublisher.PublishAsync("contract.created", new
        {
            contractId = contract.Id,
            customerId = contract.CustomerId,
            status = contract.Status,
            startDate = contract.StartDate,
            endDate = contract.EndDate,
            lengthMonths = contract.LengthMonths,
            autoRenew = contract.AutoRenew
        });

        _logger.LogInformation(
            "Contract {ContractId} created successfully",
            contract.Id
        );
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

        // Handle status transitions
        await HandleStatusChangeAsync(oldContract, newContract);

        // Handle auto-renew toggle
        if (oldContract.AutoRenew != newContract.AutoRenew)
        {
            await HandleAutoRenewChangeAsync(newContract);
        }
    }

    private async Task HandleStatusChangeAsync(Contract oldContract, Contract newContract)
    {
        // future → active
        if (oldContract.Status == "future" && newContract.Status == "active")
        {
            _logger.LogInformation(
                "Contract {Id} activated, notifying customer {CustomerId}",
                newContract.Id, newContract.CustomerId
            );

            await _eventPublisher.PublishAsync("contract.activated", new
            {
                contractId = newContract.Id,
                customerId = newContract.CustomerId,
                startDate = newContract.StartDate,
                endDate = newContract.EndDate
            });
        }

        // active → completed
        if (oldContract.Status != "completed" && newContract.Status == "completed")
        {
            _logger.LogInformation(
                "Contract {Id} completed, clearing Chargebee metadata",
                newContract.Id
            );

            try
            {
                // Clear contract metadata from Chargebee subscription
                await _chargebee.UpdateSubscriptionAsync(
                    newContract.SubscriptionId,
                    new
                    {
                        cf_contract_id = (string?)null,
                        cf_contract_length = (int?)null
                    }
                );

                _logger.LogInformation(
                    "Cleared Chargebee metadata for subscription {SubscriptionId}",
                    newContract.SubscriptionId
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to clear Chargebee metadata for subscription {SubscriptionId}",
                    newContract.SubscriptionId
                );
            }

            // Publish completion event
            await _eventPublisher.PublishAsync("contract.completed", new
            {
                contractId = newContract.Id,
                customerId = newContract.CustomerId,
                endDate = newContract.EndDate
            });
        }

        // active → renewed
        if (oldContract.Status != "renewed" && newContract.Status == "renewed")
        {
            _logger.LogInformation(
                "Contract {Id} renewed",
                newContract.Id
            );

            await _eventPublisher.PublishAsync("contract.renewed", new
            {
                oldContractId = newContract.Id,
                customerId = newContract.CustomerId,
                renewalDate = DateTime.UtcNow
            });
        }
    }

    private async Task HandleAutoRenewChangeAsync(Contract contract)
    {
        _logger.LogInformation(
            "Contract {Id} auto-renew changed to {AutoRenew}",
            contract.Id, contract.AutoRenew
        );

        if (contract.AutoRenew && contract.EndDate.HasValue)
        {
            // Schedule renewal task
            var scheduleTime = contract.EndDate.Value.AddDays(-1);
            
            await _cloudTask.ScheduleTaskAsync(
                taskName: $"contract-renewal-{contract.Id}",
                url: "/api/tasks/auto-renew-contract",
                payload: new { contractId = contract.Id },
                scheduleTime: scheduleTime
            );

            _logger.LogInformation(
                "Scheduled auto-renewal for contract {ContractId}",
                contract.Id
            );
        }
        else
        {
            // Cancel scheduled renewal (if exists)
            // Note: Cloud Tasks doesn't support cancellation by name easily
            // In production, would need to handle this differently
            _logger.LogInformation(
                "Auto-renew disabled for contract {ContractId}",
                contract.Id
            );
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
            "Contract {Id} deleted",
            contract.Id
        );

        // Publish deletion event
        await _eventPublisher.PublishAsync("contract.deleted", new
        {
            contractId = contract.Id,
            customerId = contract.CustomerId
        });
    }
}

/// <summary>
/// Contract domain model
/// Matches chargebee.contract table schema
/// </summary>
public class Contract
{
    public string Id { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // future, active, completed, renewed, discontinued, void
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int LengthMonths { get; set; }
    public bool AutoRenew { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
