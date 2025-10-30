# Hasura GraphQL Patterns

## Overview

Hasura provides an auto-generated GraphQL API over PostgreSQL, with event triggers, permissions, and relationships. This is the primary data access layer for Henry Meds services.

## Architecture

```
Frontend / Backend Services
    ↓
GraphQL Queries/Mutations
    ↓
Hasura GraphQL Engine
    ↓
PostgreSQL Database
    ↓
Event Triggers → Pub/Sub
```

## Project Structure

```
HasuraMigrations/
├── metadata/
│   ├── databases/
│   │   └── default/
│   │       ├── tables/
│   │       │   ├── chargebee_contract.yaml
│   │       │   ├── chargebee_subscription.yaml
│   │       │   └── ...
│   │       └── functions/
│   ├── actions.yaml
│   ├── allow_list.yaml
│   └── remote_schemas.yaml
├── migrations/
│   └── default/
│       ├── 1234567890_create_contract_table/
│       │   └── up.sql
│       ├── 1234567891_add_contract_subscription_fk/
│       │   └── up.sql
│       └── ...
├── seeds/
│   └── default/
│       └── seed_data.sql
└── config.yaml
```

## Core Concepts

### 1. Tables
Database tables exposed via GraphQL.

### 2. Relationships
Foreign key relationships exposed as nested queries.

### 3. Permissions
Row-level security via permission rules.

### 4. Event Triggers
Database events published to Pub/Sub.

### 5. Actions
Custom business logic via HTTP handlers.

### 6. Remote Schemas
Federation with other GraphQL services.

## Migration Pattern

### Create Migration
```bash
hasura migrate create create_contract_table --database-name default
```

### Migration SQL (up.sql)
```sql
CREATE TABLE chargebee.contract (
    id TEXT PRIMARY KEY,
    customer_id TEXT NOT NULL,
    subscription_id TEXT NOT NULL,
    status TEXT NOT NULL,
    start_date TIMESTAMP,
    end_date TIMESTAMP,
    length_months INTEGER NOT NULL,
    auto_renew BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP,
    
    CONSTRAINT fk_customer
        FOREIGN KEY (customer_id)
        REFERENCES chargebee.customer(id),
        
    CONSTRAINT fk_subscription
        FOREIGN KEY (subscription_id)
        REFERENCES chargebee.subscription(id),
        
    CONSTRAINT valid_status
        CHECK (status IN ('future', 'active', 'completed', 'renewed', 'discontinued', 'void'))
);

CREATE INDEX idx_contract_customer ON chargebee.contract(customer_id);
CREATE INDEX idx_contract_subscription ON chargebee.contract(subscription_id);
CREATE INDEX idx_contract_status ON chargebee.contract(status);
CREATE INDEX idx_contract_end_date ON chargebee.contract(end_date) WHERE auto_renew = true;

-- Trigger for updated_at
CREATE TRIGGER set_contract_updated_at
    BEFORE UPDATE ON chargebee.contract
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();
```

### Track Table in Hasura
```bash
hasura metadata apply
```

### metadata/databases/default/tables/chargebee_contract.yaml
```yaml
table:
  name: contract
  schema: chargebee
object_relationships:
  - name: customer
    using:
      foreign_key_constraint_on: customer_id
  - name: subscription
    using:
      foreign_key_constraint_on: subscription_id
array_relationships:
  - name: line_items
    using:
      foreign_key_constraint_on:
        column: contract_id
        table:
          name: contract_line_item
          schema: chargebee
select_permissions:
  - role: user
    permission:
      columns:
        - id
        - customer_id
        - subscription_id
        - status
        - start_date
        - end_date
        - length_months
        - auto_renew
        - created_at
      filter:
        customer:
          firebase_uid:
            _eq: X-Hasura-User-Id
      allow_aggregations: false
insert_permissions:
  - role: service
    permission:
      columns:
        - id
        - customer_id
        - subscription_id
        - status
        - start_date
        - end_date
        - length_months
        - auto_renew
      check: {}
update_permissions:
  - role: service
    permission:
      columns:
        - status
        - end_date
        - auto_renew
        - updated_at
      filter: {}
event_triggers:
  - name: contract_events
    definition:
      enable_manual: false
      insert:
        columns: '*'
      update:
        columns:
          - status
          - end_date
          - auto_renew
    retry_conf:
      interval_sec: 10
      num_retries: 3
      timeout_sec: 60
    webhook_from_env: CONTRACT_EVENT_WEBHOOK_URL
```

## Relationship Patterns

### One-to-Many (Array Relationship)
```yaml
# Contract has many line items
array_relationships:
  - name: line_items
    using:
      foreign_key_constraint_on:
        column: contract_id
        table:
          name: contract_line_item
          schema: chargebee
```

### Many-to-One (Object Relationship)
```yaml
# Contract belongs to customer
object_relationships:
  - name: customer
    using:
      foreign_key_constraint_on: customer_id
```

### Manual Relationship (No FK)
```yaml
# Subscription to treatment via item_price_id
object_relationships:
  - name: treatment_pricing
    using:
      manual_configuration:
        column_mapping:
          item_price_id: item_price_id
        insertion_order: null
        remote_table:
          name: treatment_pricing_map
          schema: chargebee
```

## Query Patterns

### Simple Query
```graphql
query GetContract($contractId: String!) {
  chargebee_contract_by_pk(id: $contractId) {
    id
    status
    start_date
    end_date
    customer {
      id
      email
      first_name
      last_name
    }
  }
}
```

