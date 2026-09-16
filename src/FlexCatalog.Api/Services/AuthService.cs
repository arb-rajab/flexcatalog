using FlexCatalog.Api.Auth;
using FlexCatalog.Api.Dtos;
using FlexCatalog.Api.Repositories;

namespace FlexCatalog.Api.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
}

public sealed class AuthService(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenService tokenService,
    Microsoft.Extensions.Options.IOptions<JwtOptions> jwtOptions) : IAuthService
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userRepository.GetByUsernameAsync(request.Username, ct);
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            // Deliberately identical error for "no such user" and "wrong
            // password" -- distinguishing them lets an attacker enumerate
            // valid usernames.
            throw new ValidationException("Invalid username or password.");
        }

        var token = tokenService.CreateToken(user);
        var expiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.Value.ExpiryMinutes);

        return new LoginResponse(token, expiresAt, user.TenantId, user.Role.ToString());
    }
}
