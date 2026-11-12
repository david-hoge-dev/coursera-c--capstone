using LogiTrack.Data;
using LogiTrack.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LogiTrack.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrderController(LogiTrackContext context) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IEnumerable<OrderResponse>>> GetOrders(CancellationToken cancellationToken)
	{
		var orders = await context.Orders.AsNoTracking()
			.Select(order => new OrderResponse(order.OrderId, order.CustomerName, order.DatePlaced))
			.ToListAsync(cancellationToken);
		return Ok(orders);
	}

	[HttpGet("{id:int}")]
	[ProducesResponseType<OrderDetailsResponse>(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<ActionResult<OrderDetailsResponse>> GetOrder(int id, CancellationToken cancellationToken)
	{
		var order = await context.Orders.AsNoTracking()
			.Include(order => order.Items)
			.FirstOrDefaultAsync(order => order.OrderId == id, cancellationToken);
		if (order is null)
		{
			return NotFound();
		}

		var items = order.Items.Select(item => new InventoryItemResponse(
			item.ItemId, item.OrderId, item.Name, item.Quantity, item.Location)).ToList();
		return Ok(new OrderDetailsResponse(order.OrderId, order.CustomerName, order.DatePlaced, items));
	}

	[HttpPost]
	[Authorize(Roles = "Manager")]
	[ProducesResponseType<OrderDetailsResponse>(StatusCodes.Status201Created)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<OrderDetailsResponse>> CreateOrder(CreateOrderRequest request, CancellationToken cancellationToken)
	{
		if (request.Items.Any(item => item is null))
		{
			return BadRequest("Items cannot contain null values.");
		}

		var order = new Order
		{
			CustomerName = request.CustomerName,
			Items = request.Items.Select(item => new InventoryItem
			{
				Name = item.Name,
				Quantity = item.Quantity,
				Location = item.Location
			}).ToList()
		};
		context.Orders.Add(order);
		await context.SaveChangesAsync(cancellationToken);

		var items = order.Items.Select(item => new InventoryItemResponse(
			item.ItemId, item.OrderId, item.Name, item.Quantity, item.Location)).ToList();
		var response = new OrderDetailsResponse(order.OrderId, order.CustomerName, order.DatePlaced, items);
		return CreatedAtAction(nameof(GetOrder), new { id = order.OrderId }, response);
	}

	[HttpDelete("{id:int}")]
	[Authorize(Roles = "Manager")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public async Task<IActionResult> DeleteOrder(int id, CancellationToken cancellationToken)
	{
		await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
		await context.InventoryItems.Where(item => item.OrderId == id)
			.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.OrderId, (int?)null), cancellationToken);
		var deleted = await context.Orders.Where(order => order.OrderId == id)
			.ExecuteDeleteAsync(cancellationToken);
		if (deleted == 0)
		{
			return NotFound();
		}

		await transaction.CommitAsync(cancellationToken);
		return NoContent();
	}
}

/// <summary>Details for a new order.</summary>
public sealed record CreateOrderRequest
{
	public string CustomerName { get; init; } = string.Empty;
	public IReadOnlyList<CreateOrderItemRequest> Items { get; init; } = [];
}

/// <summary>Details for an inventory item added to a new order.</summary>
public sealed record CreateOrderItemRequest
{
	public string Name { get; init; } = string.Empty;
	public int Quantity { get; init; }
	public string Location { get; init; } = string.Empty;
}

/// <summary>An order returned in the order list.</summary>
public sealed record OrderResponse(int OrderId, string CustomerName, DateTime DatePlaced);

/// <summary>An order and its inventory items.</summary>
public sealed record OrderDetailsResponse(int OrderId, string CustomerName, DateTime DatePlaced, IReadOnlyList<InventoryItemResponse> Items);