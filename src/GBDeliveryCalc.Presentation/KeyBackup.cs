using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GBDeliveryCalc.Presentation;

/// <summary>
/// Password-protected export of the API keys (Settings → API keys → Back up keys), for moving them to another PC.
/// The keys are encrypted with AES-256-GCM using a key derived from the password with PBKDF2-SHA256, so the file is
/// useless without the password and any tampering is detected.
/// </summary>
public static class KeyBackup
{
    public const string Extension = ".estimator-keys";
    public const int MinimumPasswordLength = 8;
    private const string Format = "grover-estimator-keys";
    private const int Iterations = 600_000;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private sealed record Envelope(string Format, int Version, string Kdf, int Iterations, string Salt, string Nonce, string Tag, string Data);

    public static byte[] Export(IReadOnlyDictionary<string, string> keys, string password)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
            throw new ArgumentException($"The password must be at least {MinimumPasswordLength} characters.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var key = DeriveKey(password, salt, Iterations);
        var plain = JsonSerializer.SerializeToUtf8Bytes(keys);
        var cipher = new byte[plain.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(Format));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
        var envelope = new Envelope(Format, 1, "PBKDF2-SHA256", Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag), Convert.ToBase64String(cipher));
        return JsonSerializer.SerializeToUtf8Bytes(envelope, Indented);
    }

    /// <summary>Decrypts an export. Throws <see cref="InvalidDataException"/> for a wrong password or a damaged or foreign file.</summary>
    public static IReadOnlyDictionary<string, string> Import(byte[] file, string password)
    {
        Envelope? e;
        try { e = JsonSerializer.Deserialize<Envelope>(file); }
        catch (JsonException) { e = null; }
        if (e is null || e.Format != Format || e.Version != 1 || e.Kdf != "PBKDF2-SHA256" || e.Iterations is < 100_000 or > 10_000_000)
            throw new InvalidDataException("This isn't a GB DeliveryCalc key backup.");

        byte[] salt, nonce, tag, cipher;
        try
        {
            salt = Convert.FromBase64String(e.Salt);
            nonce = Convert.FromBase64String(e.Nonce);
            tag = Convert.FromBase64String(e.Tag);
            cipher = Convert.FromBase64String(e.Data);
        }
        catch (FormatException) { throw new InvalidDataException("The key backup is damaged."); }

        var key = DeriveKey(password ?? "", salt, e.Iterations);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(Format));
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? [];
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new InvalidDataException("Wrong password, or the key backup has been changed or damaged.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
}
