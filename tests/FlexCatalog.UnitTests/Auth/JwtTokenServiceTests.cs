using System.IdentityModel.Tokens.Jwt;
using FlexCatalog.Api.Auth;
using FlexCatalog.Api.Domain;
using Microsoft.Extensions.Options;

namespace FlexCatalog.UnitTests.Auth;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService(int expiryMinutes = 60) =>
        new(Options.Create(new JwtOptions
        {
            Secret = "unit-test-signing-secret-at-least-32-bytes-long",
            Issuer = "flexcatalog-tests",
            Audience = "flexcatalog-tests-clients",
            ExpiryMinutes = expiryMinutes,
        }));

    [Fact]
    public void CreateToken_EmbedsTenantIdAndRoleClaims()
    {
        var service = CreateService();
        var user = new User
        {
            Id = "user-1",
            TenantId = "tenant-abc",
            Username = "admin@acme.test",
            Role = UserRole.Admin,
        };

        var token = service.CreateToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("tenant-abc", jwt.Claims.Single(c => c.Type == FlexClaimTypes.TenantId).Value);
        Assert.Equal("Admin", jwt.Claims.Single(c => c.Type == FlexClaimTypes.Role).Value);
        Assert.Equal("user-1", jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("flexcatalog-tests", jwt.Issuer);
    }

    [Fact]
    public void CreateToken_SetsExpiryFromOptions()
    {
        var service = CreateService(expiryMinutes: 15);
        var user = new User { Id = "u", TenantId = "t", Username = "x@y.test", Role = UserRole.Viewer };

        var token = service.CreateToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        var expectedExpiry = DateTime.UtcNow.AddMinutes(15);
        Assert.True(Math.Abs((jwt.ValidTo - expectedExpiry).TotalMinutes) < 1);
    }
}
