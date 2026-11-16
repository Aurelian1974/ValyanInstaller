using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Erp.Licensing.Generator;

/// <summary>
/// Protejarea cheii private pe calculatorul care emite licențele. În produs e DPAPI
/// <c>CurrentUser</c> (3.5); în teste, o implementare echivalentă în memorie.
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] data);

    byte[] Unprotect(byte[] data);
}

/// <summary>
/// Depozitul cheilor de semnare: <c>%APPDATA%\ErpLicenseGen\keys\sign-&lt;KeyId&gt;.bin</c>,
/// criptat DPAPI <c>CurrentUser</c> (3.5). Cheia privată nu părăsește acest calculator decât
/// prin copia de siguranță protejată cu parolă, păstrată offline (R7, SEC-02).
/// </summary>
public sealed partial class LicenseKeyStore
{
    private readonly string folder;
    private readonly ISecretProtector protector;

    public LicenseKeyStore(string folder, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(protector);
        this.folder = folder;
        this.protector = protector;
    }

    /// <summary>Folderul implicit, pe calculatorul care emite licențele (3.5).</summary>
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ErpLicenseGen",
        "keys");

    [GeneratedRegex(@"^sign-(?<id>[0-9]{1,3})\.bin$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    /// <summary>Parametrii copiei de siguranță: PBES2 cu SHA-256 și 600.000 de iterații.</summary>
    private static PbeParameters BackupParameters => new(
        PbeEncryptionAlgorithm.Aes256Cbc,
        HashAlgorithmName.SHA256,
        iterationCount: 600_000);

    public string Folder => folder;

    /// <summary>Cheile existente, în ordine crescătoare.</summary>
    public IReadOnlyList<byte> KeyIds
    {
        get
        {
            if (!Directory.Exists(folder))
            {
                return [];
            }

            var ids = new List<byte>();
            foreach (var file in Directory.EnumerateFiles(folder, "sign-*.bin"))
            {
                var match = FileNamePattern().Match(Path.GetFileName(file));
                if (match.Success && byte.TryParse(match.Groups["id"].ValueSpan, CultureInfo.InvariantCulture, out var id) && id > 0)
                {
                    ids.Add(id);
                }
            }

            ids.Sort();
            return ids;
        }
    }

    /// <summary>Cheia cu numărul cel mai mare: cea folosită la emiterea licențelor noi (3.5).</summary>
    public byte? CurrentKeyId => KeyIds.Count == 0 ? null : KeyIds[^1];

    public bool Exists(byte keyId) => File.Exists(PathFor(keyId));

    /// <summary>
    /// Salvează cheia privată a unei perechi nou generate. Nu suprascrie o cheie existentă:
    /// o cheie pierdută ar invalida toate licențele emise cu ea (3.5).
    /// </summary>
    public void Save(GeneratedLicenseKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (Exists(keys.KeyId))
        {
            throw new InvalidOperationException(
                $"Cheia {keys.KeyId.ToString(CultureInfo.InvariantCulture)} există deja; "
                + "generați un KeyId nou în loc să o înlocuiți (3.5).");
        }

        Directory.CreateDirectory(folder);
        var protectedKey = protector.Protect(keys.PrivateKeyPkcs8);
        File.WriteAllBytes(PathFor(keys.KeyId), protectedKey);
    }

    /// <summary>Încarcă cheia privată pentru semnare. Apelantul eliberează obiectul.</summary>
    public ECDsa LoadSigningKey(byte keyId)
    {
        var path = PathFor(keyId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Cheia de semnare {keyId.ToString(CultureInfo.InvariantCulture)} nu există pe acest calculator.",
                path);
        }

        var pkcs8 = protector.Unprotect(File.ReadAllBytes(path));
        try
        {
            var ecdsa = ECDsa.Create();
            try
            {
                ecdsa.ImportPkcs8PrivateKey(pkcs8, out _);
                return ecdsa;
            }
            catch
            {
                ecdsa.Dispose();
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    /// <summary>
    /// Copia de siguranță a cheii private, protejată cu parolă, pentru păstrare offline (3.5).
    /// </summary>
    public byte[] ExportBackup(byte keyId, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length < 12)
        {
            throw new ArgumentException("Parola copiei de siguranță are cel puțin 12 caractere.", nameof(password));
        }

        using var ecdsa = LoadSigningKey(keyId);
        return ecdsa.ExportEncryptedPkcs8PrivateKey(password, BackupParameters);
    }

    /// <summary>Reface cheia privată din copia de siguranță, pe un calculator nou (3.5).</summary>
    public void ImportBackup(byte keyId, byte[] encryptedPkcs8, string password)
    {
        ArgumentNullException.ThrowIfNull(encryptedPkcs8);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportEncryptedPkcs8PrivateKey(password, encryptedPkcs8, out _);

        Save(new GeneratedLicenseKeys(
            keyId,
            ecdsa.ExportPkcs8PrivateKey(),
            ecdsa.ExportSubjectPublicKeyInfo(),
            AesKey: [],
            MaskedAesKey: [],
            AesKeyMask: []));
    }

    /// <summary>
    /// Verifică faptul că cheia privată de pe acest calculator se potrivește cu cheia publică
    /// din <c>LicenseKeys.cs</c>: altfel licențele emise ar fi refuzate de ERP (3.5, 4.2).
    /// </summary>
    public bool MatchesPublicKey(byte keyId, LicenseKeyMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);

        using var privateKey = LoadSigningKey(keyId);
        using var publicKey = material.CreateEcdsa();

        var probe = "ERPLIC-probe"u8.ToArray();
        var signature = privateKey.SignData(probe, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return publicKey.VerifyData(probe, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private string PathFor(byte keyId)
        => Path.Combine(folder, $"sign-{keyId.ToString(CultureInfo.InvariantCulture)}.bin");
}
