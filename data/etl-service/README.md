# Chargebee ETL Service Pattern

## Overview

The Chargebee ETL service is a Node.js application that syncs subscription data from Chargebee API to PostgreSQL database every minute. This keeps the database in sync with the source of truth (Chargebee) for all subscription and billing data.

## Architecture

```
Chargebee API
    ↓
ETL Service (Node.js + Cloud Run)
    ↓
PostgreSQL Database
    ↓
Hasura GraphQL Engine
    ↓
Frontend/Backend Services
```

## Key Characteristics

- **Language**: Node.js (JavaScript)
- **Deployment**: Google Cloud Run
- **Schedule**: Every 1 minute (Cloud Scheduler)
- **Data**: Subscriptions, invoices, customers, items
- **Idempotency**: Upserts based on Chargebee IDs
- **Custom Fields**: Syncs cf_contract_id, cf_contract_length

## Project Structure

```
chargebee-etl/
├── index.js           # Cloud Run HTTP handler
├── etl.js             # Main ETL logic
├── logger.js          # Structured logging
├── test/              # Unit tests
├── package.json
├── Dockerfile
└── cloudbuild.yaml
```

## Core Implementation

### index.js (HTTP Handler)
```javascript
const express = require('express');
const { runETL } = require('./etl');
const logger = require('./logger');

const app = express();
const PORT = process.env.PORT || 8080;

// Health check endpoint
app.get('/health', (req, res) => {
  res.status(200).json({ status: 'healthy' });
});

// ETL trigger endpoint (called by Cloud Scheduler)
app.post('/run-etl', async (req, res) => {
  try {
    logger.info('ETL job started');
    
    const result = await runETL();
    
    logger.info('ETL job completed', {
      subscriptions: result.subscriptionsProcessed,
      invoices: result.invoicesProcessed,
      duration: result.duration
    });

    res.status(200).json({
      success: true,
      ...result
    });
  } catch (error) {
    logger.error('ETL job failed', { error: error.message, stack: error.stack });
    
    // Return 500 to trigger retry in Cloud Scheduler
    res.status(500).json({
      success: false,
      error: error.message
    });
  }
});

app.listen(PORT, () => {
  logger.info(`ETL service listening on port ${PORT}`);
});
```

