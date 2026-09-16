using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Infrastructure;
using MongoDB.Driver;

namespace FlexCatalog.Api.Repositories;

/// <summary>
/// Not tenant-scoped by design: login happens before a tenant is known --
/// the username is globally unique and identifies the tenant, not the other
/// way around. See docs/project-memory/decisions/0003-jwt-authentication.md.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);

    Task InsertAsync(User user, CancellationToken ct = default);

    Task<long> CountAsync(CancellationToken ct = default);
}

public sealed class UserRepository(MongoContext context) : IUserRepository
{
    private IMongoCollection<User> Collection => context.Users;

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        await Collection.Find(u => u.Username == username).FirstOrDefaultAsync(ct);

    public Task InsertAsync(User user, CancellationToken ct = default) =>
        Collection.InsertOneAsync(user, cancellationToken: ct);

    public Task<long> CountAsync(CancellationToken ct = default) =>
        Collection.CountDocumentsAsync(FilterDefinition<User>.Empty, cancellationToken: ct);
}
