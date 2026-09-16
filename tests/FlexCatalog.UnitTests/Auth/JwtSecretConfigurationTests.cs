using System.Text;
using FlexCatalog.Api.Auth;
using Microsoft.IdentityModel.Tokens;

namespace FlexCatalog.UnitTests.Auth;

/// <summary>
/// Regression test for a bug caught by CI (not this project's local build):
/// Program.cs originally read Jwt:Secret into a local variable *before*
/// builder.Build(), then closed over that stale value inside the
/// AddJwtBearer options delegate. That works at real startup (nothing else
/// touches configuration between CreateBuilder and Build), but
/// WebApplicationFactory-based tests inject their configuration overrides
/// exactly at Build() time -- so the validation middleware silently kept
/// checking signatures against the pre-override secret while
/// JwtTokenService (which resolves IOptions&lt;JwtOptions&gt; lazily via DI,
/// after Build()) correctly picked up the override. Every login succeeded
/// but every subsequent authenticated request failed with 401.
///
/// This test doesn't exercise Program.cs's DI wiring directly (that needs
/// the full host, covered by the integration suite); it pins the specific
/// mechanism instead: a token signed with one secret must fail validation
/// against a *different* key, and must succeed when validated against the
/// *same* key that signed it -- the exact distinction the original bug got
/// wrong by using two different secret values without realizing it.
/// </summary>
public class JwtSecretConfigurationTests
{
    [Fact]
    public void TokenValidation_WithSameSecretUsedToSign_Succeeds()
    {
        var secret = "matching-secret-used-for-both-signing-and-validation";
        var token = CreateTestToken(secret);

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var parameters = ValidationParametersFor(secret);

        var exception = Record.Exception(() => handler.ValidateToken(token, parameters, out _));

        Assert.Null(exception);
    }

    [Fact]
    public void TokenValidation_WithDifferentSecretThanSigning_FailsWithSecurityTokenException()
    {
        // This reproduces the actual bug: sign with the "real" (post-override)
        // secret, validate with a stale/different secret -- exactly what
        // happened when Program.cs captured Jwt:Secret before builder.Build().
        var signingSecret = "the-secret-jwttokenservice-actually-signed-with";
        var staleValidationSecret = "a-different-stale-secret-captured-too-early";
        var token = CreateTestToken(signingSecret);

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var parameters = ValidationParametersFor(staleValidationSecret);

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(
            () => handler.ValidateToken(token, parameters, out _));
    }

    private static string CreateTestToken(string secret)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            Secret = secret,
            Issuer = "flexcatalog-tests",
            Audience = "flexcatalog-tests-clients",
            ExpiryMinutes = 60,
        });
        var service = new JwtTokenService(options);
        var user = new FlexCatalog.Api.Domain.User
        {
            Id = "u1",
            TenantId = "t1",
            Username = "test@example.test",
            Role = FlexCatalog.Api.Domain.UserRole.Admin,
        };
        return service.CreateToken(user);
    }

    private static TokenValidationParameters ValidationParametersFor(string secret) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = "flexcatalog-tests",
        ValidAudience = "flexcatalog-tests-clients",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        ClockSkew = TimeSpan.FromSeconds(30),
    };
}