### etl.js (Main ETL Logic)
```javascript
const { Pool } = require('pg');
const chargebee = require('chargebee');

// Initialize Chargebee SDK
chargebee.configure({
  site: process.env.CHARGEBEE_SITE,
  api_key: process.env.CHARGEBEE_API_KEY
});

// PostgreSQL connection pool
const pool = new Pool({
  host: process.env.DB_HOST,
  database: process.env.DB_NAME,
  user: process.env.DB_USER,
  password: process.env.DB_PASSWORD,
  max: 10,
  idleTimeoutMillis: 30000
});

async function runETL() {
  const startTime = Date.now();
  const result = {
    subscriptionsProcessed: 0,
    invoicesProcessed: 0,
    customersProcessed: 0
  };

  // Sync in order (dependencies matter)
  result.customersProcessed = await importCustomers();
  result.subscriptionsProcessed = await importSubscriptions();
  result.invoicesProcessed = await importInvoices();

  result.duration = Date.now() - startTime;
  return result;
}

async function importSubscriptions() {
  let count = 0;
  let offset = null;

  do {
    // Fetch from Chargebee API (paginated)
    const response = await chargebee.subscription.list({
      limit: 100,
      offset: offset,
      'updated_at[after]': getLastSyncTime() // Incremental sync
    }).request();

    for (const entry of response.list) {
      const subscription = entry.subscription;

      // Extract custom fields
      const {
        id,
        customer_id,
        plan_id,
        status,
        current_term_start,
        current_term_end,
        next_billing_at,
        created_at,
        updated_at,
        cf_contract_id,        // Custom field
        cf_contract_length,    // Custom field
        ...otherFields
      } = subscription;

      // Upsert to database
      await pool.query(`
        INSERT INTO chargebee.subscription (
          id,
          customer_id,
          item_price_id,
          status,
          current_term_start,
          current_term_end,
          next_billing_at,
          created_at,
          updated_at,
          contract_id,           -- Maps to cf_contract_id
          contract_length,       -- Maps to cf_contract_length
          data
        ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)
        ON CONFLICT (id) DO UPDATE SET
          customer_id = EXCLUDED.customer_id,
          item_price_id = EXCLUDED.item_price_id,
          status = EXCLUDED.status,
          current_term_start = EXCLUDED.current_term_start,
          current_term_end = EXCLUDED.current_term_end,
          next_billing_at = EXCLUDED.next_billing_at,
          updated_at = EXCLUDED.updated_at,
          contract_id = EXCLUDED.contract_id,
          contract_length = EXCLUDED.contract_length,
          data = EXCLUDED.data
      `, [
        id,
        customer_id,
        subscription.subscription_items?.[0]?.item_price_id,
        status,
        new Date(current_term_start * 1000),
        new Date(current_term_end * 1000),
        next_billing_at ? new Date(next_billing_at * 1000) : null,
        new Date(created_at * 1000),
        new Date(updated_at * 1000),
        cf_contract_id || null,
        cf_contract_length || null,
        JSON.stringify(subscription) // Store full object for reference
      ]);

      count++;
    }

    offset = response.next_offset;
  } while (offset);

  return count;
}

async function importInvoices() {
  let count = 0;
  let offset = null;

  do {
    const response = await chargebee.invoice.list({
      limit: 100,
      offset: offset,
      'updated_at[after]': getLastSyncTime()
    }).request();

    for (const entry of response.list) {
      const invoice = entry.invoice;

      await pool.query(`
        INSERT INTO chargebee.invoice (
          id,
          customer_id,
          subscription_id,
          status,
          date,
          due_date,
          total,
          amount_paid,
          amount_due,
          created_at,
          updated_at,
          data
        ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)
        ON CONFLICT (id) DO UPDATE SET
          status = EXCLUDED.status,
          amount_paid = EXCLUDED.amount_paid,
          amount_due = EXCLUDED.amount_due,
          updated_at = EXCLUDED.updated_at,
          data = EXCLUDED.data
      `, [
        invoice.id,
        invoice.customer_id,
        invoice.subscription_id,
        invoice.status,
        new Date(invoice.date * 1000),
        invoice.due_date ? new Date(invoice.due_date * 1000) : null,
        invoice.total,
        invoice.amount_paid,
        invoice.amount_due,
        new Date(invoice.created_at * 1000),
        new Date(invoice.updated_at * 1000),
        JSON.stringify(invoice)
      ]);

      count++;
    }

    offset = response.next_offset;
  } while (offset);

  return count;
}

function getLastSyncTime() {
  // Incremental sync: only fetch records updated in last 2 minutes
  // (ETL runs every 1 minute, 2 minute window for safety)
  return Math.floor(Date.now() / 1000) - 120;
}

module.exports = { runETL };
```

## Database Schema

### Subscription Table
```sql
CREATE TABLE chargebee.subscription (
  id TEXT PRIMARY KEY,
  customer_id TEXT NOT NULL,
  item_price_id TEXT,
  status TEXT NOT NULL,
  current_term_start TIMESTAMP,
  current_term_end TIMESTAMP,
  next_billing_at TIMESTAMP,
  created_at TIMESTAMP NOT NULL,
  updated_at TIMESTAMP NOT NULL,
  
  -- Custom fields from Chargebee
  contract_id TEXT,           -- cf_contract_id
  contract_length INTEGER,    -- cf_contract_length
  
  -- Full Chargebee object for reference
  data JSONB,
  
  CONSTRAINT fk_customer
    FOREIGN KEY (customer_id)
    REFERENCES chargebee.customer(id)
);

CREATE INDEX idx_subscription_customer ON chargebee.subscription(customer_id);
CREATE INDEX idx_subscription_contract ON chargebee.subscription(contract_id);
CREATE INDEX idx_subscription_status ON chargebee.subscription(status);
CREATE INDEX idx_subscription_updated_at ON chargebee.subscription(updated_at);
```