### Nested Query with Filter
```graphql
query GetActiveContracts($customerId: String!) {
  chargebee_contract(
    where: {
      customer_id: { _eq: $customerId }
      status: { _in: ["future", "active"] }
    }
    order_by: { created_at: desc }
  ) {
    id
    status
    start_date
    end_date
    subscription {
      id
      item_price_id
      status
    }
    line_items {
      id
      item_price_id
      quantity
    }
  }
}
```

### Aggregation Query
```graphql
query GetContractStats($customerId: String!) {
  chargebee_contract_aggregate(
    where: { customer_id: { _eq: $customerId } }
  ) {
    aggregate {
      count
    }
    nodes {
      status
    }
  }
}
```

## Mutation Patterns

### Insert
```graphql
mutation CreateContract($object: chargebee_contract_insert_input!) {
  insert_chargebee_contract_one(object: $object) {
    id
    status
    created_at
  }
}
```

### Update
```graphql
mutation UpdateContractStatus($contractId: String!, $status: String!) {
  update_chargebee_contract_by_pk(
    pk_columns: { id: $contractId }
    _set: { status: $status, updated_at: "now()" }
  ) {
    id
    status
    updated_at
  }
}
```

### Delete
```graphql
mutation DeleteContract($contractId: String!) {
  delete_chargebee_contract_by_pk(id: $contractId) {
    id
  }
}
```

## Event Trigger Pattern

### Configuration
```yaml
event_triggers:
  - name: invoice_events
    definition:
      enable_manual: false
      insert:
        columns: '*'
      update:
        columns:
          - status
          - amount_due
    retry_conf:
      interval_sec: 10
      num_retries: 3
      timeout_sec: 60
    webhook_from_env: INVOICE_EVENT_WEBHOOK_URL
```

### Event Payload
```json
{
  "id": "unique-event-id",
  "event": {
    "op": "INSERT",
    "data": {
      "old": null,
      "new": {
        "id": "inv_123",
        "status": "posted",
        "amount_due": 5000
      }
    }
  },
  "table": {
    "schema": "chargebee",
    "name": "invoice"
  },
  "trigger": {
    "name": "invoice_events"
  },
  "created_at": "2024-01-15T10:00:00.000Z"
}
```

## Action Pattern (REST Endpoint)

### Action Definition
```yaml
- name: createCheckoutSession
  definition:
    kind: synchronous
    handler: https://checkout-service-xxx.run.app/api/checkout/create
    forward_client_headers: true
    headers:
      - name: X-Hasura-Role
        value_from_env: HASURA_ADMIN_SECRET
  permissions:
    - role: user
  comment: Creates a Chargebee checkout session
```

### GraphQL Schema
```graphql
type Mutation {
  createCheckoutSession(
    itemPriceId: String!
    billingCycles: Int
  ): CheckoutSession
}

type CheckoutSession {
  id: String!
  url: String!
  expiresAt: String!
}
```

### Handler Response (Controller)
```csharp
[HttpPost("api/checkout/create")]
public async Task<IActionResult> CreateCheckoutSession(
    [FromBody] HasuraActionRequest<CheckoutInput> request)
{
    var input = request.Input;
    
    // Business logic
    var session = await _chargebee.CreateCheckoutSessionAsync(
        input.ItemPriceId,
        input.BillingCycles
    );
    
    // ALWAYS return 200 for Hasura actions
    return Ok(new
    {
        id = session.Id,
        url = session.Url,
        expiresAt = session.ExpiresAt
    });
}
```

## Permission Patterns

### User Role (Customer Portal)
```yaml
select_permissions:
  - role: user
    permission:
      columns:
        - id
        - status
        - start_date
        - end_date
      filter:
        customer:
          firebase_uid:
            _eq: X-Hasura-User-Id
      allow_aggregations: false
```

### Service Role (Backend Services)
```yaml
select_permissions:
  - role: service
    permission:
      columns: '*'
      filter: {}
      allow_aggregations: true
```

### CCT Role (Clinical Portal)
```yaml
select_permissions:
  - role: cct
    permission:
      columns:
        - id
        - customer_id
        - status
        - created_at
      filter:
        customer:
          treatment_state_id:
            _in: X-Hasura-Allowed-States
```

## .NET Client Pattern

```csharp
public interface IHasuraService
{
    Task<T?> QueryAsync<T>(string query, object? variables = null);
    Task<T?> MutateAsync<T>(string mutation, object variables);
}

public class HasuraService : IHasuraService
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _adminSecret;

    public async Task<T?> QueryAsync<T>(string query, object? variables = null)
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
            throw new GraphQLException(result.Errors);
        }

        return result.Data;
    }
}

// Usage
var result = await _hasura.QueryAsync<ContractResult>("""
    query GetContract($id: String!) {
      Results: chargebee_contract_by_pk(id: $id) {
        id
        status
        customer { email }
      }
    }
    """,
    new { id = contractId }
);
```

## Best Practices

1. **Migrations**: Always use up.sql, create indexes
2. **Relationships**: Leverage FK constraints when possible
3. **Permissions**: Use row-level security, least privilege
4. **Event Triggers**: Use for async side effects only
5. **Actions**: Use for synchronous business logic
6. **Naming**: Use snake_case for columns, camelCase for GraphQL
7. **Indexes**: Index foreign keys and filter columns
8. **Metadata**: Version control metadata YAML files

## See Also

- [Hasura Docs](https://hasura.io/docs/)
- [Event Handler Pattern](../../backend/event-handler-service/README.md)
- [Chargebee ETL](../../data/etl-service/README.md)
