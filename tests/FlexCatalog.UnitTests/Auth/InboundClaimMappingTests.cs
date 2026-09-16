using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FlexCatalog.Api.Auth;
using FlexCatalog.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FlexCatalog.UnitTests.Auth;

/// <summary>
/// Regression test for a second bug CI caught after the JWT-secret-timing
/// fix: JwtSecurityTokenHandler silently remaps well-known short claim
/// types on validation ("role" -> ClaimTypes.Role, "sub" ->
/// ClaimTypes.NameIdentifier, ...) unless JwtBearerOptions.MapInboundClaims
/// is explicitly set to false. FlexClaimTypes.Role and
/// HttpTenantContext.UserId read the literal short names JwtTokenService
/// issues, so with the default mapping left on, the "Admin" policy's
/// RequireClaim("role", "Admin") never matched anything -- every write
/// request got a false 403, for every role, including Admin.
/// </summary>
public class InboundClaimMappingTests
{
    private static string IssueTestToken()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "claim-mapping-test-secret-at-least-32-bytes-long",
            Issuer = "flexcatalog-tests",
            Audience = "flexcatalog-tests-clients",
            ExpiryMinutes = 60,
        });
        var user = new User { Id = "u1", TenantId = "t1", Username = "admin@example.test", Role = UserRole.Admin };
        return new JwtTokenService(options).CreateToken(user);
    }

    private static TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = "flexcatalog-tests",
        ValidAudience = "flexcatalog-tests-clients",
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("claim-mapping-test-secret-at-least-32-bytes-long")),
    };

    [Fact]
    public void WithDefaultInboundMapping_RoleClaimTypeIsSilentlyRemapped_SoFlexClaimTypesRoleIsMissing()
    {
        var token = IssueTestToken();
        var handler = new JwtSecurityTokenHandler(); // default handler: mapping left on

        var principal = handler.ValidateToken(token, ValidationParameters(), out _);

        // This is the bug: the literal "role" claim FlexClaimTypes.Role
        // reads is gone -- it's been remapped to ClaimTypes.Role instead.
        Assert.Null(principal.FindFirst(FlexClaimTypes.Role));
        Assert.NotNull(principal.FindFirst(ClaimTypes.Role));
    }

    [Fact]
    public void WithMapInboundClaimsDisabled_RoleClaimTypeSurvivesAsIssued()
    {
        var token = IssueTestToken();
        var handler = new JwtSecurityTokenHandler
        {
            // Mirrors options.MapInboundClaims = false in Program.cs.
            InboundClaimTypeMap = new Dictionary<string, string>(),
        };

        var principal = handler.ValidateToken(token, ValidationParameters(), out _);

        var roleClaim = principal.FindFirst(FlexClaimTypes.Role);
        Assert.NotNull(roleClaim);
        Assert.Equal("Admin", roleClaim!.Value);
    }
}
