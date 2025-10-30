# Firebase Identity Platform Integration

## Overview

Firebase Identity Platform powers authentication and authorization for Henry Meds applications. It provides secure user authentication with JWT tokens that integrate with Hasura permissions.

## Key Features

- Email/password authentication
- JWT token generation
- Role-based access control
- Integration with Hasura permissions
- User management

## Architecture

```
Frontend Application
    ↓
Firebase Auth SDK
    ↓
Firebase Identity Platform
    ↓
JWT Token (with custom claims)
    ↓
Hasura GraphQL (validates JWT)
    ↓
PostgreSQL (row-level security)
```

## Configuration

### Frontend (Firebase SDK)
```typescript
// src/services/firebase.ts
import { initializeApp } from 'firebase/app';
import { getAuth } from 'firebase/auth';

const firebaseConfig = {
  apiKey: import.meta.env.VITE_FIREBASE_API_KEY,
  authDomain: 'henrymeds.firebaseapp.com',
  projectId: 'henry-prod-345721'
};

export const app = initializeApp(firebaseConfig);
export const auth = getAuth(app);
```

### Backend (.NET Admin SDK)
```csharp
// appsettings.json
{
  "Firebase": {
    "ProjectId": "henry-prod-345721",
    "CredentialPath": "/secrets/firebase-admin-key.json"
  }
}

// Startup.cs
services.AddSingleton<FirebaseAuth>(provider =>
{
    var credentialPath = configuration["Firebase:CredentialPath"];
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromFile(credentialPath)
    });
    return FirebaseAuth.DefaultInstance;
});
```

## Frontend Authentication

### Sign Up
```typescript
import { createUserWithEmailAndPassword } from 'firebase/auth';
import { auth } from '@/services/firebase';

export async function signUp(email: string, password: string) {
  try {
    const userCredential = await createUserWithEmailAndPassword(
      auth,
      email,
      password
    );

    // Get JWT token
    const token = await userCredential.user.getIdToken();
    
    // Store for API requests
    localStorage.setItem('authToken', token);

    return userCredential.user;
  } catch (error) {
    if (error.code === 'auth/email-already-in-use') {
      throw new Error('Email already registered');
    }
    throw error;
  }
}
```

### Sign In
```typescript
import { signInWithEmailAndPassword } from 'firebase/auth';

export async function signIn(email: string, password: string) {
  const userCredential = await signInWithEmailAndPassword(
    auth,
    email,
    password
  );

  const token = await userCredential.user.getIdToken();
  localStorage.setItem('authToken', token);

  return userCredential.user;
}
```

### Sign Out
```typescript
import { signOut as firebaseSignOut } from 'firebase/auth';

export async function signOut() {
  await firebaseSignOut(auth);
  localStorage.removeItem('authToken');
}
```

### Get Current Token
```typescript
export async function getCurrentToken(): Promise<string | null> {
  const user = auth.currentUser;
  if (!user) return null;

  // Force refresh if needed (auto-refreshes before expiry)
  const token = await user.getIdToken();
  return token;
}
```

### Auth State Listener
```typescript
import { onAuthStateChanged } from 'firebase/auth';

export function onAuthChange(callback: (user: User | null) => void) {
  return onAuthStateChanged(auth, callback);
}

// Usage in React
useEffect(() => {
  const unsubscribe = onAuthChange((user) => {
    setUser(user);
    setIsLoading(false);
  });

  return unsubscribe;
}, []);
```

## Backend User Management

### Create User (Admin)
```csharp
public class IdentityService : IIdentityService
{
    private readonly FirebaseAuth _auth;

    public async Task<string> CreateUserAsync(
        string email,
        string password,
        string role = "user")
    {
        var args = new UserRecordArgs
        {
            Email = email,
            Password = password,
            EmailVerified = false
        };

        var userRecord = await _auth.CreateUserAsync(args);

        // Set custom claims for Hasura
        var claims = new Dictionary<string, object>
        {
            ["https://hasura.io/jwt/claims"] = new
            {
                x_hasura_default_role = role,
                x_hasura_allowed_roles = new[] { role },
                x_hasura_user_id = userRecord.Uid
            }
        };

        await _auth.SetCustomUserClaimsAsync(userRecord.Uid, claims);

        return userRecord.Uid;
    }
}
```

### Update Custom Claims
```csharp
public async Task UpdateUserRoleAsync(string uid, string role)
{
    var claims = new Dictionary<string, object>
    {
        ["https://hasura.io/jwt/claims"] = new
        {
            x_hasura_default_role = role,
            x_hasura_allowed_roles = new[] { role, "user" },
            x_hasura_user_id = uid
        }
    };

    await _auth.SetCustomUserClaimsAsync(uid, claims);
}
```

### Verify Token (Middleware)
```csharp
public class FirebaseAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly FirebaseAuth _auth;

    public async Task InvokeAsync(HttpContext context)
    {
        var token = context.Request.Headers["Authorization"]
            .FirstOrDefault()?.Replace("Bearer ", "");

        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var decodedToken = await _auth.VerifyIdTokenAsync(token);
                context.Items["FirebaseUid"] = decodedToken.Uid;
                context.Items["FirebaseEmail"] = decodedToken.Claims["email"];
            }
            catch (FirebaseAuthException)
            {
                context.Response.StatusCode = 401;
                return;
            }
        }

        await _next(context);
    }
}
```

