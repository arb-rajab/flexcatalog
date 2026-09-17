namespace FlexCatalog.Api.Auth;

/// <summary>
/// Startup-time guard against the known local-dev placeholder JWT secret
/// leaking into a real deployment (risk.md R2). Extracted from Program.cs
/// as a pure, static check so it's unit-testable without a full host --
/// see JwtSecretGuardTests.
/// </summary>
public static class JwtSecretGuard
{
    /// <summary>
    /// Must match the literal value committed in appsettings.Development.json.
    /// Intentionally duplicated as a constant here (not read back from that
    /// file) -- the whole point is to catch this *specific* string reaching
    /// Production, regardless of how it got there.
    /// </summary>
    public const string KnownDevPlaceholder = "dev-only-signing-secret-change-me-please-32-bytes-min";

    /// <summary>
    /// Throws if <paramref name="secret"/> is the known dev placeholder and
    /// <paramref name="isProduction"/> is true. A missing/empty secret is a
    /// separate, pre-existing check in Program.cs -- this only closes the
    /// gap where a real (non-empty) but still-placeholder value was carried
    /// into Production by an operator who ignored security.md.
    /// </summary>
    public static void EnsureNotPlaceholder(string? secret, bool isProduction)
    {
        if (isProduction && secret == KnownDevPlaceholder)
        {
            throw new InvalidOperationException(
                "Jwt:Secret is set to the known local-development placeholder value. " +
                "Refusing to start in Production. Set Jwt__Secret (or FLEXCATALOG_JWT_SECRET " +
                "for docker-compose) to a real, unique secret -- see docs/project-memory/security.md.");
        }
    }
}
