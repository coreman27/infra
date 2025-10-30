# React + Vite Portal Application Pattern

## Overview

Henry Meds portal applications use React 18 with Vite for fast development, TypeScript for type safety, and GraphQL Code Generator for type-safe API integration.

## Tech Stack

- **React 18**: UI framework
- **Vite**: Build tool (faster than webpack)
- **TypeScript**: Type safety
- **GraphQL Code Generator**: Auto-generate typed hooks
- **React Router**: Client-side routing
- **TanStack Query**: Data fetching and caching
- **Vitest**: Unit testing
- **Playwright**: E2E testing
- **Docker + nginx**: Production deployment

## Project Structure

```
portal/
├── src/
│   ├── components/          # Reusable UI components
│   │   ├── Contract/
│   │   │   ├── ContractCard.tsx
│   │   │   └── ContractList.tsx
│   │   └── common/
│   │       ├── Button.tsx
│   │       └── Card.tsx
│   ├── pages/               # Route pages
│   │   ├── Dashboard.tsx
│   │   ├── Contracts.tsx
│   │   └── Profile.tsx
│   ├── hooks/               # Custom React hooks
│   │   ├── useAuth.ts
│   │   └── useContracts.ts
│   ├── graphql/             # GraphQL queries/mutations
│   │   ├── queries/
│   │   │   └── contract.graphql
│   │   └── mutations/
│   │       └── updateContract.graphql
│   ├── services/            # API clients
│   │   ├── graphql.ts
│   │   └── auth.ts
│   ├── utils/               # Utility functions
│   │   ├── date.ts
│   │   └── format.ts
│   ├── types/               # TypeScript types
│   │   └── generated/       # Auto-generated from GraphQL
│   ├── App.tsx              # Root component
│   └── main.tsx             # Entry point
├── e2e/                     # Playwright E2E tests
│   ├── contracts.spec.ts
│   └── auth.spec.ts
├── public/                  # Static assets
├── env/                     # Environment configs
│   ├── dev/
│   │   └── .env
│   └── staging/
│       └── .env
├── scripts/
│   └── generate-types.sh
├── graphql.config.ts        # GraphQL codegen config
├── vite.config.ts
├── vitest.config.ts
├── Dockerfile
├── nginx.conf
└── package.json
```

## GraphQL Integration

### 1. Define Queries
```graphql
# src/graphql/queries/contract.graphql
query GetContracts($customerId: String!) {
  chargebee_contract(
    where: { 
      customer_id: { _eq: $customerId }
      status: { _in: ["future", "active"] }
    }
    order_by: { created_at: desc }
  ) {
    id
    status
    startDate: start_date
    endDate: end_date
    lengthMonths: length_months
    autoRenew: auto_renew
    subscription {
      id
      itemPriceId: item_price_id
      status
    }
  }
}
```

### 2. Generate TypeScript Types
```bash
yarn generate-dev
```

This generates:
- Type-safe hooks: `useGetContractsQuery`
- TypeScript types: `GetContractsQuery`, `GetContractsQueryVariables`

### 3. Use Generated Hook
```typescript
import { useGetContractsQuery } from '@/types/generated/graphql';

export function ContractList() {
  const { user } = useAuth();
  
  const { data, isLoading, error, refetch } = useGetContractsQuery({
    customerId: user.customerId
  });

  if (isLoading) return <LoadingSpinner />;
  if (error) return <ErrorMessage error={error} />;

  return (
    <div className="contract-list">
      {data?.chargebee_contract.map(contract => (
        <ContractCard key={contract.id} contract={contract} />
      ))}
    </div>
  );
}
```

### 4. Mutations
```graphql
# src/graphql/mutations/updateContract.graphql
mutation UpdateContract($id: String!, $autoRenew: Boolean!) {
  update_chargebee_contract_by_pk(
    pk_columns: { id: $id }
    _set: { auto_renew: $autoRenew, updated_at: "now()" }
  ) {
    id
    autoRenew: auto_renew
    updatedAt: updated_at
  }
}
```

```typescript
import { useUpdateContractMutation } from '@/types/generated/graphql';
import { useMutation, useQueryClient } from '@tanstack/react-query';

export function ContractSettings({ contract }) {
  const queryClient = useQueryClient();
  
  const { mutate, isPending } = useUpdateContractMutation({
    onSuccess: () => {
      // Invalidate queries to refetch
      queryClient.invalidateQueries({ queryKey: ['GetContracts'] });
      toast.success('Contract updated');
    }
  });

  const handleToggleAutoRenew = () => {
    mutate({
      id: contract.id,
      autoRenew: !contract.autoRenew
    });
  };

  return (
    <button onClick={handleToggleAutoRenew} disabled={isPending}>
      {contract.autoRenew ? 'Disable' : 'Enable'} Auto-Renew
    </button>
  );
}
```

## Authentication Pattern

