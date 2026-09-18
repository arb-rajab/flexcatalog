using FlexCatalog.Api.Dtos;
using FlexCatalog.Api.Infrastructure;
using FlexCatalog.Api.Services;

namespace FlexCatalog.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, IAuthService authService, CancellationToken ct) =>
        {
            var result = await authService.LoginAsync(request, ct);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .RequireRateLimiting(LoginRateLimiting.PolicyName)
        .WithName("Login")
        .WithSummary("Exchange tenant-scoped credentials for a JWT.");
    }
}
