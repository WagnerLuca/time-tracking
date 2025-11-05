# Time Tracking API Client

Auto-generated TypeScript API client for the Time Tracking backend.

## Documentation

### API Endpoints
- [TimeTrackingApiApi](api/TimeTrackingApiApi.md) - TimeTrackingApi endpoints
- [UsersApi](api/UsersApi.md) - Users endpoints

### Models
- [CreateUserRequest](models/CreateUserRequest.md) - CreateUserRequest model
- [UpdateUserRequest](models/UpdateUserRequest.md) - UpdateUserRequest model
- [User](models/User.md) - User model
- [UserResponse](models/UserResponse.md) - UserResponse model

## Usage

```typescript
import { UsersApi, Configuration } from '$lib/api';

// Create configuration (optional)
const configuration = new Configuration({
    basePath: 'http://localhost:7000'
});

// Create API instance
const usersApi = new UsersApi(configuration);

// Make API calls
const users = await usersApi.apiUsersGet();
```

## Documentation Formats

- **Markdown**: Browse the [API](docs/api/) and [Models](docs/models/) folders
- **HTML**: Launch application with `npm run dev` and navigate to `/docs` route

## Generated

This client was auto-generated using OpenAPI Generator.
To regenerate: `npm run generate-api` 