### Firebase Auth Integration
```typescript
// src/services/auth.ts
import { 
  getAuth, 
  signInWithEmailAndPassword,
  signOut as firebaseSignOut,
  onAuthStateChanged 
} from 'firebase/auth';

export const auth = getAuth();

export async function signIn(email: string, password: string) {
  const result = await signInWithEmailAndPassword(auth, email, password);
  const token = await result.user.getIdToken();
  
  // Store token for GraphQL requests
  localStorage.setItem('authToken', token);
  
  return result.user;
}

export async function signOut() {
  await firebaseSignOut(auth);
  localStorage.removeItem('authToken');
}

export function onAuthChange(callback: (user: User | null) => void) {
  return onAuthStateChanged(auth, callback);
}
```

### Auth Context Provider
```typescript
// src/hooks/useAuth.ts
import { createContext, useContext, useEffect, useState } from 'react';

interface AuthContextValue {
  user: User | null;
  isLoading: boolean;
  signIn: (email: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const unsubscribe = onAuthChange((user) => {
      setUser(user);
      setIsLoading(false);
    });

    return unsubscribe;
  }, []);

  return (
    <AuthContext.Provider value={{ user, isLoading, signIn, signOut }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within AuthProvider');
  return context;
}
```

### Protected Routes
```typescript
// src/components/ProtectedRoute.tsx
import { Navigate } from 'react-router-dom';
import { useAuth } from '@/hooks/useAuth';

export function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { user, isLoading } = useAuth();

  if (isLoading) return <LoadingSpinner />;
  if (!user) return <Navigate to="/login" replace />;

  return <>{children}</>;
}

// App.tsx
<Routes>
  <Route path="/login" element={<Login />} />
  <Route path="/dashboard" element={
    <ProtectedRoute>
      <Dashboard />
    </ProtectedRoute>
  } />
</Routes>
```

## GraphQL Client Setup

```typescript
// src/services/graphql.ts
import { GraphQLClient } from 'graphql-request';

const HASURA_ENDPOINT = import.meta.env.VITE_HASURA_ENDPOINT;

function getAuthToken() {
  return localStorage.getItem('authToken');
}

export const graphqlClient = new GraphQLClient(HASURA_ENDPOINT, {
  headers: () => {
    const token = getAuthToken();
    return {
      Authorization: token ? `Bearer ${token}` : '',
      'X-Hasura-Role': 'user'
    };
  }
});

// For GraphQL Code Generator
export const fetcher = <TData, TVariables>(
  query: string,
  variables?: TVariables
) => {
  return graphqlClient.request<TData>(query, variables);
};
```

### GraphQL Codegen Config
```typescript
// graphql.config.ts
import { CodegenConfig } from '@graphql-codegen/cli';

const config: CodegenConfig = {
  schema: {
    'https://hasura-dev.henrymeds.com/v1/graphql': {
      headers: {
        'X-Hasura-Admin-Secret': process.env.HASURA_ADMIN_SECRET!
      }
    }
  },
  documents: ['src/**/*.graphql', 'src/**/*.tsx'],
  generates: {
    'src/types/generated/graphql.ts': {
      plugins: [
        'typescript',
        'typescript-operations',
        'typescript-react-query'
      ],
      config: {
        fetcher: '@/services/graphql#fetcher',
        exposeFetcher: true,
        exposeDocument: true,
        exposeQueryKeys: true,
        addInfiniteQuery: true
      }
    }
  }
};

export default config;
```

## Component Patterns

### 1. Feature Component
```typescript
// src/components/Contract/ContractCard.tsx
import { Contract } from '@/types/generated/graphql';
import { formatDate, formatCurrency } from '@/utils/format';

interface ContractCardProps {
  contract: Contract;
  onRenewToggle?: () => void;
}

export function ContractCard({ contract, onRenewToggle }: ContractCardProps) {
  return (
    <div className="contract-card">
      <div className="contract-header">
        <h3>Contract #{contract.id}</h3>
        <span className={`status status-${contract.status}`}>
          {contract.status}
        </span>
      </div>
      
      <div className="contract-details">
        <div className="detail-row">
          <span>Start Date:</span>
          <span>{formatDate(contract.startDate)}</span>
        </div>
        <div className="detail-row">
          <span>End Date:</span>
          <span>{formatDate(contract.endDate)}</span>
        </div>
        <div className="detail-row">
          <span>Length:</span>
          <span>{contract.lengthMonths} months</span>
        </div>
      </div>

      <div className="contract-actions">
        <label>
          <input
            type="checkbox"
            checked={contract.autoRenew}
            onChange={onRenewToggle}
          />
          Auto-renew
        </label>
      </div>
    </div>
  );
}
```

### 2. Custom Hook
```typescript
// src/hooks/useContracts.ts
import { useGetContractsQuery } from '@/types/generated/graphql';
import { useAuth } from './useAuth';

export function useContracts() {
  const { user } = useAuth();
  
  const query = useGetContractsQuery(
    { customerId: user?.customerId ?? '' },
    { enabled: !!user?.customerId }
  );

  const activeContracts = query.data?.chargebee_contract.filter(
    c => c.status === 'active'
  ) ?? [];

  return {
    contracts: query.data?.chargebee_contract ?? [],
    activeContracts,
    isLoading: query.isLoading,
    error: query.error,
    refetch: query.refetch
  };
}
```

