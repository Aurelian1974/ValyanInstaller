using System.Security.Cryptography;
using System.Text.Json;

namespace Erp.Licensing.Generator;

/// <summary>
/// Scrierea fișierului de licență (3.4): se criptează AES-256-GCM, apoi se semnează
/// ECDSA P-256 peste tot fișierul în afară de semnătură.
/// <para>
/// Această clasă face parte NUMAI din generatorul de licențe: fișierul nu se compilează
/// în <c>Erp.Licensing</c>, deci nu ajunge în installer sau în ERP (3.4, R7).
/// </para>
/// </summary>
public sealed class LicenseWriter
{
    private static readonly JsonSerializerOptions ContentJsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly byte keyId;
    private readonly ECDsa signingKey;
    private readonly Func<AesGcm> aesFactory;

    /// <param name="keyId">Perechea de chei folosită, 1–255 (3.4).</param>
    /// <param name="signingKey">Cheia privată ECDSA P-256; rămâne în proprietatea apelantului.</param>
    /// <param name="aesKey">Cheia AES-256 (32 de octeți).</param>
    public LicenseWriter(byte keyId, ECDsa signingKey, ReadOnlySpan<byte> aesKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);

        if (keyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keyId), "KeyId este între 1 și 255 (3.4).");
        }

        if (aesKey.Length != LicenseKeyMaterial.AesKeyLength)
        {
            throw new ArgumentException("Cheia AES-256 are 32 de octeți.", nameof(aesKey));
        }

        this.keyId = keyId;
        this.signingKey = signingKey;

        var key = aesKey.ToArray();
        this.aesFactory = () => new AesGcm(key, LicenseFileFormat.TagLength);
    }

    /// <summary>
    /// Varianta folosită în generator: cheia AES vine din inelul de chei al produsului și
    /// rămâne mascată, fără să fie copiată vreodată în clar în afara <see cref="AesGcm"/> (3.5).
    /// </summary>
    public LicenseWriter(ECDsa signingKey, LicenseKeyMaterial material)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        ArgumentNullException.ThrowIfNull(material);

        this.keyId = material.KeyId;
        this.signingKey = signingKey;
        this.aesFactory = material.CreateAesGcm;
    }

    /// <summary>Produce octeții fișierului <c>.lic</c> pentru un conținut valid (3.3, 3.4).</summary>
    public byte[] Write(LicenseContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Validate(content);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(content, ContentJsonOptions);
        try
        {
            var file = new byte[LicenseFileFormat.FixedLength + plaintext.Length];
            var layout = new LicenseFileLayout(keyId, plaintext.Length);

            // Nonce aleator de 12 octeți, unic pentru fiecare licență (3.4).
            var nonce = file.AsSpan(LicenseFileFormat.NonceRange);
            RandomNumberGenerator.Fill(nonce);

            // Antetul se scrie înaintea criptării: el este AAD-ul pentru GCM (3.4).
            LicenseFileFormat.WriteHeader(file, keyId, nonce, plaintext.Length);

            using (var aes = aesFactory())
            {
                aes.Encrypt(
                    nonce: file.AsSpan(LicenseFileFormat.NonceRange),
                    plaintext: plaintext,
                    ciphertext: file.AsSpan(layout.Cipher),
                    tag: file.AsSpan(layout.Tag),
                    associatedData: file.AsSpan(LicenseFileFormat.HeaderRange));
            }

            // Se semnează tot ce s-a scris până acum: octeții 0 … 41 + n (3.4).
            var written = signingKey.SignData(
                file.AsSpan(layout.SignedBytes),
                file.AsSpan(layout.Signature),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            if (written != LicenseFileFormat.SignatureLength)
            {
                throw new CryptographicException(
                    "Semnătura nu are 64 de octeți; cheia de semnare nu este ECDSA P-256.");
            }

            return file;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static void Validate(LicenseContent content)
    {
        if (!string.Equals(content.Product, "ERP", StringComparison.Ordinal))
        {
            throw new ArgumentException("Produsul trebuie să fie „ERP” (3.3).", nameof(content));
        }

        if (!FiscalCode.IsValidList(content.FiscalCodes))
        {
            throw new ArgumentException(
                "Lista de CUI-uri nu respectă regula din 3.3 (doar cifre, separate prin „:”, fără duplicate).",
                nameof(content));
        }

        if (string.IsNullOrWhiteSpace(content.LicenseId))
        {
            throw new ArgumentException("Numărul licenței lipsește (3.2).", nameof(content));
        }

        if (string.IsNullOrWhiteSpace(content.CustomerName))
        {
            throw new ArgumentException("Denumirea clientului lipsește (3.2).", nameof(content));
        }

        if (!content.HasModule(LicenseModules.Mandatory))
        {
            throw new ArgumentException("Modulul Nucleu este obligatoriu (3.2).", nameof(content));
        }

        if (!LicenseModules.AreKnown(content.Modules))
        {
            throw new ArgumentException("Lista de module conține un cod necunoscut (3.2).", nameof(content));
        }
    }
}
