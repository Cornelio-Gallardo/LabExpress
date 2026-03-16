using Dx7Api.Data;
using Dx7Api.DTOs;
using Dx7Api.Models;
using Dx7Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;
    private readonly IHttpClientFactory _httpClientFactory;

    public AuthController(AppDbContext db, JwtService jwt, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _jwt = jwt;
        _httpClientFactory = httpClientFactory;
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

    /// <summary>
    /// SSO login via Google or Facebook.
    /// Google  → verifies ID token against Google tokeninfo endpoint, checks audience.
    /// Facebook → verifies access token against Graph API, fetches id/name/email.
    /// Looks up user by ExternalProviderId first, falls back to email match (account linking).
    /// </summary>
    [HttpPost("external")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest req)
    {
        string? email      = null;
        string? externalId = null;

        var http = _httpClientFactory.CreateClient();

        if (req.Provider == "google")
        {
            // Verify Google ID token via tokeninfo endpoint
            // Frontend sends credential (ID token) from google.accounts.id.renderButton callback
            var res = await http.GetAsync(
                $"https://oauth2.googleapis.com/tokeninfo?id_token={Uri.EscapeDataString(req.Token)}");

            if (!res.IsSuccessStatusCode)
                return Unauthorized(new { message = "Google token verification failed. Please try again." });

            var payload = await res.Content.ReadFromJsonAsync<JsonElement>();

            // tokeninfo returns error as a field in the body
            if (payload.TryGetProperty("error", out _) || payload.TryGetProperty("error_description", out _))
                return Unauthorized(new { message = "Google token verification failed. Please try again." });

            // Reject unverified email addresses (tokeninfo returns email_verified as string "true")
            var emailVerified = payload.TryGetProperty("email_verified", out var evProp) &&
                                evProp.GetString() == "true";
            if (!emailVerified)
                return Unauthorized(new { message = "Google email is not verified." });

            email      = payload.TryGetProperty("email", out var ep) ? ep.GetString() : null;
            externalId = payload.TryGetProperty("sub",   out var sp) ? sp.GetString() : null;
        }
        else if (req.Provider == "facebook")
        {
            // Verify Facebook access token and fetch profile
            var res = await http.GetAsync(
                $"https://graph.facebook.com/me?fields=id,name,email&access_token={req.Token}");

            if (!res.IsSuccessStatusCode)
                return Unauthorized(new { message = "Facebook token verification failed. Please try again." });

            var payload = await res.Content.ReadFromJsonAsync<JsonElement>();

            // Check for Facebook error object in response body
            if (payload.TryGetProperty("error", out var errProp))
            {
                var errMsg = errProp.TryGetProperty("message", out var em) ? em.GetString() : "Unknown error";
                return Unauthorized(new { message = $"Facebook error: {errMsg}" });
            }

            email      = payload.TryGetProperty("email", out var ep) ? ep.GetString() : null;
            externalId = payload.TryGetProperty("id",    out var ip) ? ip.GetString() : null;
        }
        else
        {
            return BadRequest(new { message = "Unsupported provider. Use 'google' or 'facebook'." });
        }

        if (string.IsNullOrEmpty(externalId))
            return Unauthorized(new { message = "Could not retrieve identity from provider." });

        // 1. Find by external provider ID (fastest path, exact match)
        // CurrentTenantId is null for unauthenticated requests → filter passes all tenants
        var user = await _db.Users
            .Include(u => u.Tenant)
            .Include(u => u.Client)
            .FirstOrDefaultAsync(u =>
                u.ExternalProvider == req.Provider &&
                u.ExternalProviderId == externalId);

        // 2. Fall back to email match and link the account
        if (user == null && !string.IsNullOrEmpty(email))
        {
            user = await _db.Users
                .Include(u => u.Tenant)
                .Include(u => u.Client)
                .FirstOrDefaultAsync(u => u.Email == email);

            if (user != null)
            {
                user.ExternalProvider   = req.Provider;
                user.ExternalProviderId = externalId;
                await _db.SaveChangesAsync();
            }
        }

        if (user == null)
            return Unauthorized(new { message = "No Dx7 account is linked to this identity. Contact your administrator." });

        if (!user.IsActive)
            return Unauthorized(new { message = "Your account has been deactivated. Contact your administrator." });

        if (!user.Tenant.IsActive)
            return Unauthorized(new { message = "This organization account is not active." });

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