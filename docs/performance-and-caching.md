# Performance and Caching

## Query Optimizations

- Read-only EF Core queries use `AsNoTracking()` to skip change tracking.
- List actions project only the fields required by their response records.
- Order details load related inventory items in one SQL query.
- Order deletion uses a bulk update to detach related items followed by a bulk delete, inside one transaction. A diagnostic run with three items observed a reduction from five SQL commands to two.

The detail query's earlier projection also ran as one query; using `Include` makes eager loading explicit but was not a reduction in query count. The benchmark results and methodology are in [performance.md](performance.md).

## Inventory List Cache

`GET /api/inventory` uses `IMemoryCache` with the key `inventory:all` and a 30-second absolute expiration. On a cache miss, the API queries SQLite and caches the projected list. On a hit, it returns the cached list without issuing SQL. The individual inventory-item endpoint is not cached.

The cached response is shared because the current list is the same for every authenticated user. If the API later filters by user or tenant, the cache key must include that identity so one user's result cannot be returned to another.

## Freshness and State

The cache is an optimization, not durable storage. SQLite remains the source of truth. The cache is process-local, disappears on restart, and is not shared across instances. Writes do not currently invalidate it, so a change can remain absent from the list for up to 30 seconds. A cache entry may be evicted early, and simultaneous misses may issue duplicate queries.

If the product requires immediate read-after-write consistency, invalidate or update the cache after every inventory-affecting write, including order creation and deletion. For multiple application instances, use a shared cache or an explicit invalidation mechanism if consistent cache behavior is required.

## Benchmark

Run the repeatable benchmark from the repository root:

```powershell
dotnet run --configuration Release --project tools/InventoryCacheBenchmark/InventoryCacheBenchmark.csproj
```

It creates an isolated temporary SQLite database with 10,000 rows, performs five warmups, and collects 50 alternating samples for the uncached EF query and the cached controller action. `Stopwatch` measures elapsed in-process time; EF Core diagnostic events count database commands. The tool also verifies identical returned data, zero SQL on a cache hit, and refresh after simulated expiration following additions and deletions.

One local Release run on Windows with .NET SDK 10.0.113 and an Intel Core i7-11850H measured:

| Path | Median | 95th percentile | SQL commands |
| --- | ---: | ---: | ---: |
| Uncached query | 16.857 ms | 33.298 ms | 50 |
| Cached controller | 0.059 ms | 0.092 ms | 0 |

These are machine-specific in-process figures, not end-to-end HTTP latency or a production load test. Each cached sample includes context and controller construction while the baseline is the direct query, so the comparison isolates the benefit of avoiding the database query rather than comparing identical request pipelines.

## Further Optimization

The list endpoints currently return all rows. Add pagination with stable ordering before datasets grow. Use production-like load tests to decide whether additional indexes, cache invalidation, or a distributed cache are warranted. Avoid low-level allocation changes until profiling identifies them as a bottleneck.