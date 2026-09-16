using System.Text;
using FlexCatalog.Api.Auth;
using FlexCatalog.Api.Endpoints;
using FlexCatalog.Api.Infrastructure;
using FlexCatalog.Api.Repositories;
using FlexCatalog.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Scalar.AspNetCore;

MongoConventions.Register();

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MongoOptions>(builder.Configuration.GetSection(MongoOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

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

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtSecret = jwtSection["Secret"] ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ProductEndpoints.AdminPolicy, policy => policy.RequireClaim(FlexClaimTypes.Role, nameof(FlexCatalog.Api.Domain.UserRole.Admin)));

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

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapProductEndpoints();
app.MapCategoryEndpoints();
app.MapHealthEndpoints();

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;
