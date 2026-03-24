namespace Dx7Api.Data;

/// <summary>
/// Ambient tenant context for background services that have no HttpContext.
///
/// Uses AsyncLocal so each logical async call chain carries its own tenant ID
/// independently of other concurrent chains (e.g. multiple HL7 files processed
/// sequentially through the semaphore each get their own scope).
///
/// Usage:
///   using (TenantAmbient.Use(tenantId)) { ... await processor.ProcessAsync(...) ... }
///
/// TenantRlsInterceptor reads this as a fallback when HttpContext is unavailable.
/// </summary>
public static class TenantAmbient
{
    private static readonly AsyncLocal<Guid?> _current = new();

    public static Guid? Current => _current.Value;

    /// <summary>
    /// Sets the ambient tenant for the current async scope and returns a disposable
    /// that restores the previous value on disposal.
    /// </summary>
    public static IDisposable Use(Guid tenantId)
    {
        var previous = _current.Value;
        _current.Value = tenantId;
        return new Scope(previous);
    }

    private sealed class Scope(Guid? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
