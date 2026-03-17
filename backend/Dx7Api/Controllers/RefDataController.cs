using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Dx7Api.Data;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/refdata")]
[Authorize]
public class RefDataController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// GET /api/refdata?category=Hl7Status
    /// Returns all active RefData entries for the given category,
    /// ordered by SortOrder then Code.
    /// Omit ?category to get all entries.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? category)
    {
        var query = db.RefData.AsNoTracking().Where(r => r.IsActive);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(r => r.Category == category);

        var results = await query
            .OrderBy(r => r.Category)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.Code)
            .Select(r => new
            {
                r.Category,
                r.Code,
                r.Label,
                r.Description,
                r.SortOrder
            })
            .ToListAsync();

        return Ok(results);
    }
}
