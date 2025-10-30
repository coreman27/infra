# Kameleoon Feature Flags Integration

## Overview

Kameleoon provides feature flags and A/B testing capabilities for controlled rollouts and experimentation across Henry Meds applications.

## Key Concepts

- **Feature Flags**: Enable/disable features remotely
- **Experiments**: A/B testing with variations
- **Targeting**: User segmentation for flags
- **Metrics**: Track conversion and impact

## Architecture

```
Frontend/Backend Application
    ↓
Kameleoon SDK
    ↓
Kameleoon API
    ↓
Feature Flag Evaluation
    ↓
Application Logic
```

## Configuration

### Frontend (JavaScript SDK)
```typescript
// src/services/kameleoon.ts
import { KameleoonClient } from '@kameleoon/javascript-sdk';

const siteCode = import.meta.env.VITE_KAMELEOON_SITE_CODE;

export const kameleoon = new KameleoonClient({
  siteCode,
  configuration: {
    updateInterval: 60000, // 1 minute
    environment: import.meta.env.MODE
  }
});

// Initialize
await kameleoon.initialize();
```

### Backend (.NET SDK)
```csharp
// Startup.cs
services.AddSingleton<IKameleoonClient>(provider =>
{
    var siteCode = configuration["Kameleoon:SiteCode"];
    var client = new KameleoonClient(siteCode);
    client.WaitInit().Wait();
    return client;
});
```

## Frontend Usage

### 1. Get Feature Flag
```typescript
import { kameleoon } from '@/services/kameleoon';

export function useFeatureFlag(featureKey: string) {
  const [isEnabled, setIsEnabled] = useState(false);
  const { user } = useAuth();

  useEffect(() => {
    if (!user) return;

    const visitorCode = user.customerId;
    kameleoon.addData(visitorCode, new CustomData(1, user.email));

    const enabled = kameleoon.isFeatureActive(visitorCode, featureKey);
    setIsEnabled(enabled);
  }, [user, featureKey]);

  return isEnabled;
}

// Usage in component
function Dashboard() {
  const showNewDashboard = useFeatureFlag('new_dashboard_ui');

  return showNewDashboard ? <NewDashboard /> : <OldDashboard />;
}
```

### 2. Get Experiment Variation
```typescript
export function useExperiment(experimentId: number) {
  const [variation, setVariation] = useState<string | null>(null);
  const { user } = useAuth();

  useEffect(() => {
    if (!user) return;

    const visitorCode = user.customerId;
    
    // Track user attributes
    kameleoon.addData(visitorCode, new CustomData(1, user.email));
    kameleoon.addData(visitorCode, new CustomData(2, user.treatmentType));

    const variationId = kameleoon.triggerExperiment(visitorCode, experimentId);
    setVariation(variationId);
  }, [user, experimentId]);

  return variation;
}

// Usage
function CheckoutPage() {
  const variation = useExperiment(123456);

  if (variation === 'variation_a') {
    return <OneStepCheckout />;
  } else if (variation === 'variation_b') {
    return <TwoStepCheckout />;
  }

  return <DefaultCheckout />;
}
```

### 3. Track Conversion
```typescript
export function trackGoal(goalId: number) {
  const { user } = useAuth();
  if (!user) return;

  const visitorCode = user.customerId;
  kameleoon.trackConversion(visitorCode, goalId);
}

// Usage
function CompletePurchase() {
  const handleComplete = async () => {
    await processPurchase();
    
    // Track conversion for experiment
    trackGoal(789012);
  };
}
```

## Backend Usage

### 1. Feature Flag in Service
```csharp
public class ContractService
{
    private readonly IKameleoonClient _kameleoon;

    public async Task<Contract> CreateContractAsync(CreateContractRequest request)
    {
        var visitorCode = request.CustomerId;

        // Check if new contract flow is enabled
        var useNewFlow = await _kameleoon.IsFeatureActiveAsync(
            visitorCode,
            "new_contract_flow"
        );

        if (useNewFlow)
        {
            return await CreateContractV2Async(request);
        }
        else
        {
            return await CreateContractV1Async(request);
        }
    }
}
```

### 2. Gradual Rollout
```csharp
public class AutoRenewService
{
    public async Task HandleAutoRenewAsync(Contract contract)
    {
        // Gradual rollout: 25% of users get new auto-renew logic
        var useNewLogic = await _kameleoon.IsFeatureActiveAsync(
            contract.CustomerId,
            "auto_renew_v2"
        );

        if (useNewLogic)
        {
            await AutoRenewV2Async(contract);
        }
        else
        {
            await AutoRenewV1Async(contract);
        }
    }
}
```

