using Dx7Api.Data;
using Dx7Api.DTOs;
using Dx7Api.Models;
using Dx7Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/auth/external")]
[AllowAnonymous]
public class ExternalAuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;
    private readonly IConfiguration _config;
    private static readonly HttpClient _http = new();

    public ExternalAuthController(AppDbContext db, JwtService jwt, IConfiguration config)
    {
        _db = db; _jwt = jwt; _config = config;
    }

    public record ExternalLoginRequest(string Provider, string Token, string? TenantCode = null);

    [HttpPost]
    public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest req)
    {
        var provider = req.Provider.ToLower();
        ExternalUserInfo? info = provider switch
        {
            "google"   => await VerifyGoogle(req.Token),
            "facebook" => await VerifyFacebook(req.Token),
            _ => null
        };

        if (info == null)
            return Unauthorized(new { message = "Invalid or expired token from provider." });

        // Resolve tenant — use TenantCode hint or fall back to single tenant
        _db.CurrentTenantId = null; // bypass filter for lookup
        var tenant = await _db.Tenants.FirstOrDefaultAsync();
        if (tenant == null)
            return StatusCode(500, new { message = "No tenant configured." });

        _db.CurrentTenantId = tenant.Id;

        // Find existing user by provider ID, or by email only for non-synthetic emails
        var user = await _db.Users.Include(u => u.Client)
            .FirstOrDefaultAsync(u =>
                (u.ExternalProvider == provider && u.ExternalProviderId == info.Id) ||
                (!info.Email.EndsWith("@facebook.local") && u.Email == info.Email && u.TenantId == tenant.Id));

        if (user == null)
        {
            // Auto-create with shift_nurse role — admin promotes later
            user = new User
            {
                TenantId           = tenant.Id,
                Email              = info.Email,
                Name               = info.Name,
                PasswordHash       = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()), // unusable pw
                Role               = UserRole.shift_nurse,
                ExternalProvider   = provider,
                ExternalProviderId = info.Id,
                AvatarUrl          = info.Picture,
                IsActive           = true
            };
            _db.Users.Add(user);
        }
        else
        {
            // Update provider linkage and name if missing
            user.ExternalProvider   ??= provider;
            user.ExternalProviderId ??= info.Id;
            if (string.IsNullOrEmpty(user.AvatarUrl) && !string.IsNullOrEmpty(info.Picture))
                user.AvatarUrl = info.Picture;
        }

        if (!user.IsActive)
            return Unauthorized(new { message = "Account is deactivated. Contact your administrator." });

        await _db.SaveChangesAsync();

        // Reload with tenant for JWT
        await _db.Entry(user).Reference(u => u.Tenant).LoadAsync();

        var token = _jwt.GenerateToken(user);

        // Resolve role label
        var roleLabel = user.Role.ToString().Replace("_", " ");

        return Ok(new LoginResponse(
            token,
            new UserDto(user.Id, user.Name, user.Email, user.Role.ToString(), user.TenantId, user.ClientId),
            new TenantDto(tenant.Id, tenant.Name, tenant.PrimaryColor ?? "#1d4ed8", tenant.LogoUrl, tenant.FooterText),
            user.Client == null ? null : new ClientDto(user.Client.Id, user.Client.Name, user.Client.LogoUrl, user.Client.Address)
        ));
    }

    // ── Google token verification ──────────────────────────────────────────
    private async Task<ExternalUserInfo?> VerifyGoogle(string idToken)
    {
        try
        {
            var url = $"https://oauth2.googleapis.com/tokeninfo?id_token={idToken}";
            var res = await _http.GetAsync(url);
            if (!res.IsSuccessStatusCode) return null;
            var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

            var clientId = _config["OAuth:Google:ClientId"];
            var aud = json.GetProperty("aud").GetString();
            if (aud != clientId) return null; // wrong audience

            return new ExternalUserInfo(
                json.GetProperty("sub").GetString()!,
                json.GetProperty("email").GetString()!,
                json.TryGetProperty("name", out var n) ? n.GetString()! : json.GetProperty("email").GetString()!,
                json.TryGetProperty("picture", out var p) ? p.GetString() : null
            );
        }
        catch { return null; }
    }

    // ── Facebook token verification ────────────────────────────────────────
    private async Task<ExternalUserInfo?> VerifyFacebook(string accessToken)
    {
        try
        {
            var appId     = _config["OAuth:Facebook:AppId"];
            var appSecret = _config["OAuth:Facebook:AppSecret"];
            var appToken  = $"{appId}|{appSecret}";

            // Verify token
            var verifyUrl = $"https://graph.facebook.com/debug_token?input_token={accessToken}&access_token={appToken}";
            var verifyRes = await _http.GetAsync(verifyUrl);
            if (!verifyRes.IsSuccessStatusCode) return null;
            var verify = JsonDocument.Parse(await verifyRes.Content.ReadAsStringAsync()).RootElement;
            if (!verify.GetProperty("data").GetProperty("is_valid").GetBoolean()) return null;

            // Get user info
            var meUrl = $"https://graph.facebook.com/me?fields=id,name,picture&access_token={accessToken}";
            var meRes = await _http.GetAsync(meUrl);
            if (!meRes.IsSuccessStatusCode) return null;
            var me = JsonDocument.Parse(await meRes.Content.ReadAsStringAsync()).RootElement;

            var fbId  = me.GetProperty("id").GetString()!;
            var email = $"fb_{fbId}@facebook.local";
            var picture = me.TryGetProperty("picture", out var pic)
                ? pic.GetProperty("data").GetProperty("url").GetString()
                : null;

            return new ExternalUserInfo(
                fbId,
                email,
                me.GetProperty("name").GetString()!,
                picture
            );
        }
        catch { return null; }
    }

    private record ExternalUserInfo(string Id, string Email, string Name, string? Picture);
}