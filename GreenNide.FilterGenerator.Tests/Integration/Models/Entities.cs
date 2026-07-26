namespace GreenNide.ExpressionFilter.Tests.Integration.Models;

public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public class Order
{
    public Guid Id { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public List<OrderItem> OrderItems { get; set; } = new();
    public List<OrderHistory> History { get; set; } = new();
}

public class OrderItem
{
    public Guid Id { get; set; }
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
}

public class OrderHistory
{
    public Guid Id { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime Timestamp { get; set; }
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
}

public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}