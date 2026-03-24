using System.IdentityModel.Tokens.Jwt;
using Dx7Api.Data;

namespace Dx7Api.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        // F-09: read JWT from httpOnly cookie (primary); fall back to Bearer header
        // so Swagger UI / API clients using Authorization still work.
        var token = context.Request.Cookies["dx7_token"];

        if (string.IsNullOrEmpty(token))
        {
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            if (authHeader?.StartsWith("Bearer ") == true)
                token = authHeader[7..];
        }

        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                if (handler.CanReadToken(token))
                {
                    var jwt = handler.ReadJwtToken(token);

                    var tenantClaim = jwt.Claims.FirstOrDefault(c => c.Type == "tenant_id");
                    if (tenantClaim != null && Guid.TryParse(tenantClaim.Value, out var tenantId))
                    {
                        db.CurrentTenantId = tenantId;
                        // F-10: expose to TenantRlsInterceptor via HttpContext.Items
                        // (avoids circular dependency between the interceptor and DbContext)
                        context.Items["CurrentTenantId"] = tenantId;
                    }

                    var subClaim = jwt.Claims.FirstOrDefault(c =>
                        c.Type == System.Security.Claims.ClaimTypes.NameIdentifier ||
                        c.Type == "sub" || c.Type == "nameid");
                    if (subClaim != null && Guid.TryParse(subClaim.Value, out var userId))
                        db.CurrentUserId = userId;
                }
            }
            catch
            {
                // Invalid token — JWT middleware will return 401 on protected endpoints
            }
        }

        await _next(context);
    }
}
