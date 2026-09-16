namespace FlexCatalog.Api.Dtos;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(string Token, DateTime ExpiresAt, string TenantId, string Role);
