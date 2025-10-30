using System.Text.Json;

namespace Henry.EventHandlerService.Handlers;

/// <summary>
/// Handles invoice events from Hasura
/// Based on Henry.ContractAgent.InvoiceEventHandler
/// </summary>
public class InvoiceEventHandler : IInvoiceEventHandler
{
    private readonly IHasuraService _hasura;
    private readonly ICloudTaskService _cloudTask;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<InvoiceEventHandler> _logger;

    public InvoiceEventHandler(
        IHasuraService hasura,
        ICloudTaskService cloudTask,
        IEventPublisher eventPublisher,
        ILogger<InvoiceEventHandler> logger)
    {
        _hasura = hasura;
        _cloudTask = cloudTask;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task HandleAsync(HasuraEventWrapper evt)
    {
        var invoice = evt.Event.New?.Deserialize<Invoice>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (invoice == null)
        {
            _logger.LogWarning("Failed to deserialize invoice from event");
            return;
        }

        _logger.LogInformation(
            "Processing invoice event: id={InvoiceId}, status={Status}, subscriptionId={SubscriptionId}",
            invoice.Id, invoice.Status, invoice.SubscriptionId
        );

        // Get subscription to check if it has an active contract
        var subscription = await GetSubscriptionWithContractAsync(invoice.SubscriptionId);
        
        if (subscription?.ContractId == null)
        {
            _logger.LogInformation(
                "Invoice {InvoiceId} has no associated contract, skipping",
                invoice.Id
            );
            return;
        }

        // Get the contract
        var contract = await _hasura.GetContractAsync(subscription.ContractId);
        
        if (contract == null)
        {
            _logger.LogWarning(
                "Contract {ContractId} not found for invoice {InvoiceId}",
                subscription.ContractId, invoice.Id
            );
            return;
        }

        if (!contract.EndDate.HasValue)
        {
            _logger.LogWarning(
                "Contract {ContractId} has no end date",
                contract.Id
            );
            return;
        }

        // Check if this is the last invoice before contract ends
        // Only schedule renewal if nextBillingAt is AFTER contract end date
        if (invoice.NextBillingAt.HasValue && 
            invoice.NextBillingAt.Value.Date > contract.EndDate.Value.Date)
        {
            _logger.LogInformation(
                "Invoice {InvoiceId} is last before contract {ContractId} ends, scheduling renewal check",
                invoice.Id, contract.Id
            );

            await ScheduleContractRenewalAsync(contract);
        }
        else
        {
            _logger.LogInformation(
                "Invoice {InvoiceId} is not last invoice (nextBilling={NextBilling}, contractEnd={ContractEnd})",
                invoice.Id, invoice.NextBillingAt, contract.EndDate
            );
        }
    }

    private async Task<Subscription?> GetSubscriptionWithContractAsync(string subscriptionId)
    {
        var result = await _hasura.QueryAsync<Result<Subscription>>("""
            query GetSubscriptionContract($subscriptionId: String!) {
              Results: chargebee_subscription_by_pk(id: $subscriptionId) {
                id
                contract_id
              }
            }
            """,
            new { subscriptionId }
        );

        return result?.Results;
    }

    private async Task ScheduleContractRenewalAsync(Contract contract)
    {
        if (!contract.AutoRenew)
        {
            _logger.LogInformation(
                "Contract {ContractId} has auto-renew disabled, skipping",
                contract.Id
            );
            return;
        }

        var scheduleTime = contract.EndDate!.Value.AddDays(-1);

        try
        {
            // Use named task for deduplication
            await _cloudTask.ScheduleTaskAsync(
                taskName: $"contract-renewal-{contract.Id}",
                url: "/api/tasks/auto-renew-contract",
                payload: new { contractId = contract.Id },
                scheduleTime: scheduleTime
            );

            _logger.LogInformation(
                "Scheduled auto-renewal for contract {ContractId} at {ScheduleTime}",
                contract.Id, scheduleTime
            );
        }
        catch (Exception ex) when (ex.Message.Contains("AlreadyExists"))
        {
            _logger.LogInformation(
                "Auto-renewal task already exists for contract {ContractId}",
                contract.Id
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to schedule auto-renewal for contract {ContractId}",
                contract.Id
            );
            throw;
        }
    }
}

public class Invoice
{
    public string Id { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? Date { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public int Total { get; set; }
    public int AmountPaid { get; set; }
    public int AmountDue { get; set; }
}

public class Subscription
{
    public string Id { get; set; } = string.Empty;
    public string? ContractId { get; set; }
    public string? ItemPriceId { get; set; }
}

public class Result<T>
{
    public T? Results { get; set; }
}
