using System.Diagnostics;
using LogiTrack.Controllers;
using LogiTrack.Data;
using LogiTrack.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;

const int itemCount = 10_000;
const int samples = 50;
var originalDirectory = Directory.GetCurrentDirectory();
var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"logitrack-cache-benchmark-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryDirectory);

try
{
    Directory.SetCurrentDirectory(temporaryDirectory);
    await using (var context = new LogiTrackContext())
    {
        await context.Database.EnsureCreatedAsync();
        context.InventoryItems.AddRange(Enumerable.Range(1, itemCount).Select(number => new InventoryItem
        {
            Name = $"Item {number}",
            Quantity = number,
            Location = "Benchmark warehouse"
        }));
        await context.SaveChangesAsync();
    }

    using var subscription = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver());
    var clock = new TestClock();
    using var cache = new MemoryCache(new MemoryCacheOptions { Clock = clock });
    var baseline = await ReadUncachedAsync();
    var initial = await ReadCachedAsync(cache);
    Check(SameItems(baseline, initial), "Initial cached result differs from the database query.");

    CommandObserver.Count = 0;
    var hit = await ReadCachedAsync(cache);
    Check(SameItems(baseline, hit) && CommandObserver.Count == 0, "Cache hit queried the database or changed data.");

    int addedId;
    await using (var context = new LogiTrackContext())
    {
        var item = new InventoryItem { Name = "Refresh verification", Quantity = 1, Location = "Test" };
        context.InventoryItems.Add(item);
        await context.SaveChangesAsync();
        addedId = item.ItemId;
    }

    clock.UtcNow = clock.UtcNow.AddSeconds(29);
    Check((await ReadCachedAsync(cache)).All(item => item.ItemId != addedId), "Entry expired before 30 seconds.");
    clock.UtcNow = clock.UtcNow.AddSeconds(1);
    CommandObserver.Count = 0;
    var afterExpiry = await ReadCachedAsync(cache);
    Check(CommandObserver.Count == 1 && afterExpiry.Any(item => item.ItemId == addedId), "Cache did not refresh after 30 seconds.");

    await using (var context = new LogiTrackContext())
    {
        await context.InventoryItems.Where(item => item.ItemId == addedId).ExecuteDeleteAsync();
    }
    Check((await ReadCachedAsync(cache)).Any(item => item.ItemId == addedId), "Cached item changed before expiration.");
    clock.UtcNow = clock.UtcNow.AddSeconds(30);
    CommandObserver.Count = 0;
    var afterDelete = await ReadCachedAsync(cache);
    Check(CommandObserver.Count == 1 && afterDelete.All(item => item.ItemId != addedId), "Deleted item remained after refresh.");

    for (var index = 0; index < 5; index++)
    {
        await ReadUncachedAsync();
        await ReadCachedAsync(cache);
    }

    var uncachedTimes = new List<double>(samples);
    var cachedTimes = new List<double>(samples);
    var uncachedCommands = 0;
    var cachedCommands = 0;
    for (var index = 0; index < samples; index++)
    {
        CommandObserver.Count = 0;
        var stopwatch = Stopwatch.StartNew();
        var uncached = await ReadUncachedAsync();
        stopwatch.Stop();
        uncachedTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
        uncachedCommands += CommandObserver.Count;

        CommandObserver.Count = 0;
        stopwatch.Restart();
        var cached = await ReadCachedAsync(cache);
        stopwatch.Stop();
        cachedTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
        cachedCommands += CommandObserver.Count;
        Check(SameItems(uncached, cached), "Measured results do not match.");
    }

    Check(uncachedCommands == samples && cachedCommands == 0, "Unexpected SQL command count during measurement.");
    Console.WriteLine($"Items: {itemCount}; samples per path: {samples}; warmup iterations: 5");
    PrintResults("Uncached query", uncachedTimes, uncachedCommands);
    PrintResults("Cached controller", cachedTimes, cachedCommands);
    Console.WriteLine("PASS: matching data, no SQL on a hit, and additions/deletions refresh at expiration.");
}
finally
{
    Directory.SetCurrentDirectory(originalDirectory);
    SqliteConnection.ClearAllPools();
    Directory.Delete(temporaryDirectory, recursive: true);
}

static async Task<List<InventoryItemResponse>> ReadUncachedAsync()
{
    await using var context = new LogiTrackContext();
    return await context.InventoryItems.AsNoTracking()
        .Select(item => new InventoryItemResponse(item.ItemId, item.OrderId, item.Name, item.Quantity, item.Location))
        .ToListAsync();
}

static async Task<List<InventoryItemResponse>> ReadCachedAsync(IMemoryCache cache)
{
    await using var context = new LogiTrackContext();
    var result = await new InventoryController(context, cache).GetInventory(CancellationToken.None);
    return result.Result is OkObjectResult { Value: List<InventoryItemResponse> items }
        ? items
        : throw new InvalidOperationException("Inventory GET did not return a list.");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static bool SameItems(IEnumerable<InventoryItemResponse> left, IEnumerable<InventoryItemResponse> right)
    => left.OrderBy(item => item.ItemId).SequenceEqual(right.OrderBy(item => item.ItemId));

static void PrintResults(string label, List<double> timings, int commands)
{
    timings.Sort();
    var median = (timings[(timings.Count - 1) / 2] + timings[timings.Count / 2]) / 2;
    var percentile95 = timings[(int)Math.Ceiling(timings.Count * 0.95) - 1];
    Console.WriteLine($"{label}: median {median:F3} ms; p95 {percentile95:F3} ms; SQL commands {commands}");
}

sealed class TestClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}

sealed class ListenerObserver : IObserver<DiagnosticListener>
{
    public void OnCompleted() { }
    public void OnError(Exception error) { }
    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == "Microsoft.EntityFrameworkCore") listener.Subscribe(new CommandObserver());
    }
}

sealed class CommandObserver : IObserver<KeyValuePair<string, object?>>
{
    public static int Count { get; set; }
    public void OnCompleted() { }
    public void OnError(Exception error) { }
    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Value is CommandExecutedEventData && value.Key.EndsWith("CommandExecuted")) Count++;
    }
}