## Testing

### Unit Tests (Vitest)
```typescript
// src/components/Contract/ContractCard.test.tsx
import { render, screen } from '@testing-library/react';
import { ContractCard } from './ContractCard';

describe('ContractCard', () => {
  const mockContract = {
    id: 'con_123',
    status: 'active',
    startDate: '2024-01-01',
    endDate: '2024-06-30',
    lengthMonths: 6,
    autoRenew: true
  };

  it('renders contract details', () => {
    render(<ContractCard contract={mockContract} />);
    
    expect(screen.getByText('Contract #con_123')).toBeInTheDocument();
    expect(screen.getByText('active')).toBeInTheDocument();
    expect(screen.getByText('6 months')).toBeInTheDocument();
  });

  it('shows auto-renew checkbox', () => {
    render(<ContractCard contract={mockContract} />);
    
    const checkbox = screen.getByRole('checkbox');
    expect(checkbox).toBeChecked();
  });
});
```

### E2E Tests (Playwright)
```typescript
// e2e/contracts.spec.ts
import { test, expect } from '@playwright/test';

test.describe('Contracts Page', () => {
  test.beforeEach(async ({ page }) => {
    // Login
    await page.goto('/login');
    await page.fill('[name="email"]', 'test@example.com');
    await page.fill('[name="password"]', 'password123');
    await page.click('button[type="submit"]');
    await page.waitForURL('/dashboard');
  });

  test('displays active contracts', async ({ page }) => {
    await page.goto('/contracts');
    
    await expect(page.locator('.contract-card')).toHaveCount(2);
    await expect(page.locator('.status-active')).toBeVisible();
  });

  test('toggles auto-renew', async ({ page }) => {
    await page.goto('/contracts');
    
    const checkbox = page.locator('[type="checkbox"]').first();
    await checkbox.click();
    
    await expect(page.locator('.toast-success')).toBeVisible();
  });
});
```

## Build & Deployment

### Dockerfile
```dockerfile
# Build stage
FROM node:20-alpine AS build

WORKDIR /app

# Copy package files
COPY package.json yarn.lock ./
RUN yarn install --frozen-lockfile

# Copy source
COPY . .

# Build
ARG VITE_HASURA_ENDPOINT
ARG VITE_FIREBASE_API_KEY
ENV VITE_HASURA_ENDPOINT=$VITE_HASURA_ENDPOINT
ENV VITE_FIREBASE_API_KEY=$VITE_FIREBASE_API_KEY

RUN yarn build

# Production stage
FROM nginx:alpine

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
```

### nginx.conf
```nginx
server {
    listen 80;
    server_name _;

    root /usr/share/nginx/html;
    index index.html;

    # Gzip compression
    gzip on;
    gzip_types text/plain text/css application/json application/javascript text/xml application/xml application/xml+rss text/javascript;

    # SPA routing
    location / {
        try_files $uri $uri/ /index.html;
    }

    # Cache static assets
    location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg|woff|woff2)$ {
        expires 1y;
        add_header Cache-Control "public, immutable";
    }

    # Security headers
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-XSS-Protection "1; mode=block" always;
}
```

### Cloud Build
```yaml
# cloudbuild.yaml
steps:
  # Build Docker image
  - name: 'gcr.io/cloud-builders/docker'
    args:
      - 'build'
      - '-t'
      - 'gcr.io/$PROJECT_ID/customer-portal:$SHORT_SHA'
      - '--build-arg'
      - 'VITE_HASURA_ENDPOINT=$_HASURA_ENDPOINT'
      - '.'

  # Push image
  - name: 'gcr.io/cloud-builders/docker'
    args: ['push', 'gcr.io/$PROJECT_ID/customer-portal:$SHORT_SHA']

  # Deploy to Cloud Run
  - name: 'gcr.io/cloud-builders/gcloud'
    args:
      - 'run'
      - 'deploy'
      - 'customer-portal'
      - '--image'
      - 'gcr.io/$PROJECT_ID/customer-portal:$SHORT_SHA'
      - '--region'
      - 'us-central1'
      - '--platform'
      - 'managed'

  # Run E2E tests
  - name: 'mcr.microsoft.com/playwright:v1.40.0'
    env:
      - 'BASE_URL=https://customer-portal-dev-xxx.run.app'
    entrypoint: 'bash'
    args:
      - '-c'
      - 'yarn install && yarn test:e2e'
```

## Best Practices

1. **Type Safety**: Use GraphQL Code Generator for all API calls
2. **Authentication**: Store tokens securely, refresh before expiry
3. **Error Handling**: Global error boundary + local error states
4. **Loading States**: Show spinners, skeleton screens
5. **Accessibility**: Use semantic HTML, ARIA labels
6. **Performance**: Code splitting, lazy loading, memoization
7. **Testing**: Unit tests for logic, E2E for critical paths

## See Also

- [Hasura GraphQL](../../data/hasura-migrations/README.md)
- [Authentication](../../integrations/identity-platform/README.md)
- [Deployment](../../infrastructure/terraform/README.md)
