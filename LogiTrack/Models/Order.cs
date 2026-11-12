namespace LogiTrack.Models;

public class Order
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime DatePlaced  { get; set; } = DateTime.Now;
    public ICollection<InventoryItem> Items { get; set; } = new List<InventoryItem>();

    public void AddItem(InventoryItem item)
    {
        Items.Add(item);
    }

    public void RemoveItem(InventoryItem item)
    {
        Items.Remove(item);
    }

    public string GetOrderSummary()
    {
        return $"Order #{OrderId} for {CustomerName} | Items: {Items.Count} | Placed: {DatePlaced}";
    }
}