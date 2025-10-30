namespace Henry.EventHandlerService.Services;

/// <summary>
/// Service for querying and mutating data via Hasura GraphQL
/// </summary>
public interface IHasuraService
{
    Task<T?> QueryAsync<T>(string query, object? variables = null);
    Task<T?> MutateAsync<T>(string mutation, object variables);
    Task<Contract?> GetContractAsync(string contractId);
    Task<Subscription?> GetContractSubscriptionAsync(string contractId);
    Task<bool> TreatmentHasUpfrontPaymentOption(string itemPriceId);
}

public class HasuraService : IHasuraService
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _adminSecret;
    private readonly ILogger<HasuraService> _logger;

    public HasuraService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<HasuraService> logger)
    {
        _httpClient = httpClient;
        _endpoint = configuration["HASURA_ENDPOINT"]!;
        _adminSecret = configuration["HASURA_ADMIN_SECRET"]!;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.Add("X-Hasura-Admin-Secret", _adminSecret);
        _httpClient.DefaultRequestHeaders.Add("X-Hasura-Role", "service");
    }

    public async Task<T?> QueryAsync<T>(string query, object? variables = null)
    {
        try
        {
            var request = new
            {
                query,
                variables
            };

            var response = await _httpClient.PostAsJsonAsync(_endpoint, request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<GraphQLResponse<T>>();

            if (result?.Errors?.Any() == true)
            {
                _logger.LogError(
                    "GraphQL query errors: {Errors}",
                    string.Join(", ", result.Errors.Select(e => e.Message))
                );
                throw new GraphQLException(result.Errors);
            }

            return result!.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GraphQL query failed");
            throw;
        }
    }

    public async Task<T?> MutateAsync<T>(string mutation, object variables)
    {
        return await QueryAsync<T>(mutation, variables);
    }

    public async Task<Contract?> GetContractAsync(string contractId)
    {
        var result = await QueryAsync<Result<Contract>>("""
            query GetContract($contractId: String!) {
              Results: chargebee_contract_by_pk(id: $contractId) {
                id
                customer_id
                subscription_id
                status
                start_date
                end_date
                length_months
                auto_renew
                created_at
                updated_at
              }
            }
            """,
            new { contractId }
        );

        return result?.Results;
    }

    public async Task<Subscription?> GetContractSubscriptionAsync(string contractId)
    {
        var result = await QueryAsync<Result<Contract>>("""
            query GetContractSubscription($contractId: String!) {
              Results: chargebee_contract_by_pk(id: $contractId) {
                subscriptions(limit: 1) {
                  id
                  item_price_id
                }
              }
            }
            """,
            new { contractId }
        );

        return result?.Results?.Subscriptions?.FirstOrDefault();
    }

    public async Task<bool> TreatmentHasUpfrontPaymentOption(string itemPriceId)
    {
        var result = await QueryAsync<Result<List<TreatmentPricingMap>>>("""
            query GetTreatmentUpfrontOption($itemPriceId: String!) {
              Results: chargebee_treatment_pricing_map(
                where: {
                  item_price_id: { _eq: $itemPriceId }
                  is_upfront_payment: { _eq: true }
                }
              ) {
                id
              }
            }
            """,
            new { itemPriceId }
        );

        return result?.Results?.Any() ?? false;
    }
}

#region Models

public class GraphQLResponse<T>
{
    public T? Data { get; set; }
    public List<GraphQLError>? Errors { get; set; }
}

public class GraphQLError
{
    public string Message { get; set; } = string.Empty;
    public List<ErrorLocation>? Locations { get; set; }
    public string? Path { get; set; }
}

public class ErrorLocation
{
    public int Line { get; set; }
    public int Column { get; set; }
}

public class GraphQLException : Exception
{
    public List<GraphQLError> Errors { get; }

    public GraphQLException(List<GraphQLError> errors)
        : base($"GraphQL errors: {string.Join(", ", errors.Select(e => e.Message))}")
    {
        Errors = errors;
    }
}

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
    public List<Subscription>? Subscriptions { get; set; }
}

public class Subscription
{
    public string Id { get; set; } = string.Empty;
    public string? ItemPriceId { get; set; }
    public string? ContractId { get; set; }
}

public class TreatmentPricingMap
{
    public string Id { get; set; } = string.Empty;
    public string ItemPriceId { get; set; } = string.Empty;
    public bool IsUpfrontPayment { get; set; }
}

public class Result<T>
{
    public T? Results { get; set; }
}

#endregion