## Hasura JWT Integration

### Hasura Configuration
```yaml
# hasura config
HASURA_GRAPHQL_JWT_SECRET: |
  {
    "type": "RS256",
    "jwk_url": "https://www.googleapis.com/service_accounts/v1/jwk/securetoken@system.gserviceaccount.com",
    "audience": "henry-prod-345721",
    "issuer": "https://securetoken.google.com/henry-prod-345721"
  }
```

### JWT Token Structure
```json
{
  "iss": "https://securetoken.google.com/henry-prod-345721",
  "aud": "henry-prod-345721",
  "auth_time": 1705334400,
  "user_id": "abc123",
  "sub": "abc123",
  "iat": 1705334400,
  "exp": 1705338000,
  "email": "customer@example.com",
  "email_verified": true,
  "firebase": {
    "identities": {
      "email": ["customer@example.com"]
    },
    "sign_in_provider": "password"
  },
  "https://hasura.io/jwt/claims": {
    "x-hasura-default-role": "user",
    "x-hasura-allowed-roles": ["user"],
    "x-hasura-user-id": "abc123"
  }
}
```

### Frontend GraphQL Client
```typescript
import { GraphQLClient } from 'graphql-request';
import { getCurrentToken } from '@/services/auth';

export const graphqlClient = new GraphQLClient(
  import.meta.env.VITE_HASURA_ENDPOINT,
  {
    headers: async () => {
      const token = await getCurrentToken();
      return {
        Authorization: token ? `Bearer ${token}` : '',
        'X-Hasura-Role': 'user'
      };
    }
  }
);
```

## Role-Based Access

### Roles
- **user**: Customer portal access (row-level security)
- **cct**: Clinical Care Team access (assigned customers only)
- **provider**: Provider portal access
- **service**: Backend service access (full permissions)
- **admin**: Administrative access

### Hasura Permissions Example
```yaml
# Contract table permissions
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
      
  - role: cct
    permission:
      columns:
        - id
        - customer_id
        - status
        - start_date
        - end_date
      filter:
        customer:
          assigned_cct_id:
            _eq: X-Hasura-User-Id
            
  - role: service
    permission:
      columns: '*'
      filter: {}
```

## User Lifecycle

### 1. Sign Up Flow
```typescript
// Frontend
const user = await signUp(email, password);

// Backend creates customer record
await createCustomer({
  firebase_uid: user.uid,
  email: user.email,
  ...
});
```

### 2. Email Verification
```typescript
import { sendEmailVerification } from 'firebase/auth';

export async function sendVerificationEmail() {
  const user = auth.currentUser;
  if (!user) throw new Error('No user signed in');

  await sendEmailVerification(user);
}
```

### 3. Password Reset
```typescript
import { sendPasswordResetEmail } from 'firebase/auth';

export async function resetPassword(email: string) {
  await sendPasswordResetEmail(auth, email);
}
```

### 4. Delete User
```csharp
public async Task DeleteUserAsync(string uid)
{
    await _auth.DeleteUserAsync(uid);
    
    // Also delete from database
    await _hasura.DeleteCustomerByFirebaseUidAsync(uid);
}
```

## Testing

### Mock Auth Service
```typescript
export class MockAuthService {
  currentUser: User | null = null;

  async signIn(email: string, password: string) {
    this.currentUser = {
      uid: 'test-uid',
      email,
      getIdToken: async () => 'mock-token'
    };
    return this.currentUser;
  }

  async signOut() {
    this.currentUser = null;
  }
}
```

### Integration Tests
```csharp
[Test]
public async Task CreateUser_SetsCustomClaims()
{
    // Arrange
    var service = new IdentityService(_auth);

    // Act
    var uid = await service.CreateUserAsync(
        "test@example.com",
        "password123",
        "user"
    );

    // Assert
    var user = await _auth.GetUserAsync(uid);
    var claims = user.CustomClaims["https://hasura.io/jwt/claims"];
    Assert.That(claims["x_hasura_default_role"], Is.EqualTo("user"));
}
```

## Security Best Practices

1. **Token Expiry**: Tokens expire after 1 hour, auto-refresh
2. **HTTPS Only**: Never send tokens over HTTP
3. **Secure Storage**: Use secure storage (not localStorage in production)
4. **Email Verification**: Require email verification for sensitive actions
5. **Role Validation**: Validate roles on backend, not just frontend
6. **Custom Claims**: Use for Hasura permissions only
7. **Audit Logs**: Log authentication events

## See Also

- [Firebase Auth Docs](https://firebase.google.com/docs/auth)
- [Hasura JWT Auth](https://hasura.io/docs/latest/auth/authentication/jwt/)
- [Frontend Auth Pattern](../../frontend/react-vite-portal/README.md)
