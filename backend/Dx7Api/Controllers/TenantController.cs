using Dx7Api.Data;
using Dx7Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/tenant")]
public class TenantController : TenantBaseController
{
    private readonly AppDbContext _db;
    public TenantController(AppDbContext db) => _db = db;

    // GET /api/tenant — current tenant branding
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == TenantId);
        if (tenant == null) return NotFound();
        return Ok(new TenantDetailDto(
            tenant.Id, tenant.Name, tenant.Code,
            tenant.PrimaryColor, tenant.LogoUrl, tenant.FooterText,
            tenant.IsActive));
    }

    // PATCH /api/tenant/branding — PL Admin updates tenant branding
    [HttpPatch("branding")]
    public async Task<IActionResult> UpdateBranding([FromBody] UpdateTenantBrandingRequest req)
    {
        if (!IsPlAdmin) return Forbid();

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == TenantId);
        if (tenant == null) return NotFound();

        if (req.PrimaryColor != null) tenant.PrimaryColor = req.PrimaryColor;
        if (req.LogoUrl      != null) tenant.LogoUrl      = req.LogoUrl;
        if (req.FooterText   != null) tenant.FooterText   = req.FooterText;
        tenant.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new TenantDetailDto(
            tenant.Id, tenant.Name, tenant.Code,
            tenant.PrimaryColor, tenant.LogoUrl, tenant.FooterText,
            tenant.IsActive));
    }

    // ── SXA catalogs (read-only, for dropdowns) ───────────────────────────────

    [HttpGet("sxa-tests")]
    public async Task<IActionResult> GetSxaTests()
    {
        var list = await _db.SxaTests.Where(t => t.ActiveFlag)
            .OrderBy(t => t.Category).ThenBy(t => t.CanonicalName)
            .Select(t => new SxaTestDto(t.SxaTestId, t.CanonicalName, t.Category))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("sxa-analytes")]
    public async Task<IActionResult> GetSxaAnalytes()
    {
        var list = await _db.SxaAnalytes
            .OrderBy(a => a.DisplayName)
            .Select(a => new SxaAnalyteDto(a.AnalyteCode, a.DisplayName, a.DefaultUnit))
            .ToListAsync();
        return Ok(list);
    }

    // ── Test mappings (OBR-4) ─────────────────────────────────────────────────

    [HttpGet("test-maps")]
    public async Task<IActionResult> GetTestMaps()
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();
        var list = await _db.TenantTestMaps
            .Include(m => m.SxaTest)
            .Where(m => m.TenantId == TenantId)
            .OrderBy(m => m.TenantTestCode)
            .Select(m => new TestMapDto(m.Id, m.TenantTestCode, m.SxaTestId, m.SxaTest.CanonicalName, m.IsActive))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("test-maps")]
    public async Task<IActionResult> CreateTestMap([FromBody] CreateTestMapRequest req)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();
        if (string.IsNullOrWhiteSpace(req.TenantTestCode) || string.IsNullOrWhiteSpace(req.SxaTestId))
            return BadRequest("TenantTestCode and SxaTestId are required.");

        var code = req.TenantTestCode.Trim().ToUpper();
        var exists = await _db.TenantTestMaps.AnyAsync(m =>
            m.TenantId == TenantId && m.TenantTestCode == code);
        if (exists) return Conflict($"Mapping for '{code}' already exists.");

        var sxa = await _db.SxaTests.FindAsync(req.SxaTestId);
        if (sxa == null) return BadRequest($"SXA test '{req.SxaTestId}' not found.");

        var map = new Models.TenantTestMap
        {
            Id             = Guid.NewGuid(),
            TenantId       = TenantId,
            TenantTestCode = code,
            SxaTestId      = req.SxaTestId,
            IsActive       = true
        };
        _db.TenantTestMaps.Add(map);
        await _db.SaveChangesAsync();
        return Ok(new TestMapDto(map.Id, map.TenantTestCode, map.SxaTestId, sxa.CanonicalName, map.IsActive));
    }

    [HttpDelete("test-maps/{id:guid}")]
    public async Task<IActionResult> DeleteTestMap(Guid id)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();
        var map = await _db.TenantTestMaps.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == TenantId);
        if (map == null) return NotFound();
        _db.TenantTestMaps.Remove(map);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Analyte mappings (OBX-3) ──────────────────────────────────────────────

    [HttpGet("analyte-maps")]
    public async Task<IActionResult> GetAnalyteMaps()
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();
        var list = await _db.TenantAnalyteMaps
            .Include(m => m.Analyte)
            .Where(m => m.TenantId == TenantId)
            .OrderBy(m => m.TenantAnalyteCode)
            .Select(m => new AnalyteMapDto(m.Id, m.TenantAnalyteCode, m.AnalyteCode, m.Analyte.DisplayName, m.IsActive))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("analyte-maps")]
    public async Task<IActionResult> CreateAnalyteMap([FromBody] CreateAnalyteMapRequest req)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();
        if (string.IsNullOrWhiteSpace(req.TenantAnalyteCode) || string.IsNullOrWhiteSpace(req.AnalyteCode))
            return BadRequest("TenantAnalyteCode and AnalyteCode are required.");

        var code = req.TenantAnalyteCode.Trim().ToUpper();
        var exists = await _db.TenantAnalyteMaps.AnyAsync(m =>
            m.TenantId == TenantId && m.TenantAnalyteCode == code);
        if (exists) return Conflict($"Mapping for '{code}' already exists.");

        var sxa = await _db.SxaAnalytes.FindAsync(req.AnalyteCode);
        if (sxa == null) return BadRequest($"SXA analyte '{req.AnalyteCode}' not found.");

        var map = new Models.TenantAnalyteMap
        {
            Id                = Guid.NewGuid(),
            TenantId          = TenantId,
            TenantAnalyteCode = code,
            AnalyteCode       = req.AnalyteCode,
            IsActive          = true
        };
        _db.TenantAnalyteMaps.Add(map);
        await _db.SaveChangesAsync();
        return Ok(new AnalyteMapDto(map.Id, map.TenantAnalyteCode, map.AnalyteCode, sxa.DisplayName, map.IsActive));
    }

    [HttpDelete("analyte-maps/{id:guid}")]
    public async Task<IActionResult> DeleteAnalyteMap(Guid id)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();
        var map = await _db.TenantAnalyteMaps.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == TenantId);
        if (map == null) return NotFound();
        _db.TenantAnalyteMaps.Remove(map);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