### 3. A/B Test with Custom Data
```csharp
public async Task<CheckoutSession> CreateCheckoutAsync(string customerId)
{
    // Track customer attributes for targeting
    _kameleoon.AddData(customerId, new CustomData(1, customer.Email));
    _kameleoon.AddData(customerId, new CustomData(2, customer.TreatmentType));
    _kameleoon.AddData(customerId, new CustomData(3, customer.State));

    // Get experiment variation
    var variationId = await _kameleoon.TriggerExperimentAsync(
        customerId,
        experimentId: 123456
    );

    // Different pricing based on variation
    return variationId switch
    {
        "variation_a" => CreateDiscountedCheckout(customer),
        "variation_b" => CreateStandardCheckout(customer),
        _ => CreateDefaultCheckout(customer)
    };
}
```

## Common Feature Flags

### Product Features
- `new_dashboard_ui`: New dashboard interface
- `contract_auto_renew`: Auto-renewal capability
- `upfront_payment_option`: Upfront payment plans
- `multi_treatment_support`: Multiple treatments per customer

### Experimental Features
- `ai_chat_support`: AI-powered chat support
- `video_consultations`: Video call feature
- `mobile_app_beta`: Mobile app beta access

### Emergency Flags
- `maintenance_mode`: Disable features during maintenance
- `payment_processing_pause`: Pause new payments
- `new_signup_pause`: Pause new customer signups

## Targeting Rules

### By Treatment Type
```typescript
// Kameleoon dashboard configuration
{
  "targeting": {
    "customData": {
      "index": 2,  // Treatment type custom data index
      "value": "weightmanagement"
    }
  }
}
```

### By Customer Lifetime Value
```typescript
{
  "targeting": {
    "customData": {
      "index": 10,  // LTV custom data index
      "operator": "greater_than",
      "value": 1000
    }
  }
}
```

### By Geographic Region
```typescript
{
  "targeting": {
    "customData": {
      "index": 3,  // State custom data index
      "value": ["CA", "NY", "TX"]
    }
  }
}
```

## Best Practices

### 1. Default Behavior
Always provide fallback for when flag is disabled:
```typescript
const showNewFeature = useFeatureFlag('new_feature') ?? false;

return showNewFeature ? <NewFeature /> : <OldFeature />;
```

### 2. Error Handling
```typescript
try {
  const enabled = kameleoon.isFeatureActive(visitorCode, featureKey);
  return enabled;
} catch (error) {
  // Log error and return safe default
  console.error('Kameleoon error:', error);
  return false;
}
```

### 3. Visitor Identification
Use consistent visitor codes:
- Frontend: Customer ID from auth
- Backend: Customer ID from request context
- Anonymous: Device ID or session ID

### 4. Custom Data Strategy
Define clear custom data indexes:
- 1: Email
- 2: Treatment type
- 3: State
- 4: Subscription status
- 5: Contract status
- 10: Lifetime value
- 11: Days since signup

### 5. Experiment Tracking
Track all relevant goals:
```typescript
// Signup conversion
trackGoal(GOALS.SIGNUP_COMPLETED);

// Purchase conversion
trackGoal(GOALS.PURCHASE_COMPLETED);

// Engagement
trackGoal(GOALS.FEATURE_USED);
```

## Testing

### Mock Client
```typescript
export class MockKameleoonClient {
  private flags: Record<string, boolean> = {};

  setFlag(key: string, enabled: boolean) {
    this.flags[key] = enabled;
  }

  isFeatureActive(visitorCode: string, featureKey: string): boolean {
    return this.flags[featureKey] ?? false;
  }

  triggerExperiment(visitorCode: string, experimentId: number): string {
    return 'control';
  }
}

// Test
it('shows new feature when flag enabled', () => {
  const kameleoon = new MockKameleoonClient();
  kameleoon.setFlag('new_feature', true);

  render(<Component />, { kameleoon });

  expect(screen.getByText('New Feature')).toBeInTheDocument();
});
```

## Rollout Strategy

### Phase 1: Internal Testing (0%)
- Flag OFF for all users
- Dev team tests manually

### Phase 2: Canary (5%)
- Enable for 5% of users
- Monitor errors and metrics

### Phase 3: Gradual Rollout (25% → 50% → 75%)
- Increase percentage if metrics positive
- Roll back if issues detected

### Phase 4: Full Rollout (100%)
- Enable for all users
- Monitor for 2 weeks

### Phase 5: Cleanup
- Remove flag from code
- Archive flag in Kameleoon

## Monitoring

Track feature flag impact:
- Error rates before/after rollout
- User engagement metrics
- Conversion rates
- Performance impact

## See Also

- [Kameleoon Docs](https://developers.kameleoon.com/)
- [Frontend Integration](../../frontend/react-vite-portal/README.md)
- [Backend Services](../../backend/event-handler-service/README.md)