## Cloud Scheduler Setup

### Terraform Configuration
```hcl
resource "google_cloud_scheduler_job" "etl" {
  name             = "chargebee-etl"
  description      = "Run Chargebee ETL every minute"
  schedule         = "*/1 * * * *"  # Every 1 minute
  time_zone        = "America/New_York"
  attempt_deadline = "120s"

  retry_config {
    retry_count = 3
    min_backoff_duration = "10s"
    max_backoff_duration = "60s"
  }

  http_target {
    http_method = "POST"
    uri         = "${google_cloud_run_service.etl.status[0].url}/run-etl"

    oidc_token {
      service_account_email = google_service_account.etl.email
      audience              = google_cloud_run_service.etl.status[0].url
    }
  }
}
```

## Monitoring

### Logging
```javascript
const winston = require('winston');

const logger = winston.createLogger({
  level: 'info',
  format: winston.format.json(),
  transports: [
    new winston.transports.Console()
  ]
});

// Usage
logger.info('Processing subscription', {
  subscriptionId: subscription.id,
  status: subscription.status
});

logger.error('Failed to sync subscription', {
  subscriptionId: subscription.id,
  error: error.message
});
```

### Metrics
Track in Cloud Monitoring:
- ETL run duration
- Records processed per run
- Error rate
- Last successful run timestamp

### Alerts
```yaml
# Alert if ETL hasn't run in 5 minutes
alerting_policy:
  display_name: "Chargebee ETL Stale"
  conditions:
    - display_name: "No ETL runs"
      condition_threshold:
        filter: 'resource.type="cloud_run_revision" AND metric.type="run.googleapis.com/request_count"'
        comparison: COMPARISON_LT
        threshold_value: 1
        duration: 300s
```

## Error Handling

### Transient Errors (Retry)
```javascript
async function importWithRetry(importFunc) {
  const maxRetries = 3;
  let attempt = 0;

  while (attempt < maxRetries) {
    try {
      return await importFunc();
    } catch (error) {
      attempt++;
      
      if (attempt >= maxRetries) {
        throw error;
      }

      // Exponential backoff
      await sleep(Math.pow(2, attempt) * 1000);
      logger.warn('Retrying import', { attempt, error: error.message });
    }
  }
}
```

### Data Validation
```javascript
function validateSubscription(subscription) {
  if (!subscription.id) {
    throw new Error('Subscription missing ID');
  }
  
  if (!subscription.customer_id) {
    throw new Error('Subscription missing customer_id');
  }

  // Contract ID should be valid format if present
  if (subscription.cf_contract_id && !/^con_/.test(subscription.cf_contract_id)) {
    logger.warn('Invalid contract ID format', {
      subscriptionId: subscription.id,
      contractId: subscription.cf_contract_id
    });
  }
}
```

## Testing

### Unit Tests
```javascript
const { importSubscriptions } = require('./etl');
const { Pool } = require('pg');
jest.mock('chargebee');

describe('ETL', () => {
  it('syncs subscriptions from Chargebee', async () => {
    // Mock Chargebee response
    chargebee.subscription.list.mockResolvedValue({
      list: [
        {
          subscription: {
            id: 'sub_123',
            customer_id: 'cus_456',
            cf_contract_id: 'con_789'
          }
        }
      ],
      next_offset: null
    });

    const count = await importSubscriptions();

    expect(count).toBe(1);
    // Verify database insert
  });
});
```

## Best Practices

1. **Incremental Sync**: Only fetch recent updates (last 2 minutes)
2. **Idempotency**: Use UPSERT to handle duplicate runs
3. **Error Handling**: Retry transient failures, alert on persistent errors
4. **Logging**: Structured JSON logs for debugging
5. **Monitoring**: Track metrics and set up alerts
6. **Custom Fields**: Always sync cf_contract_id for business logic
7. **Full Data**: Store complete Chargebee object in JSONB for reference

## See Also

- [Chargebee Integration](../integrations/chargebee/README.md)
- [Hasura Patterns](../data/hasura-migrations/README.md)
- [Event Handler Pattern](../backend/event-handler-service/README.md)
