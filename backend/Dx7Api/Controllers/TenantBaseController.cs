using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dx7Api.Controllers;

[Authorize]
public abstract class TenantBaseController : ControllerBase
{
    protected Guid CurrentUserId
    {
        get
        {
            var val = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub")
                   ?? User.FindFirstValue("nameid");
            return Guid.TryParse(val, out var id) ? id : Guid.Empty;
        }
    }

    protected Guid TenantId =>
        Guid.Parse(User.FindFirstValue("tenant_id")!);

    protected Guid? ClientId
    {
        get
        {
            var val = User.FindFirstValue("client_id");
            return string.IsNullOrEmpty(val) ? null : Guid.Parse(val);
        }
    }

    protected string UserRole =>
        User.FindFirstValue(ClaimTypes.Role)!;

    protected bool IsPlAdmin => UserRole is "pl_admin" or "sysad";
    protected bool IsClinicAdmin => UserRole == "clinic_admin";
    protected bool IsChargeNurse => UserRole == "charge_nurse";
    protected bool IsShiftNurse => UserRole == "shift_nurse";
    protected bool IsMd => UserRole == "md";
}