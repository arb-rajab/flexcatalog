using FlexCatalog.Api.Auth;

namespace FlexCatalog.UnitTests.Auth;

/// <summary>
/// Pins the startup guard that closes risk.md R2 (dev placeholder JWT
/// secret reused in Production). Exercises the extracted static check
/// directly rather than booting the full host, per the pattern used
/// elsewhere in this project for host-independent logic.
/// </summary>
public class JwtSecretGuardTests
{
    [Fact]
    public void EnsureNotPlaceholder_PlaceholderInProduction_Throws()
    {
        var ex = Record.Exception(() =>
            JwtSecretGuard.EnsureNotPlaceholder(JwtSecretGuard.KnownDevPlaceholder, isProduction: true));

        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void EnsureNotPlaceholder_PlaceholderInDevelopment_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            JwtSecretGuard.EnsureNotPlaceholder(JwtSecretGuard.KnownDevPlaceholder, isProduction: false));

        Assert.Null(ex);
    }

    [Fact]
    public void EnsureNotPlaceholder_RealSecretInProduction_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            JwtSecretGuard.EnsureNotPlaceholder("a-real-operator-provisioned-secret-value", isProduction: true));

        Assert.Null(ex);
    }

    [Fact]
    public void EnsureNotPlaceholder_NullSecretInProduction_DoesNotThrow()
    {
        // Not this guard's job -- the separate IsNullOrEmpty check in
        // Program.cs handles a missing secret.
        var ex = Record.Exception(() =>
            JwtSecretGuard.EnsureNotPlaceholder(null, isProduction: true));

        Assert.Null(ex);
    }
}
