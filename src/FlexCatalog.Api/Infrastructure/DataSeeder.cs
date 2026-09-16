using FlexCatalog.Api.Auth;
using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Repositories;
using Microsoft.Extensions.Hosting;

namespace FlexCatalog.Api.Infrastructure;

/// <summary>
/// Seeds two demo tenants, one admin user each, and a handful of products
/// spanning all three categories -- deliberately split across tenants so a
/// fresh checkout can immediately demonstrate (and integration-test) tenant
/// isolation instead of starting from an empty database. Runs once at
/// startup, only when the tenants collection is empty, and only outside
/// Production.
/// </summary>
public sealed class DataSeeder(
    ITenantRepository tenants,
    IUserRepository users,
    MongoContext context,
    IPasswordHasher passwordHasher,
    IHostEnvironment environment,
    ILogger<DataSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (environment.IsProduction())
        {
            return;
        }

        if (await tenants.CountAsync(cancellationToken) > 0)
        {
            logger.LogInformation("Seed data already present, skipping seeding");
            return;
        }

        var acme = new Tenant { Slug = "acme-electronics", Name = "Acme Electronics Co." };
        var urban = new Tenant { Slug = "urbanthread-apparel", Name = "UrbanThread Apparel" };
        await tenants.InsertAsync(acme, cancellationToken);
        await tenants.InsertAsync(urban, cancellationToken);

        var acmeAdmin = new User
        {
            TenantId = acme.Id,
            Username = "admin@acme.test",
            PasswordHash = passwordHasher.Hash("Passw0rd!"),
            Role = UserRole.Admin,
        };
        var acmeViewer = new User
        {
            TenantId = acme.Id,
            Username = "viewer@acme.test",
            PasswordHash = passwordHasher.Hash("Passw0rd!"),
            Role = UserRole.Viewer,
        };
        var urbanAdmin = new User
        {
            TenantId = urban.Id,
            Username = "admin@urbanthread.test",
            PasswordHash = passwordHasher.Hash("Passw0rd!"),
            Role = UserRole.Admin,
        };
        await users.InsertAsync(acmeAdmin, cancellationToken);
        await users.InsertAsync(acmeViewer, cancellationToken);
        await users.InsertAsync(urbanAdmin, cancellationToken);

        var acmeProducts = new List<Product>
        {
            NewProduct(acme.Id, "ACME-LAP-001", "Nimbus 14 Laptop", CategoryType.Electronics, 1299.99m, 12,
                new ElectronicsAttributes
                {
                    Brand = "Acme",
                    Model = "Nimbus 14",
                    WarrantyMonths = 24,
                    Specs = new Dictionary<string, string> { ["RAM"] = "16GB", ["Storage"] = "512GB SSD" },
                }),
            NewProduct(acme.Id, "ACME-HDP-002", "Aria Wireless Headphones", CategoryType.Electronics, 199.50m, 40,
                new ElectronicsAttributes
                {
                    Brand = "Acme",
                    Model = "Aria",
                    WarrantyMonths = 12,
                    Specs = new Dictionary<string, string> { ["BatteryLife"] = "30h", ["Bluetooth"] = "5.3" },
                }),
            NewProduct(acme.Id, "ACME-CAM-003", "Pixel Vision Camera", CategoryType.Electronics, 649.00m, 0,
                new ElectronicsAttributes
                {
                    Brand = "Pixel",
                    Model = "Vision X",
                    WarrantyMonths = 12,
                    Specs = new Dictionary<string, string> { ["Sensor"] = "24MP", ["Zoom"] = "3x optical" },
                }),
        };

        var urbanProducts = new List<Product>
        {
            NewProduct(urban.Id, "URB-TEE-001", "Classic Crew Tee", CategoryType.Apparel, 24.99m, 150,
                new ApparelAttributes
                {
                    Sizes = ["S", "M", "L", "XL"],
                    Colors = ["Black", "White", "Navy"],
                    Material = "100% Cotton",
                    Gender = "Unisex",
                }),
            NewProduct(urban.Id, "URB-JKT-002", "Trailblazer Jacket", CategoryType.Apparel, 89.00m, 30,
                new ApparelAttributes
                {
                    Sizes = ["M", "L", "XL"],
                    Colors = ["Olive", "Black"],
                    Material = "Nylon shell, fleece lining",
                    Gender = "Men",
                }),
            NewProduct(urban.Id, "URB-BOK-003", "Threads: A Field Guide", CategoryType.Books, 18.75m, 60,
                new BookAttributes
                {
                    Author = "J. Alvarez",
                    Isbn = "978-1-234567-89-0",
                    Pages = 212,
                    Format = "Paperback",
                }),
        };

        await context.Products.InsertManyAsync(acmeProducts, cancellationToken: cancellationToken);
        await context.Products.InsertManyAsync(urbanProducts, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Seeded 2 tenants, 3 users, {ProductCount} products",
            acmeProducts.Count + urbanProducts.Count);
    }

    private static Product NewProduct(
        string tenantId, string sku, string name, CategoryType category, decimal price, int quantity,
        ProductAttributes attributes) => new()
        {
            TenantId = tenantId,
            Sku = sku,
            Name = name,
            Description = $"{name} -- seeded demo product.",
            CategoryType = category,
            Price = price,
            Currency = "USD",
            InStock = quantity > 0,
            QuantityOnHand = quantity,
            Tags = [category.ToString().ToLowerInvariant()],
            Attributes = attributes,
        };

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
