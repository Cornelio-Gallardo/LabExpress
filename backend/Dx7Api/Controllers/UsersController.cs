using Dx7Api.Data;
using Dx7Api.DTOs;
using Dx7Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : TenantBaseController
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    public UsersController(AppDbContext db, IWebHostEnvironment env) { _db = db; _env = env; }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? clientId)
    {
        if (!IsPlAdmin && !IsClinicAdmin)
            return Forbid();

        var query = _db.Users
            .Include(u => u.Client)
            .Where(u => u.TenantId == TenantId);

        if (IsClinicAdmin && ClientId.HasValue)
            query = query.Where(u => u.ClientId == ClientId.Value);

        if (clientId.HasValue && IsPlAdmin)
            query = query.Where(u => u.ClientId == clientId.Value);

        var users = await query.OrderBy(u => u.Name).ToListAsync();

        return Ok(users.Select(u => new UserDetailDto(
            u.Id, u.Name, u.Email, u.Role.ToString(),
            u.TenantId, u.ClientId,
            u.Client == null ? null : u.Client.Name,
            u.IsActive, u.CreatedAt, u.AvatarUrl
        )));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();

        var u = await _db.Users.Include(u => u.Client)
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == TenantId);
        if (u == null) return NotFound();

        return Ok(new UserDetailDto(
            u.Id, u.Name, u.Email, u.Role.ToString(),
            u.TenantId, u.ClientId,
            u.Client == null ? null : u.Client.Name,
            u.IsActive, u.CreatedAt, u.AvatarUrl
        ));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();

        if (IsClinicAdmin && (req.Role == "pl_admin" || req.Role == "sysad"))
            return Forbid();

        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return BadRequest(new { message = "Email already exists" });

        if (!Enum.TryParse<UserRole>(req.Role, out var role))
            return BadRequest(new { message = "Invalid role" });

        var user = new User
        {
            TenantId = TenantId,
            ClientId = req.ClientId ?? ClientId,
            Email = req.Email,
            Name = req.Name,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = role,
            IsActive = true
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = user.Id },
            new UserDetailDto(user.Id, user.Name, user.Email, user.Role.ToString(),
                user.TenantId, user.ClientId, null, user.IsActive, user.CreatedAt, null));
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest req)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == TenantId);
        if (user == null) return NotFound();

        var userRoleStr = user.Role.ToString();
        if (IsClinicAdmin && (userRoleStr == "pl_admin" || userRoleStr == "sysad"))
            return Forbid();

        if (!string.IsNullOrEmpty(req.Name))
            user.Name = req.Name;

        if (!string.IsNullOrEmpty(req.Email))
        {
            if (await _db.Users.AnyAsync(u => u.Email == req.Email && u.Id != id))
                return BadRequest(new { message = "Email already in use" });
            user.Email = req.Email;
        }

        if (!string.IsNullOrEmpty(req.Role))
        {
            if (!Enum.TryParse<UserRole>(req.Role, out var parsedRole))
                return BadRequest(new { message = "Invalid role" });
            if (IsClinicAdmin && (req.Role == "pl_admin" || req.Role == "sysad"))
                return Forbid();
            user.Role = parsedRole;
        }

        if (!string.IsNullOrEmpty(req.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);

        if (req.ClientId.HasValue)
            user.ClientId = req.ClientId;

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == TenantId);
        if (user == null) return NotFound();

        if (user.Id == CurrentUserId)
            return BadRequest(new { message = "Cannot deactivate your own account" });

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == TenantId);
        if (user == null) return NotFound();

        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("me/password")]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangePasswordRequest req)
    {
        var user = await _db.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "Current password is incorrect" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/users/{id}/avatar
    [HttpPost("{id}/avatar")]
    public async Task<IActionResult> UploadAvatar(Guid id, IFormFile file)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == TenantId);
        if (user == null) return NotFound();

        if (file == null || file.Length == 0) return BadRequest("No file provided");
        if (file.Length > 2 * 1024 * 1024) return BadRequest("File too large (max 2MB)");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(ext))
            return BadRequest("Only JPG, PNG, or WebP allowed");

        var wwwroot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var avatarsDir = Path.Combine(wwwroot, "avatars");
        Directory.CreateDirectory(avatarsDir);

        // Delete old avatar if exists
        if (!string.IsNullOrEmpty(user.AvatarUrl))
        {
            var oldPath = Path.Combine(wwwroot, user.AvatarUrl.TrimStart('/'));
            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        }

        var filename = $"{user.Id}{ext}";
        var filePath = Path.Combine(avatarsDir, filename);

        using (var stream = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(stream);

        user.AvatarUrl = $"/avatars/{filename}";
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { avatarUrl = user.AvatarUrl });
    }

    // DELETE /api/users/{id}/avatar
    [HttpDelete("{id}/avatar")]
    public async Task<IActionResult> DeleteAvatar(Guid id)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == TenantId);
        if (user == null) return NotFound();

        if (!string.IsNullOrEmpty(user.AvatarUrl))
        {
            var wwwroot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var oldPath = Path.Combine(wwwroot, user.AvatarUrl.TrimStart('/'));
            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
            user.AvatarUrl = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return NoContent();
    }
}