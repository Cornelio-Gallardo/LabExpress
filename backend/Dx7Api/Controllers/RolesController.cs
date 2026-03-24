using Dx7Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/roles")]
public class RolesController : TenantBaseController
{
    private readonly AppDbContext _db;
    public RolesController(AppDbContext db) => _db = db;

    // All authenticated users can read roles (needed for user management dropdowns)
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var roles = await _db.RoleDefinitions
            .Where(r => r.TenantId == TenantId && r.IsActive)
            .OrderBy(r => r.SortOrder)
            .ToListAsync();

        return Ok(roles.Select(r => new {
            r.Id, r.RoleKey, r.Label, r.Description, r.IsActive, r.SortOrder
        }));
    }

}