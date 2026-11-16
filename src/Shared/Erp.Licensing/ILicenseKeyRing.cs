using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Erp.Licensing;

/// <summary>
/// Cheile publice de verificare și cheile AES, pe <c>KeyId</c> (3.5).
/// Cheia privată de semnare nu apare niciodată aici: ea există doar în generator (R7).
/// </summary>
public interface ILicenseKeyRing
{
    /// <summary>Identificatorii de chei cunoscuți, în ordine crescătoare.</summary>
    IReadOnlyList<byte> KeyIds { get; }

    /// <summary>Caută materialul de chei pentru un <c>KeyId</c> (4.2, verificarea 2).</summary>
    bool TryGet(byte keyId, [NotNullWhen(true)] out LicenseKeyMaterial? material);
}

/// <summary>
/// Materialul de chei pentru un <c>KeyId</c>: cheia publică ECDSA P-256 (SPKI) și
/// cheia AES-256, păstrată mascată prin XOR cu o a doua constantă, ca să nu apară
/// în clar ca șir de octeți în binar (3.5).
/// </summary>
public sealed class LicenseKeyMaterial
{
    private readonly byte[] maskedAesKey;
    private readonly byte[] aesKeyMask;

    public LicenseKeyMaterial(
        byte keyId,
        ReadOnlySpan<byte> publicKeySpki,
        ReadOnlySpan<byte> maskedAesKey,
        ReadOnlySpan<byte> aesKeyMask)
    {
        if (keyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keyId), "KeyId este între 1 și 255 (3.4).");
        }

        if (publicKeySpki.IsEmpty)
        {
            throw new ArgumentException("Cheia publică lipsește.", nameof(publicKeySpki));
        }

        if (maskedAesKey.Length != AesKeyLength)
        {
            throw new ArgumentException("Cheia AES mascată are 32 de octeți.", nameof(maskedAesKey));
        }

        if (aesKeyMask.Length != AesKeyLength)
        {
            throw new ArgumentException("Masca are 32 de octeți.", nameof(aesKeyMask));
        }

        KeyId = keyId;
        PublicKeySpki = publicKeySpki.ToArray();
        this.maskedAesKey = maskedAesKey.ToArray();
        this.aesKeyMask = aesKeyMask.ToArray();
    }

    /// <summary>Lungimea cheii AES-256 (3.4).</summary>
    public const int AesKeyLength = 32;

    public byte KeyId { get; }

    /// <summary>Cheia publică ECDSA P-256 în format SubjectPublicKeyInfo.</summary>
    public ReadOnlyMemory<byte> PublicKeySpki { get; }

    /// <summary>Creează obiectul ECDSA pentru verificarea semnăturii (3.4).</summary>
    public ECDsa CreateEcdsa()
    {
        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportSubjectPublicKeyInfo(PublicKeySpki.Span, out _);
            return ecdsa;
        }
        catch
        {
            ecdsa.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creează obiectul AES-GCM. Cheia se demască într-un tampon temporar, care se
    /// șterge imediat: nu rămâne nicio copie în clar în memorie (R8).
    /// </summary>
    public AesGcm CreateAesGcm()
    {
        var key = new byte[AesKeyLength];
        try
        {
            for (var i = 0; i < AesKeyLength; i++)
            {
                key[i] = (byte)(maskedAesKey[i] ^ aesKeyMask[i]);
            }

            return new AesGcm(key, LicenseFileFormat.TagLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}

/// <summary>Inel de chei construit dintr-o listă fixă de chei (3.5).</summary>
public sealed class LicenseKeyRing : ILicenseKeyRing
{
    private readonly Dictionary<byte, LicenseKeyMaterial> materials;

    public LicenseKeyRing(IEnumerable<LicenseKeyMaterial> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        this.materials = materials.ToDictionary(m => m.KeyId);
        KeyIds = [.. this.materials.Keys.Order()];
    }

    public IReadOnlyList<byte> KeyIds { get; }

    public bool TryGet(byte keyId, [NotNullWhen(true)] out LicenseKeyMaterial? material)
        => materials.TryGetValue(keyId, out material);
}
