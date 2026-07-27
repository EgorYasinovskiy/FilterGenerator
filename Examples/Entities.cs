namespace Examples;

public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public List<ProductTag> Tags { get; set; } = new();
}

public class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public class ProductTag
{
    public Guid Id { get; set; }
    public string Tag { get; set; } = "";
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
}
