namespace Henry.ChargebeeIntegration;

using ChargeBee.Api;
using ChargeBee.Models;

/// <summary>
/// Service for interacting with Chargebee API
/// Based on Henry.ChargebeeAgent patterns
/// </summary>
public interface IChargebeeService
{
    Task<Subscription> GetSubscriptionAsync(string subscriptionId);
    Task<Subscription> UpdateSubscriptionAsync(string subscriptionId, object customFields);
    Task<Subscription> CancelSubscriptionAsync(string subscriptionId, bool endOfTerm = true);
    Task<Subscription> ChangePlanAsync(string subscriptionId, string planId);
    Task<Subscription> ResetSubscriptionToMonthlyAsync(string subscriptionId);
}

public class ChargebeeService : IChargebeeService
{
    private readonly ILogger<ChargebeeService> _logger;

    public ChargebeeService(
        IConfiguration configuration,
        ILogger<ChargebeeService> logger)
    {
        // Initialize Chargebee SDK
        var apiKey = configuration["CHARGEBEE_API_KEY"]!;
        var site = configuration["CHARGEBEE_SITE"]!;
        
        ApiConfig.Configure(site, apiKey);
        _logger = logger;
    }

    public async Task<Subscription> GetSubscriptionAsync(string subscriptionId)
    {
        try
        {
            var result = await Task.Run(() => 
                ChargeBee.Models.Subscription.Retrieve(subscriptionId).Request()
            );

            _logger.LogInformation(
                "Retrieved subscription {SubscriptionId}",
                subscriptionId
            );

            return result.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to retrieve subscription {SubscriptionId}",
                subscriptionId
            );
            throw;
        }
    }

    public async Task<Subscription> UpdateSubscriptionAsync(
        string subscriptionId,
        object customFields)
    {
        try
        {
            var request = ChargeBee.Models.Subscription.Update(subscriptionId);

            // Use reflection to set custom fields dynamically
            var properties = customFields.GetType().GetProperties();
            foreach (var prop in properties)
            {
                var value = prop.GetValue(customFields);
                var methodName = $"CfField{ToPascalCase(prop.Name)}";
                
                var method = request.GetType().GetMethod(methodName);
                if (method != null)
                {
                    method.Invoke(request, new[] { value });
                }
            }

            var result = await Task.Run(() => request.Request());

            _logger.LogInformation(
                "Updated subscription {SubscriptionId} with custom fields",
                subscriptionId
            );

            return result.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to update subscription {SubscriptionId}",
                subscriptionId
            );
            throw;
        }
    }

    public async Task<Subscription> CancelSubscriptionAsync(
        string subscriptionId,
        bool endOfTerm = true)
    {
        try
        {
            var request = ChargeBee.Models.Subscription.Cancel(subscriptionId)
                .EndOfTerm(endOfTerm);

            var result = await Task.Run(() => request.Request());

            _logger.LogInformation(
                "Cancelled subscription {SubscriptionId} (endOfTerm={EndOfTerm})",
                subscriptionId, endOfTerm
            );

            return result.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to cancel subscription {SubscriptionId}",
                subscriptionId
            );
            throw;
        }
    }

    public async Task<Subscription> ChangePlanAsync(string subscriptionId, string planId)
    {
        try
        {
            var request = ChargeBee.Models.Subscription.Update(subscriptionId)
                .ItemPriceId(planId)
                .Prorate(true);

            var result = await Task.Run(() => request.Request());

            _logger.LogInformation(
                "Changed subscription {SubscriptionId} to plan {PlanId}",
                subscriptionId, planId
            );

            return result.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to change plan for subscription {SubscriptionId}",
                subscriptionId
            );
            throw;
        }
    }

    public async Task<Subscription> ResetSubscriptionToMonthlyAsync(string subscriptionId)
    {
        try
        {
            // Get current subscription to find original monthly price
            var current = await GetSubscriptionAsync(subscriptionId);
            var originalPriceId = DetermineMonthlyPriceId(current);

            // Update subscription: clear contract fields + reset to monthly price
            var request = ChargeBee.Models.Subscription.Update(subscriptionId)
                .CfFieldContractId(null)
                .CfFieldContractLength(null)
                .ItemPriceId(originalPriceId);

            var result = await Task.Run(() => request.Request());

            _logger.LogInformation(
                "Reset subscription {SubscriptionId} to monthly pricing",
                subscriptionId
            );

            return result.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to reset subscription {SubscriptionId} to monthly",
                subscriptionId
            );
            throw;
        }
    }

    private string DetermineMonthlyPriceId(Subscription subscription)
    {
        // Logic to determine the monthly price ID based on current subscription
        // This would typically involve querying treatment_pricing_map
        // For now, simplified example
        var currentPriceId = subscription.ItemPriceId;
        
        // Example: if current is 6-month contract, convert to monthly
        if (currentPriceId.Contains("-6month"))
        {
            return currentPriceId.Replace("-6month", "-monthly");
        }

        return currentPriceId;
    }

    private string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        
        var parts = input.Split('_');
        return string.Join("", parts.Select(p => 
            char.ToUpper(p[0]) + p.Substring(1).ToLower()
        ));
    }
}
