using LogiTrack.Data;
using LogiTrack.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LogiTrack.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryController : ControllerBase
{
    private readonly LogiTrackContext _context;
    private readonly IMemoryCache _cache;

    public InventoryController(LogiTrackContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<InventoryItemResponse>>> GetInventory(CancellationToken cancellationToken)
    {
        var items = await _cache.GetOrCreateAsync("inventory:all", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await _context.InventoryItems.AsNoTracking()
                .Select(item => new InventoryItemResponse(item.ItemId, item.OrderId, item.Name, item.Quantity, item.Location))
                .ToListAsync(cancellationToken);
        });
        return Ok(items);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<InventoryItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InventoryItemResponse>> GetInventoryItem(int id, CancellationToken cancellationToken)
    {
        var item = await _context.InventoryItems.AsNoTracking()
            .Where(item => item.ItemId == id)
            .Select(item => new InventoryItemResponse(item.ItemId, item.OrderId, item.Name, item.Quantity, item.Location))
            .FirstOrDefaultAsync(cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType<InventoryItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<InventoryItemResponse>> AddInventoryItem(CreateInventoryItemRequest request, CancellationToken cancellationToken)
    {
        var item = new InventoryItem
        {
            OrderId = request.OrderId,
            Name = request.Name,
            Quantity = request.Quantity,
            Location = request.Location
        };
        _context.InventoryItems.Add(item);
        await _context.SaveChangesAsync(cancellationToken);

        var response = new InventoryItemResponse(item.ItemId, item.OrderId, item.Name, item.Quantity, item.Location);
        return CreatedAtAction(nameof(GetInventoryItem), new { id = item.ItemId }, response);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Manager")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteInventoryItem(int id, CancellationToken cancellationToken)
    {
        var item = await _context.InventoryItems.FindAsync([id], cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        _context.InventoryItems.Remove(item);
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

/// <summary>Details for a new inventory item.</summary>
public sealed record CreateInventoryItemRequest
{
    public int? OrderId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public string Location { get; init; } = string.Empty;
}

/// <summary>An inventory item returned by the API.</summary>
public sealed record InventoryItemResponse(int ItemId, int? OrderId, string Name, int Quantity, string Location);