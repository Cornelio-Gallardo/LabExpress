using Dx7Api.Data;
using Dx7Api.DTOs;
using Dx7Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/clinics")]
public class ClinicsController : TenantBaseController
{
    private readonly AppDbContext _db;
    public ClinicsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();

        var query = _db.Clients.Where(c => c.TenantId == TenantId);

        if (IsClinicAdmin && ClientId.HasValue)
            query = query.Where(c => c.Id == ClientId.Value);

        var clients = await query.OrderBy(c => c.Name).ToListAsync();

        var userCounts = await _db.Users
            .Where(u => u.TenantId == TenantId && u.ClientId != null)
            .GroupBy(u => u.ClientId!.Value)
            .Select(g => new { ClientId = g.Key, Count = g.Count() })
            .ToListAsync();

        var countDict = userCounts.ToDictionary(x => x.ClientId, x => x.Count);

        return Ok(clients.Select(c => new {
            c.Id, c.Name, c.Code, c.Address, c.LogoUrl, c.IsActive,
            UserCount = countDict.TryGetValue(c.Id, out var cnt) ? cnt : 0
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClinicRequest req)
    {
        if (!IsPlAdmin) return Forbid();

        if (await _db.Clients.AnyAsync(c => c.TenantId == TenantId && c.Code == req.Code))
            return BadRequest(new { message = "Clinic code already exists" });

        var client = new Client
        {
            TenantId = TenantId,
            Name = req.Name,
            Code = req.Code.ToUpper(),
            Address = req.Address,
            LogoUrl = req.LogoUrl,
            IsActive = true
        };

        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new { id = client.Id }, new { client.Id });
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateClinicRequest req)
    {
        if (!IsPlAdmin) return Forbid();

        var client = await _db.Clients
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId);
        if (client == null) return NotFound();

        if (req.Name != null) client.Name = req.Name;
        if (req.Code != null) client.Code = req.Code.ToUpper();
        if (req.Address != null) client.Address = req.Address;
        if (req.LogoUrl != null) client.LogoUrl = req.LogoUrl;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        if (!IsPlAdmin) return Forbid();

        var client = await _db.Clients
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId);
        if (client == null) return NotFound();

        client.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        if (!IsPlAdmin) return Forbid();

        var client = await _db.Clients
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId);
        if (client == null) return NotFound();

        client.IsActive = true;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // PATCH /api/clinics/{id}/branding — Clinic Admin or PL Admin updates clinic branding
    [HttpPatch("{id}/branding")]
    public async Task<IActionResult> UpdateBranding(Guid id, [FromBody] UpdateClinicBrandingRequest req)
    {
        // PL Admin can update any clinic; Clinic Admin can only update their own
        if (!IsPlAdmin && !(IsClinicAdmin && ClientId == id)) return Forbid();

        var client = await _db.Clients
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId);
        if (client == null) return NotFound();

        if (req.LogoUrl != null) client.LogoUrl = req.LogoUrl;
        client.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { client.Id, client.Name, client.LogoUrl, client.IsActive });
    }
}
