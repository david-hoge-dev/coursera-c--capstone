# State Persistence

## Chosen strategy

LogiTrack uses its SQLite database as the durable source of truth for inventory, orders, their item relationships, Identity users, and roles. Successful changes are written to the database through Entity Framework Core. Unlike an in-memory cache, the database can retain that state when the application process stops and starts again.

The 30-second inventory cache remains a performance optimization, not permanent storage. It is cleared on restart and repopulated from SQLite on the next inventory-list request. Writes do not currently invalidate the cache, so the list can temporarily show an older snapshot; see [performance.md](performance.md).

## Database location and operation

The context uses `Data Source=logitrack.db`, a relative SQLite path. SQLite resolves it relative to the process's working directory. Start the application and run migrations from the `LogiTrack` project directory to keep using its existing database:

```powershell
Set-Location LogiTrack
dotnet ef database update
dotnet run --launch-profile http
```

The database schema must be migrated before starting the application. The app does not automatically add a sample inventory item on startup, so an intentionally empty inventory stays empty.

For deployment, the SQLite file must be on persistent storage; a file inside an ephemeral container filesystem can disappear when the container is replaced. Back up the database using a SQLite-aware backup procedure. A local file alone does not provide replication or shared state across application instances. The JWT signing key must also be configured securely for each environment and kept stable if existing tokens should remain valid across restarts.

## Restart verification

An isolated, freshly migrated SQLite database was used to verify the API without changing the project's data:

1. Registered a user, assigned the Manager role, and created an inventory item and an order with two linked items.
2. Restarted the application against the same database. Login and Manager access worked, and the inventory, order, and item relationships were restored.
3. Deleted the order and all test inventory items, then restarted again. The deleted order remained absent and inventory remained empty.

The test demonstrates process-restart persistence when the same SQLite file is retained. It does not establish backup recovery, storage durability after a hardware failure, or behavior across multiple hosts.

## Temporary orders and ownership

The current order model stores orders in SQLite but has no draft status, session identifier, or user owner. Authenticated users can currently read all orders, while Managers can create or delete them. No per-user cart or authorization boundary is implemented.

If per-user drafts are added later, associate each draft with an authenticated `ApplicationUser` ID, model its draft status, and enforce ownership in queries and authorization. For anonymous carts, use a securely generated session identifier with an explicit lifetime and appropriate protection. Do not store raw JWTs or use them as order identifiers.