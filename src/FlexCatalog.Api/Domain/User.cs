using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FlexCatalog.Api.Domain;

public enum UserRole
{
    Viewer,
    Admin
}

/// <summary>
/// A user belongs to exactly one tenant. Usernames (emails) are globally
/// unique so that login (POST /api/auth/login) can locate the account -- and
/// therefore its tenant -- without asking the caller which tenant they mean.
/// See docs/project-memory/decisions/0003-jwt-authentication.md.
/// </summary>
public sealed class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    [BsonElement("role")]
    [BsonRepresentation(BsonType.String)]
    public UserRole Role { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
