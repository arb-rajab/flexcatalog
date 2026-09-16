namespace FlexCatalog.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// HMAC signing secret. MUST be overridden via environment variable /
    /// secret store in any non-local environment -- the value in
    /// appsettings.json is a local-dev-only placeholder and is intentionally
    /// documented as such in docs/project-memory/security.md.
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "flexcatalog";

    public string Audience { get; set; } = "flexcatalog-clients";

    public int ExpiryMinutes { get; set; } = 60;
}
