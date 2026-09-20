using System.Text;
using System.Threading.RateLimiting;
using FlexCatalog.Api.Auth;
using FlexCatalog.Api.Endpoints;
using FlexCatalog.Api.Eventing;
using FlexCatalog.Api.Infrastructure;
using FlexCatalog.Api.Repositories;
using FlexCatalog.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Scalar.AspNetCore;

MongoConventions.Register();

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MongoOptions>(builder.Configuration.GetSection(MongoOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<NatsOptions>(builder.Configuration.GetSection(NatsOptions.SectionName));

builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoOptions>>().Value;
    return new MongoClient(options.ConnectionString);
});
builder.Services.AddSingleton<MongoContext>();
builder.Services.AddHostedService<MongoIndexInitializer>();
builder.Services.AddHostedService<DataSeeder>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

// ProductRepository depends on ITenantContext (per-request), so it must
// stay Scoped. UserRepository/TenantRepository hold no per-request state
// (they only wrap the singleton MongoContext) and are registered as
// Singleton specifically so DataSeeder -- a Singleton IHostedService --
// can depend on them directly without violating ASP.NET Core's
// singleton-cannot-consume-scoped validation.
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddSingleton<IUserRepository, UserRepository>();
builder.Services.AddSingleton<ITenantRepository, TenantRepository>();

builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IProductSearchService, ProductSearchService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

// Domain-event publishing (ADR 0006): the channel and the background
// publisher are both Singletons with no Scoped dependencies, so
// DomainEventPublishingService (a Singleton IHostedService) never hits the
// hosted-service-depends-on-Scoped-service trap documented in CLAUDE.md.
// IDomainEventPublisher is Singleton too -- Publish() only touches the
// channel, so it's safe to inject into the Scoped ProductService.
builder.Services.AddSingleton<DomainEventChannel>();
builder.Services.AddSingleton<IDomainEventPublisher, ChannelDomainEventPublisher>();
builder.Services.AddHostedService<DomainEventPublishingService>();

// Fail fast at startup if misconfigured. The actual value used for
// signature validation is re-read from configuration *inside* the
// AddJwtBearer delegate below rather than captured here: that delegate
// runs lazily (when JwtBearerOptions are first resolved, after
// builder.Build()), which matters for WebApplicationFactory-based tests --
// their configuration overrides are only merged in at Build() time, so a
// value captured in a local variable up here would still be the
// pre-override value even though JwtTokenService (which resolves
// IOptions<JwtOptions> lazily via DI) would sign with the overridden one,
// causing every issued token to fail validation with a spurious 401.
var configuredJwtSecret = builder.Configuration[$"{JwtOptions.SectionName}:Secret"];
if (string.IsNullOrEmpty(configuredJwtSecret))
{
    throw new InvalidOperationException("Jwt:Secret is not configured.");
}

// Closes risk.md R2: a non-empty secret that is still the known dev
// placeholder must not silently boot in Production. Checked here (once,
// at startup gating) rather than inside the lazy AddJwtBearer delegate
// below -- this is a one-shot fail-fast check, not a value reused later
// for signature validation, so it doesn't fall into the stale-capture
// trap documented above the AddJwtBearer call.
JwtSecretGuard.EnsureNotPlaceholder(configuredJwtSecret, builder.Environment.IsProduction());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, JwtSecurityTokenHandler silently remaps well-known
        // short claim types on validation ("sub" -> ClaimTypes.NameIdentifier,
        // "role" -> ClaimTypes.Role, ...) via its legacy inbound claim map.
        // FlexClaimTypes.Role ("role") and ITenantContext.UserId ("sub")
        // read the literal short names JwtTokenService actually issues, so
        // leaving the default mapping on makes RequireClaim("role", "Admin")
        // never match -- every write request gets a false 403, regardless
        // of the caller's actual role.
        options.MapInboundClaims = false;

        var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Secret"]!)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ProductEndpoints.AdminPolicy, policy => policy.RequireClaim(FlexClaimTypes.Role, nameof(FlexCatalog.Api.Domain.UserRole.Admin)));

// Closes risk.md R4. Threshold and rationale documented once in
// LoginRateLimiting -- keep that in sync with security.md if changed.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(LoginRateLimiting.PolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = LoginRateLimiting.PermitLimit,
                Window = LoginRateLimiting.Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// TLS is terminated upstream (container platform / load balancer) in
// non-Development environments, so there is no local HTTPS endpoint to
// redirect to there.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapProductEndpoints();
app.MapCategoryEndpoints();
app.MapHealthEndpoints();

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;
