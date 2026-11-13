# Security Measures and Remaining Work

## Implemented Measures

### Password and account management

Users are stored through ASP.NET Core Identity. `UserManager` creates accounts and applies Identity password rules, so application code does not store raw passwords. Login checks the password through `SignInManager` with lockout-on-failure enabled.

### JWT bearer authentication

Login issues an HMAC-SHA256-signed JWT with the user's identifier, username, role claims, issuer, audience, and an expiration time. The bearer handler validates the signing key, issuer, audience, allowed algorithm, and token lifetime. The development signing key is stored in .NET user secrets rather than checked into source control; deployments must provide their own secret securely.

### Role-based authorization

Inventory and order controllers require an authenticated user. Read actions are available to authenticated users, while create and delete actions require the `Manager` role. Public registration does not accept a role field. A local administrative command assigns the Manager role to an existing user.

### Persistence and API tooling

EF Core parameterizes database operations. API responses use DTOs rather than serializing Identity users or EF Core entity graphs. Swagger declares bearer authentication so developers can authorize requests in the UI.

## Important Limitations

### Input validation is incomplete

The request records require usernames and passwords for auth endpoints, and Identity returns password-policy errors. The inventory and order DTOs do not yet define sufficient constraints for required names, positive quantities, maximum lengths, or maximum collection sizes. An isolated test accepted blank names and a negative quantity. A nonexistent inventory `OrderId` caused an unhandled foreign-key error and HTTP 500.

Add validation attributes or explicit validation and return consistent `400 Bad Request` responses. Validate referenced records, but also handle database constraint failures because a reference can change between validation and save.

### Ownership is not enforced

Any authenticated user can currently read all orders. Orders have no owner or tenant key, so role protection does not provide per-user data isolation. Add an owner/tenant relationship and enforce it on every relevant query if orders are private.

### Deployment security

The HTTP launch profile is for local development. Production must use HTTPS, either directly or through a correctly configured trusted reverse proxy. Store the JWT key in a production secret manager, restrict access, and define key rotation. Do not rely on the local manager-assignment command as a public administration interface.

### Abuse controls and token lifetime

There is no request-level rate limiter on registration or login. Add rate limits and monitoring. JWT role claims remain valid until token expiration; a role removal does not immediately revoke tokens already issued. Decide whether short lifetimes or a revocation strategy are needed.

## Suggested Security Tests

- Missing, malformed, expired, wrong-issuer, wrong-audience, and invalid-signature tokens are rejected.
- Authenticated non-managers can read but cannot create or delete.
- Managers can perform their intended write actions.
- Invalid field values and nonexistent foreign keys return client errors, not server errors.
- Users cannot read or modify another user's data once ownership rules are introduced.