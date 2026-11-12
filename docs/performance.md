# Performance Improvements

## Inventory cache

`GET /api/inventory` stores the projected inventory list in `IMemoryCache` under `inventory:all`. A cache miss queries SQLite; a hit returns the stored list without a database query. The entry expires 30 seconds after creation, and the next request repopulates it. The single-item GET is not cached.

The list is a shared snapshot for authenticated users. Inventory and order writes do not invalidate it, so additions, deletions, and changes to an item's `OrderId` may not appear until the next refresh. Each application process has a separate cache, and a restart clears it. Cache entries can also be evicted early. Concurrent misses can execute the query more than once.

## Measurement

Run the repeatable benchmark from the workspace root:

```powershell
dotnet run --configuration Release --project tools/InventoryCacheBenchmark/InventoryCacheBenchmark.csproj
```

The tool creates and deletes its own temporary SQLite database; it does not use or change the project's database. It creates 10,000 inventory items, warms both paths five times, and then alternates 50 measurements of:

- The original uncached EF Core projection, executed directly.
- The current `InventoryController.GetInventory` action with a populated cache.

Each sample includes constructing and disposing a context. The cached path also constructs the controller and returns an MVC result; the baseline does not. `Stopwatch` measures elapsed in-process time. EF Core diagnostic events count database commands. The comparison excludes HTTP, authentication, JSON serialization, and network time; it does not measure a cold cache fill or concurrent load.

One local run on Windows, .NET SDK 10.0.113, Release build, Intel Core i7-11850H:

| Path | Median | 95th percentile | SQL commands |
| --- | ---: | ---: | ---: |
| Uncached query | 16.857 ms | 33.298 ms | 50 |
| Cached controller | 0.059 ms | 0.092 ms | 0 |

The median is the middle sample; the 95th percentile is the sample at or below which 95% of the measurements fall. These are observations from one machine and dataset, not production latency guarantees. Results vary with hardware, data volume, load, and runtime conditions.

## Cache correctness checks

The benchmark compares cached results to the uncached query without relying on row order. It verifies that a cache hit issues no SQL, an added item is absent from the cached snapshot at 29 seconds and present after expiration at 30 seconds, and a deleted item disappears after the next expiration. A simulated cache clock makes those boundary checks deterministic; database changes are made directly in the isolated database.

An additional authenticated HTTP check confirmed that repeated list responses return the same JSON during the cache window, a newly created item is visible through the uncached single-item route, and an anonymous list request receives `401`.

## Order queries

Read-only order queries use `AsNoTracking()`. The order-detail action uses `Include()` to load its items in one SQL query. Its previous projection also used one query; the `Include()` change makes eager loading explicit rather than reducing the query count.

Deleting an order now uses a bulk update to detach all its inventory items and a bulk delete for the order, in one transaction. In a diagnostic run with three items, EF Core issued five database commands before the change and two afterward. Tests also checked missing and empty orders, preservation of inventory items, and transaction rollback when deletion fails. These command counts are not elapsed-time measurements.