using EFiling.Core.Security;

namespace EFiling.Tests;

/// <summary>
/// Helper tests for encrypting/decrypting credentials.
/// Run EncryptCredentials_GenerateEncryptedValues to produce ENC: values
/// for your testsettings.json.
/// </summary>
public class EncryptionHelperTests
{
    [Fact]
    public void Encrypt_Decrypt_RoundTrip()
    {
        var passphrase = "test-passphrase-123";
        var original = "my-secret-password";

        var encrypted = EFilingEncryption.Encrypt(original, passphrase);

        Assert.StartsWith("ENC:", encrypted);
        Assert.NotEqual(original, encrypted);

        var decrypted = EFilingEncryption.Decrypt(encrypted, passphrase);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void DecryptIfNeeded_PlainText_ReturnsAsIs()
    {
        var plaintext = "not-encrypted";
        var result = EFilingEncryption.DecryptIfNeeded(plaintext, "any-passphrase");
        Assert.Equal(plaintext, result);
    }

    [Fact]
    public void DecryptIfNeeded_Encrypted_Decrypts()
    {
        var passphrase = "my-passphrase";
        var original = "secret123";
        var encrypted = EFilingEncryption.Encrypt(original, passphrase);

        var result = EFilingEncryption.DecryptIfNeeded(encrypted, passphrase);
        Assert.Equal(original, result);
    }

    [Fact]
    public void IsEncrypted_DetectsPrefix()
    {
        Assert.True(EFilingEncryption.IsEncrypted("ENC:abc123"));
        Assert.False(EFilingEncryption.IsEncrypted("plaintext"));
        Assert.False(EFilingEncryption.IsEncrypted(null));
    }

    /// <summary>
    /// Run this test to generate encrypted values for your testsettings.json.
    /// 
    /// Prerequisites:
    ///   1. Set env var: $env:EFILING_PASSPHRASE = "your-secret-passphrase"
    ///   2. Set env var: $env:EFILING_ENCRYPT_USERNAME = "plaintext-username"
    ///   3. Set env var: $env:EFILING_ENCRYPT_PASSWORD = "plaintext-password"
    ///   4. Run this test — encrypted values printed to console
    ///   5. Paste ENC:... values into testsettings.json
    ///   6. Clear the plaintext env vars
    /// </summary>
    [Fact]
    public void EncryptCredentials_GenerateEncryptedValues()
    {
        var passphrase = TestConfiguration.GetPassphrase();
        var username = Environment.GetEnvironmentVariable("EFILING_ENCRYPT_USERNAME") ?? string.Empty;
        var password = Environment.GetEnvironmentVariable("EFILING_ENCRYPT_PASSWORD") ?? string.Empty;

        // Skip if no values to encrypt (this test is a manual tool, not CI)
        if (string.IsNullOrEmpty(passphrase) || (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(password)))
        {
            Console.WriteLine("Skipped: Set EFILING_PASSPHRASE + EFILING_ENCRYPT_USERNAME/PASSWORD env vars first.");
            return;
        }

        if (!string.IsNullOrEmpty(username))
        {
            var enc = EFilingEncryption.Encrypt(username, passphrase);
            Assert.Equal(username, EFilingEncryption.Decrypt(enc, passphrase));
            Console.WriteLine($"\"Username\": \"{enc}\"");
        }

        if (!string.IsNullOrEmpty(password))
        {
            var enc = EFilingEncryption.Encrypt(password, passphrase);
            Assert.Equal(password, EFilingEncryption.Decrypt(enc, passphrase));
            Console.WriteLine($"\"Password\": \"{enc}\"");
        }
    }
}
