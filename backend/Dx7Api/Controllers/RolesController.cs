using Dx7Api.DTOs;
using Dx7Api.Data;
using Dx7Api.Models;
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

    // PL Admin can create custom roles
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest req)
    {
        if (!IsPlAdmin) return Forbid();

        if (await _db.RoleDefinitions.AnyAsync(r => r.TenantId == TenantId && r.RoleKey == req.RoleKey))
            return BadRequest(new { message = "Role key already exists" });

        var role = new RoleDefinition
        {
            TenantId = TenantId,
            RoleKey = req.RoleKey,
            Label = req.Label,
            Description = req.Description,
            SortOrder = req.SortOrder,
            IsActive = true
        };

        _db.RoleDefinitions.Add(role);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new { id = role.Id }, new { role.Id });
    }

    // PL Admin can update role labels/descriptions
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoleRequest req)
    {
        if (!IsPlAdmin) return Forbid();

        var role = await _db.RoleDefinitions
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == TenantId);
        if (role == null) return NotFound();

        if (req.Label != null) role.Label = req.Label;
        if (req.Description != null) role.Description = req.Description;
        if (req.SortOrder.HasValue) role.SortOrder = req.SortOrder.Value;
        if (req.IsActive.HasValue) role.IsActive = req.IsActive.Value;

        await _db.SaveChangesAsync();
        return NoContent();
    }
}