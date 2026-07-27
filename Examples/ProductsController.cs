using Microsoft.AspNetCore.Mvc;

namespace Examples;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductsController(AppDbContext db) => _db = db;

    /// <summary>
    /// GET /api/products?name=Widget&amp;minPrice=10&amp;maxPrice=100&amp;categoryId=...
    /// All parameters are optional — only non-null values are applied as filters.
    /// </summary>
    [HttpGet]
    public IActionResult Get([FromQuery] ProductFilterParams filter)
    {
        var results = _db.Products.Apply(filter).ToList();
        return Ok(results);
    }

    /// <summary>
    /// POST /api/products/filter
    /// Accepts the same filter as JSON body — useful when query strings get too long.
    /// Maybe soon it will be a HttpQuery attribute to pass body for queries and not set it as POST method.
    /// </summary>
    [HttpPost("filter")]
    public IActionResult Filter([FromBody] ProductFilterParams filter)
    {
        var results = _db.Products.Apply(filter).ToList();
        return Ok(results);
    }
}
