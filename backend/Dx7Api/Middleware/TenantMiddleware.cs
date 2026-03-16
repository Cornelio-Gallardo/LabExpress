using System.IdentityModel.Tokens.Jwt;
using Dx7Api.Data;

namespace Dx7Api.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        // Extract tenant_id from JWT and set on DbContext for global query filters
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (authHeader != null && authHeader.StartsWith("Bearer "))
        {
            var token = authHeader.Substring(7);
            try
            {
                var handler = new JwtSecurityTokenHandler();
                if (handler.CanReadToken(token))
                {
                    var jwt = handler.ReadJwtToken(token);
                    var tenantClaim = jwt.Claims.FirstOrDefault(c => c.Type == "tenant_id");
                    if (tenantClaim != null && Guid.TryParse(tenantClaim.Value, out var tenantId))
                        db.CurrentTenantId = tenantId;

                    var subClaim = jwt.Claims.FirstOrDefault(c =>
                        c.Type == System.Security.Claims.ClaimTypes.NameIdentifier ||
                        c.Type == "sub" || c.Type == "nameid");
                    if (subClaim != null && Guid.TryParse(subClaim.Value, out var userId))
                        db.CurrentUserId = userId;
                }
            }
            catch
            {
                // Invalid token - let auth middleware handle it
            }
        }

        await _next(context);
    }
}
