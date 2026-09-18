using System.Threading.RateLimiting;
using FlexCatalog.Api.Infrastructure;

namespace FlexCatalog.UnitTests.Infrastructure;

/// <summary>
/// Pins the exact threshold documented in LoginRateLimiting/security.md
/// (closes risk.md R4) as an executable spec, using the same
/// FixedWindowRateLimiterOptions Program.cs configures -- without needing
/// a full host, per the project's pattern for host-independent logic.
/// </summary>
public class LoginRateLimitingTests
{
    [Fact]
    public void FixedWindowLimiter_AllowsExactlyPermitLimit_ThenRejects()
    {
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = LoginRateLimiting.PermitLimit,
            Window = LoginRateLimiting.Window,
            QueueLimit = 0,
            AutoReplenishment = false,
        });

        for (var i = 0; i < LoginRateLimiting.PermitLimit; i++)
        {
            using var lease = limiter.AttemptAcquire();
            Assert.True(lease.IsAcquired, $"Attempt {i + 1} should be within the {LoginRateLimiting.PermitLimit}-permit window.");
        }

        using var rejected = limiter.AttemptAcquire();
        Assert.False(rejected.IsAcquired);
    }

    [Fact]
    public void FixedWindowLimiter_DifferentPartitions_AreIndependent()
    {
        // Mirrors Program.cs's per-IP partition key: one caller hitting the
        // limit must not affect a different partition's allowance.
        using var limiterA = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = LoginRateLimiting.PermitLimit,
            Window = LoginRateLimiting.Window,
            QueueLimit = 0,
            AutoReplenishment = false,
        });
        using var limiterB = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = LoginRateLimiting.PermitLimit,
            Window = LoginRateLimiting.Window,
            QueueLimit = 0,
            AutoReplenishment = false,
        });

        for (var i = 0; i < LoginRateLimiting.PermitLimit; i++)
        {
            using var lease = limiterA.AttemptAcquire();
            Assert.True(lease.IsAcquired);
        }

        using var exhausted = limiterA.AttemptAcquire();
        Assert.False(exhausted.IsAcquired);

        using var stillAllowed = limiterB.AttemptAcquire();
        Assert.True(stillAllowed.IsAcquired);
    }
}
