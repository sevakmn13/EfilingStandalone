using System.Security.Cryptography;
using System.Text;

namespace EFiling.Core.Security;

/// <summary>
/// AES-256-CBC encryption for storing credentials at rest.
/// Values are prefixed with "ENC:" to distinguish encrypted from plaintext.
/// 
/// Usage:
///   var encrypted = EFilingEncryption.Encrypt("mySecret", passphrase);
///   // Store "ENC:base64..." in config
///   var decrypted = EFilingEncryption.DecryptIfNeeded(encrypted, passphrase);
/// </summary>
public static class EFilingEncryption
{
    private const string EncryptedPrefix = "ENC:";
    private const int KeySize = 32;   // AES-256
    private const int IvSize = 16;    // AES block size
    private const int SaltSize = 16;
    private const int Iterations = 100_000;

    /// <summary>
    /// Encrypt a plaintext value using AES-256-CBC with a passphrase.
    /// Returns "ENC:" + Base64(salt + iv + ciphertext).
    /// </summary>
    public static string Encrypt(string plaintext, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        using var keyDerivation = new Rfc2898DeriveBytes(passphrase, salt, Iterations, HashAlgorithmName.SHA256);
        var key = keyDerivation.GetBytes(KeySize);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        // Combine: salt + IV + ciphertext
        var result = new byte[SaltSize + IvSize + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(aes.IV, 0, result, SaltSize, IvSize);
        Buffer.BlockCopy(ciphertext, 0, result, SaltSize + IvSize, ciphertext.Length);

        return EncryptedPrefix + Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypt a value if it starts with "ENC:". Otherwise returns the value as-is.
    /// This allows gradual migration — plaintext values still work.
    /// </summary>
    public static string DecryptIfNeeded(string value, string passphrase)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (!value.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            return value; // Plaintext — return as-is

        return Decrypt(value, passphrase);
    }

    /// <summary>
    /// Decrypt a value that starts with "ENC:".
    /// </summary>
    public static string Decrypt(string encryptedValue, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedValue);

        if (!encryptedValue.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            throw new ArgumentException("Value is not encrypted (missing ENC: prefix).");

        var combined = Convert.FromBase64String(encryptedValue[EncryptedPrefix.Length..]);

        if (combined.Length < SaltSize + IvSize + 1)
            throw new CryptographicException("Encrypted data is too short.");

        var salt = new byte[SaltSize];
        var iv = new byte[IvSize];
        var ciphertext = new byte[combined.Length - SaltSize - IvSize];

        Buffer.BlockCopy(combined, 0, salt, 0, SaltSize);
        Buffer.BlockCopy(combined, SaltSize, iv, 0, IvSize);
        Buffer.BlockCopy(combined, SaltSize + IvSize, ciphertext, 0, ciphertext.Length);

        using var keyDerivation = new Rfc2898DeriveBytes(passphrase, salt, Iterations, HashAlgorithmName.SHA256);
        var key = keyDerivation.GetBytes(KeySize);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var plaintextBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    /// <summary>
    /// Check if a value is encrypted (starts with "ENC:").
    /// </summary>
    public static bool IsEncrypted(string? value)
        => value != null && value.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
}
