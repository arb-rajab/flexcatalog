namespace FlexCatalog.Api.Infrastructure;

/// <summary>
/// Threshold for the fixed-window rate limiter applied to
/// POST /api/auth/login (closes risk.md R4). Partitioned per client IP so
/// one attacker can't exhaust other callers' allowance; a caller past the
/// limit gets 429 with no queueing (fail fast, not fail slow).
///
/// 5 attempts / 60s per IP: generous enough that a human mistyping a
/// password a couple of times never sees it, but low enough to make
/// single-IP password-guessing impractical against BCrypt-hashed
/// passwords (work factor 12 already makes each guess ~100ms+ of server
/// CPU; this caps guesses at 5/minute/IP on top of that). Documented here
/// and in docs/project-memory/security.md -- change both together.
/// </summary>
public static class LoginRateLimiting
{
    public const string PolicyName = "login";

    public const int PermitLimit = 5;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}
