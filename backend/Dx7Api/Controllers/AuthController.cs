using Dx7Api.Data;
using Dx7Api.DTOs;
using Dx7Api.Models;
using Dx7Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext db,
    JwtService jwt,
    IHttpClientFactory httpClientFactory,
    IConfiguration config) : ControllerBase
{
    private readonly AppDbContext       _db     = db;
    private readonly JwtService         _jwt    = jwt;
    private readonly IHttpClientFactory _http   = httpClientFactory;
    private readonly IConfiguration     _config = config;

    // ── Shared cookie options ─────────────────────────────────────────────────

    /// <summary>
    /// F-09: httpOnly cookie pattern.
    /// HttpOnly  — token is invisible to JavaScript (XSS-immune).
    /// Secure    — HTTPS only in production.
    /// SameSite  — Lax prevents CSRF for state-mutating requests sent via cross-site navigation.
    /// Path=/api — cookie is only sent to API routes, not served to asset requests.
    /// </summary>
    private CookieOptions TokenCookieOptions() => new()
    {
        HttpOnly = true,
        Secure   = !HttpContext.RequestServices
                       .GetRequiredService<IHostEnvironment>().IsDevelopment(),
        SameSite = SameSiteMode.Lax,
        Path     = "/api",
        Expires  = DateTimeOffset.UtcNow.AddHours(
                       double.Parse(_config["Jwt:ExpiryHours"] ?? "8"))
    };

    // ── POST /api/auth/login ──────────────────────────────────────────────────

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        // IgnoreQueryFilters: login is cross-tenant — no JWT yet, CurrentTenantId is null (fail-closed)
        var user = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.Tenant)
            .Include(u => u.Client)
            .FirstOrDefaultAsync(u => u.Email == req.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password" });

        if (!user.IsActive)
            return Unauthorized(new { message = "Your account has been deactivated. Contact your administrator." });

        if (!user.Tenant.IsActive)
            return Unauthorized(new { message = "This organization account is not active." });

        if (user.Client != null && !user.Client.IsActive)
            return Unauthorized(new { message = "This clinic account is not active. Contact your PL administrator." });

        SetTokenCookie(user);

        return Ok(new LoginResponse(
            new UserDto(user.Id, user.Name, user.Email, user.Role.ToString(), user.TenantId, user.ClientId),
            new TenantDto(user.Tenant.Id, user.Tenant.Name, user.Tenant.PrimaryColor, user.Tenant.LogoUrl, user.Tenant.FooterText),
            user.Client == null ? null : new ClientDto(user.Client.Id, user.Client.Name, user.Client.LogoUrl, user.Client.Address)
        ));
    }

    // ── POST /api/auth/external ───────────────────────────────────────────────

    /// <summary>
    /// SSO login via Google or Facebook.
    /// Google  → verifies ID token against Google tokeninfo endpoint, checks audience.
    /// Facebook → verifies access token against Graph API, fetches id/name/email.
    /// Looks up user by ExternalProviderId first, falls back to email match (account linking).
    /// </summary>
    [HttpPost("external")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest req)
    {
        string? email      = null;
        string? externalId = null;

        var http = _http.CreateClient();

        if (req.Provider == "google")
        {
            var res = await http.GetAsync(
                $"https://oauth2.googleapis.com/tokeninfo?id_token={Uri.EscapeDataString(req.Token)}");

            if (!res.IsSuccessStatusCode)
                return Unauthorized(new { message = "Google token verification failed. Please try again." });

            var payload = await res.Content.ReadFromJsonAsync<JsonElement>();

            if (payload.TryGetProperty("error", out _) || payload.TryGetProperty("error_description", out _))
                return Unauthorized(new { message = "Google token verification failed. Please try again." });

            var emailVerified = payload.TryGetProperty("email_verified", out var evProp) &&
                                evProp.GetString() == "true";
            if (!emailVerified)
                return Unauthorized(new { message = "Google email is not verified." });

            email      = payload.TryGetProperty("email", out var ep) ? ep.GetString() : null;
            externalId = payload.TryGetProperty("sub",   out var sp) ? sp.GetString() : null;
        }
        else if (req.Provider == "facebook")
        {
            var res = await http.GetAsync(
                $"https://graph.facebook.com/me?fields=id,name,email&access_token={req.Token}");

            if (!res.IsSuccessStatusCode)
                return Unauthorized(new { message = "Facebook token verification failed. Please try again." });

            var payload = await res.Content.ReadFromJsonAsync<JsonElement>();

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
        // IgnoreQueryFilters: login is cross-tenant — no JWT yet, CurrentTenantId is null (fail-closed)
        var user = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.Tenant)
            .Include(u => u.Client)
            .FirstOrDefaultAsync(u =>
                u.ExternalProvider   == req.Provider &&
                u.ExternalProviderId == externalId);

        // 2. Fall back to email match and link the account
        if (user == null && !string.IsNullOrEmpty(email))
        {
            user = await _db.Users.IgnoreQueryFilters()
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

        SetTokenCookie(user);

        return Ok(new LoginResponse(
            new UserDto(user.Id, user.Name, user.Email, user.Role.ToString(), user.TenantId, user.ClientId),
            new TenantDto(user.Tenant.Id, user.Tenant.Name, user.Tenant.PrimaryColor, user.Tenant.LogoUrl, user.Tenant.FooterText),
            user.Client == null ? null : new ClientDto(user.Client.Id, user.Client.Name, user.Client.LogoUrl, user.Client.Address)
        ));
    }

    // ── POST /api/auth/logout ─────────────────────────────────────────────────

    [HttpPost("logout")]
    [AllowAnonymous]
    public IActionResult Logout()
    {
        // Expire the cookie immediately — same options required so browser removes it
        Response.Cookies.Append("dx7_token", "", new CookieOptions
        {
            HttpOnly = true,
            Secure   = !HttpContext.RequestServices
                           .GetRequiredService<IHostEnvironment>().IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path     = "/api",
            Expires  = DateTimeOffset.UtcNow.AddDays(-1)
        });
        return NoContent();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void SetTokenCookie(User user)
    {
        var token = _jwt.GenerateToken(user);
        Response.Cookies.Append("dx7_token", token, TokenCookieOptions());
    }
}
