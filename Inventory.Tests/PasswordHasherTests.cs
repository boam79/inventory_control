using Inventory.Core;

namespace Inventory.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_is_not_plaintext_and_verifies()
    {
        const string password = "correct-horse";
        var hash = PasswordHasher.Hash(password);

        Assert.False(string.Equals(hash, password, StringComparison.Ordinal));
        Assert.DoesNotContain(password, hash, StringComparison.Ordinal);
        Assert.True(PasswordHasher.Verify(password, hash));
    }

    [Fact]
    public void Wrong_password_does_not_verify()
    {
        var hash = PasswordHasher.Hash("secret-one");

        Assert.False(PasswordHasher.Verify("secret-two", hash));
        Assert.False(PasswordHasher.Verify("", hash));
    }
}
