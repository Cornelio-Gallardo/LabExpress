using Dx7Api.Data;
using Dx7Api.DTOs;
using Dx7Api.Models;
using Dx7Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;

    public AuthController(AppDbContext db, JwtService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    /// <summary>
    /// DEV ONLY: Reset all seeded user passwords back to defaults.
    /// Remove this endpoint before going to production.
    /// </summary>
    [HttpPost("reset-passwords")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> ResetPasswords()
    {
        // Reset ALL users to a single known password: Admin@1234
        const string newPassword = "Admin@1234";
        var newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

        var allUsers = await _db.Users.ToListAsync();
        foreach (var user in allUsers)
            user.PasswordHash = newHash;

        await _db.SaveChangesAsync();

        return Ok(new {
            message     = $"Reset {allUsers.Count} user(s) — all passwords are now: {newPassword}",
            userCount   = allUsers.Count,
            password    = newPassword,
            accounts    = allUsers.Select(u => new { u.Email, u.Role, u.IsActive })
        });
    }

    [HttpGet("debug/{email}")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> Debug(string email)
    {
        var user = await _db.Users
            .Include(u => u.Tenant)
            .Include(u => u.Client)
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
            return Ok(new { found = false, email, message = "No user with this email in DB" });

        // Test common passwords
        var passwords = new[] { "Admin@1234", "Nurse@1234", "Doctor@1234", "admin", "password", "Admin1234", "admin@1234" };
        var matches = passwords.Select(p => new {
            password = p,
            matches  = BCrypt.Net.BCrypt.Verify(p, user.PasswordHash)
        }).ToList();

        return Ok(new {
            found        = true,
            email        = user.Email,
            name         = user.Name,
            role         = user.Role,
            isActive     = user.IsActive,
            tenantActive = user.Tenant?.IsActive,
            clientActive = user.Client?.IsActive,
            hashPrefix   = user.PasswordHash[..20],
            passwordTests = matches
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users
            .Include(u => u.Tenant)
            .Include(u => u.Client)
            .FirstOrDefaultAsync(u => u.Email == req.Email);

        // Check user exists and password is correct
        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password" });

        // Check user account is active
        if (!user.IsActive)
            return Unauthorized(new { message = "Your account has been deactivated. Contact your administrator." });

        // Check tenant is active
        if (!user.Tenant.IsActive)
            return Unauthorized(new { message = "This organization account is not active." });

        // Check client (clinic) is active — only for clinic-scoped users
        if (user.Client != null && !user.Client.IsActive)
            return Unauthorized(new { message = "This clinic account is not active. Contact your PL administrator." });

        var token = _jwt.GenerateToken(user);

        return Ok(new LoginResponse(
            token,
            new UserDto(user.Id, user.Name, user.Email, user.Role.ToString(), user.TenantId, user.ClientId),
            new TenantDto(user.Tenant.Id, user.Tenant.Name, user.Tenant.PrimaryColor, user.Tenant.LogoUrl, user.Tenant.FooterText),
            user.Client == null ? null : new ClientDto(user.Client.Id, user.Client.Name, user.Client.LogoUrl, user.Client.Address)
        ));
    }
}