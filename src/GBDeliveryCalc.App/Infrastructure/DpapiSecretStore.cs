using System.IO;
using System.Security.Cryptography;
using System.Text;
using GBDeliveryCalc.Data;
using GBDeliveryCalc.Presentation;

namespace GBDeliveryCalc.App.Infrastructure;

/// <summary>
/// Stores secrets as Windows DPAPI-encrypted files (CurrentUser scope) in the app data folder, e.g. google-key.bin.
/// Only the same Windows user on the same PC can decrypt them. They live beside, not inside, estimator.db, so
/// database backups and restores never carry them.
/// </summary>
public sealed class DpapiSecretStore(string directory) : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GroverBrothers.Estimator.Secrets.v1");

    public DpapiSecretStore() : this(AppDataPaths.DataDirectory) { }

    public string? Get(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return null;
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException) { return null; } // copied from another user or PC: treat as no key
        catch (IOException) { return null; }
    }

    public void Set(string name, string value)
    {
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(directory);
        var path = PathFor(name);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, cipher);
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    private string PathFor(string name)
    {
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(".."))
            throw new ArgumentException("Invalid secret name.", nameof(name));
        return Path.Combine(directory, name + ".bin");
    }
}
