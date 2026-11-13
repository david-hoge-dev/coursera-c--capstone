# Business Logic, Persistence, and State Management

## Application Structure

The application separates HTTP handling, persistence models, database access, and API response shapes:

- `Controllers` defines routes, authorization, request handling, and HTTP status codes.
- `Models` contains `Order`, `InventoryItem`, and `ApplicationUser`.
- `Data/LogiTrackContext` is the EF Core context for application data and Identity.
- Request and response records define the JSON-facing API shapes.

ASP.NET Core dependency injection creates controllers with their required services, including `LogiTrackContext`, `UserManager`, `SignInManager`, and `IMemoryCache`.

## Business Logic

Controllers map incoming request DTOs to domain entities, save changes with EF Core, and map results into response DTOs. For example, order creation builds an `Order` and its inventory items from one request, then saves them through the same context. This keeps database entities and navigation properties out of the API response contract.

The order delete operation preserves inventory items. It clears the item's nullable order foreign key, deletes the order, and commits both operations in one database transaction.

## Data Persistence

`LogiTrackContext` inherits from `IdentityDbContext<ApplicationUser>` and uses SQLite. EF Core migrations create and evolve the schema, including the Identity tables. `SaveChangesAsync` persists tracked additions and removals; the order deletion uses EF Core's immediate bulk-update and bulk-delete operations.

The SQLite file is durable only if the application continues to use the same path and the underlying storage is persistent. The configured relative path depends on the process working directory. See [state-persistence.md](state-persistence.md) for migration, restart, and deployment notes.

## Query and Response Design

Read-only queries use `AsNoTracking()` to avoid change-tracker work. Order details eagerly load items with `Include`. List actions project only fields needed by their response DTOs. The current list endpoints are unpaged, so large datasets can increase query duration, memory usage, and response size.

## State and Cache

SQLite is the source of truth. `IMemoryCache` holds one shared inventory-list snapshot under `inventory:all` for 30 seconds. A cache miss queries SQLite and stores the result; a hit avoids the database query. The single-item endpoint is not cached.

The cache is process-local, cleared on restart, and separate in each application instance. Inventory writes do not currently invalidate the list entry, so it may be stale until expiration. Cache behavior and measured results are documented in [performance.md](performance.md).

## Request Flow

For a protected controller action, ASP.NET Core validates the bearer token and role requirements before invoking the controller. The action then validates or maps request data, queries or changes the EF Core context, and returns an HTTP response containing the mapped DTO. More complete model validation and consistent handling of database constraint errors remain follow-up work.