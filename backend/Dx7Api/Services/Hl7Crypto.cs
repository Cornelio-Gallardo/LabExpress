using System.Security.Cryptography;
using System.Text;

namespace Dx7Api.Services;

/// <summary>
/// CDM §2.1 — raw_payload MUST be retained encrypted at rest.
/// AES-256-GCM authenticated encryption. Initialize once at startup via Program.cs.
///
/// Wire-format (base64): nonce(12) ‖ tag(16) ‖ ciphertext(n)
/// </summary>
public static class Hl7Crypto
{
    private static byte[]? _key;

    /// <summary>
    /// Call once at startup with the base64-encoded 32-byte key from configuration.
    /// Throws if the key is not exactly 32 bytes when decoded.
    /// </summary>
    public static void Initialize(string base64Key)
    {
        var keyBytes = Convert.FromBase64String(base64Key);
        if (keyBytes.Length != 32)
            throw new InvalidOperationException(
                "Hl7Encryption:Key must decode to exactly 32 bytes (256-bit AES key). " +
                "Generate with: Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))");
        _key = keyBytes;
    }

    public static bool IsConfigured => _key != null;

    /// <summary>Encrypt plaintext. Returns base64 envelope. Falls back to plaintext if key not configured.</summary>
    public static string Encrypt(string plaintext)
    {
        if (_key == null) return plaintext;

        var nonce      = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        RandomNumberGenerator.Fill(nonce);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext     = new byte[plaintextBytes.Length];
        var tag            = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Envelope: nonce(12) ‖ tag(16) ‖ ciphertext
        var envelope = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(envelope, 0);
        tag.CopyTo(envelope, nonce.Length);
        ciphertext.CopyTo(envelope, nonce.Length + tag.Length);

        return Convert.ToBase64String(envelope);
    }

    /// <summary>Decrypt base64 envelope. Returns original plaintext. Falls back gracefully for legacy unencrypted rows.</summary>
    public static string Decrypt(string stored)
    {
        if (_key == null) return stored;

        try
        {
            var envelope = Convert.FromBase64String(stored);
            if (envelope.Length < 28) return stored; // too short to be a valid envelope — legacy row

            var nonce      = envelope[..12];
            var tag        = envelope[12..28];
            var ciphertext = envelope[28..];

            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            // Decryption failed — legacy unencrypted row or wrong key; return as-is for traceability
            return stored;
        }
    }
}
