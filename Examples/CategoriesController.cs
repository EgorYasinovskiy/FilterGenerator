using Microsoft.AspNetCore.Mvc;

namespace Examples;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _db;

    public CategoriesController(AppDbContext db) => _db = db;

    // GET /api/categories?name=Electronics
    [HttpGet]
    public IActionResult Get([FromQuery] CategorySearchParams filter)
    {
        var results = _db.Categories.Apply(filter).ToList();
        return Ok(results);
    }
}
