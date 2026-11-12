# Production Readiness Review

Review date: 2026-11-12. Scope: the LogiTrack API, its database configuration, and the existing performance and persistence documentation. The priorities below are recommendations; their severity depends on the application's intended users and deployment environment.

## High Priority

### Order access and ownership

Registration is public, and any authenticated user can read all orders. Orders do not have an owner or tenant field. This is a risk if orders are meant to be private; it may be acceptable for a trusted internal application.

- Evidence: [registration](../LogiTrack/Controllers/AuthController.cs), [order authorization](../LogiTrack/Controllers/OrderController.cs), and [persistence notes](state-persistence.md).
- Recommendation: decide who can register and read orders. If data is private, associate orders with users or tenants and enforce ownership in queries and authorization.

### HTTPS in production

The application calls HTTPS redirection, but the local HTTP profile can run without an HTTPS port. Redirection alone does not ensure that credentials or tokens reach the application over an encrypted connection.

- Evidence: [request pipeline](../LogiTrack/Program.cs) and [launch profiles](../LogiTrack/Properties/launchSettings.json).
- Recommendation: require TLS in production, including at the ingress or trusted reverse proxy. Configure forwarded headers with trusted proxies where appropriate, and use HSTS at the correct layer.

## Medium Priority

### Request validation and database errors

The request records allow empty names, missing values that default to empty strings or zero, and negative quantities. In an isolated test, an inventory item with an empty name and quantity `-5` and an order with an empty customer name were accepted with HTTP `201`.

A nonexistent inventory `OrderId` caused a foreign-key failure and HTTP `500`. This was also verified using an isolated database.

- Evidence: [inventory requests](../LogiTrack/Controllers/InventoryController.cs) and [order requests](../LogiTrack/Controllers/OrderController.cs).
- Recommendation: define required fields, length and quantity limits, and collection-size limits. Validate referenced orders and handle database constraint failures, including races, with suitable client errors.

### Authentication abuse and token revocation

Identity account lockout is enabled, but there is no request-level rate limiting for login or registration. JWTs contain role claims and last one hour; removing a role does not invalidate an already-issued token.

- Evidence: [authentication controller](../LogiTrack/Controllers/AuthController.cs).
- Recommendation: rate-limit authentication endpoints, monitor abuse, and decide whether the existing token lifetime meets revocation requirements. If immediate revocation is required, add an appropriate server-side validation or revocation mechanism.

### Unbounded list queries

Inventory and order list endpoints currently materialize every matching row. The inventory cache also holds the entire list in memory. Memory use, response size, and latency can grow with the dataset.

- Evidence: [inventory list](../LogiTrack/Controllers/InventoryController.cs) and [order list](../LogiTrack/Controllers/OrderController.cs).
- Recommendation: add bounded pagination and deterministic ordering; add filtering and indexes based on measured access patterns. Load-test with realistic data and concurrent requests.

### Cache freshness and concurrency

The inventory list is cached for 30 seconds, but inventory and order writes do not invalidate it. Concurrent misses can also execute the same query more than once. Separate application instances maintain separate caches.

- Evidence: [inventory cache](../LogiTrack/Controllers/InventoryController.cs) and [performance notes](performance.md).
- Recommendation: establish the acceptable freshness requirement. If immediate freshness is needed, coordinate invalidation for every inventory-affecting write and account for concurrent fills and multiple instances.

### Database location and durability

The SQLite connection uses a path relative to the process working directory. Running from a different directory can open a different database. A database file in ephemeral storage can disappear on deployment or replacement.

- Evidence: [database context](../LogiTrack/Data/LogiTrackContext.cs) and [persistence notes](state-persistence.md).
- Recommendation: configure an explicit database location, use persistent storage and SQLite-aware backups, and test restore and migration procedures. Assess whether SQLite fits the expected multi-instance and write-load requirements.

## Redundant or Demo Logic

- The template `/weatherforecast` endpoint appears unrelated to the inventory API ([Program.cs](../LogiTrack/Program.cs)). Remove it if it is not part of the intended public contract.
- `InventoryItem.DisplayInfo` and the order display/helper methods have no application call sites found in the review ([InventoryItem.cs](../LogiTrack/Models/InventoryItem.cs), [Order.cs](../LogiTrack/Models/Order.cs)). Keep them if they are still useful for learning; otherwise consider removing them.
- Similar response mappings occur in several actions. A shared mapper may help if the mappings grow, but a new abstraction is not necessary solely to remove a few short expressions.

## Additional Production Checklist

- Add automated integration tests for authentication, authorization, validation, caching, transactions, and persistence; run them in CI.
- Add consistent error responses, structured logs, metrics, tracing, and health/readiness checks without exposing sensitive data.
- Manage JWT signing keys and other secrets outside source control, with rotation and environment-specific configuration.
- Scan dependencies for vulnerabilities and define an update policy.
- Define API compatibility/versioning, deployment configuration, and a tested backup/restore plan.
- Use UTC-aware timestamps and define the expected date/time contract for clients.

## Review Notes

The application-source performance scan found no sync-over-async or `async void` matches. The existing [benchmark](performance.md) measures in-process cache behavior, not end-to-end HTTP throughput or production load. No test project was found during the review. The review did not make code changes; the validation and foreign-key findings were checked against an isolated database.