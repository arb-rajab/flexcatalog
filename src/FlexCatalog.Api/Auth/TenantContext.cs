using System.Security.Claims;

namespace FlexCatalog.Api.Auth;

/// <summary>
/// Per-request tenant identity, resolved exclusively from the validated JWT
/// (never from a client-supplied header/query param -- a header could be
/// forged; the JWT is signed). Every tenant-scoped repository takes this as
/// a dependency, which makes it structurally impossible to query the
/// products/users collections without a tenant filter -- there is no
/// "unscoped" code path. See
/// docs/project-memory/decisions/0001-multi-tenant-isolation-strategy.md.
/// </summary>
public interface ITenantContext
{
    string TenantId { get; }

    string UserId { get; }

    string Role { get; }
}

public sealed class HttpTenantContext : ITenantContext
{
    private readonly ClaimsPrincipal? _user;

    public HttpTenantContext(IHttpContextAccessor accessor)
    {
        _user = accessor.HttpContext?.User;
    }

    public string TenantId => GetClaim(FlexClaimTypes.TenantId);

    public string UserId => GetClaim("sub");

    public string Role => GetClaim(FlexClaimTypes.Role);

    private string GetClaim(string type)
    {
        var value = _user?.FindFirst(type)?.Value;
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"Tenant context requested claim '{type}' outside an authenticated request. " +
                "This indicates an endpoint is missing [Authorize] / RequireAuthorization().");
        }

        return value;
    }
}
