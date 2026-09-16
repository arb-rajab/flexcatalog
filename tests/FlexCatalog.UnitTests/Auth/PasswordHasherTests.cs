using FlexCatalog.Api.Auth;

namespace FlexCatalog.UnitTests.Auth;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_ThenVerify_WithCorrectPassword_Succeeds()
    {
        var hash = _hasher.Hash("Passw0rd!");

        Assert.True(_hasher.Verify("Passw0rd!", hash));
    }

    [Fact]
    public void Verify_WithWrongPassword_Fails()
    {
        var hash = _hasher.Hash("Passw0rd!");

        Assert.False(_hasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Hash_ProducesDifferentOutputForSamePasswordEachTime()
    {
        var hash1 = _hasher.Hash("Passw0rd!");
        var hash2 = _hasher.Hash("Passw0rd!");

        Assert.NotEqual(hash1, hash2); // salted
    }
}
