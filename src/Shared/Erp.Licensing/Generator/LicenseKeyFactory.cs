using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Erp.Licensing.Generator;

/// <summary>
/// Generarea perechii de chei pentru un <c>KeyId</c> nou, folosită de butonul „Chei”
/// din generator (3.5). Face parte numai din generator, ca <see cref="LicenseWriter"/>.
/// </summary>
public static class LicenseKeyFactory
{
    /// <summary>Creează o cheie ECDSA P-256, o cheie AES-256 și masca ei (3.5).</summary>
    public static GeneratedLicenseKeys Create(byte keyId)
    {
        if (keyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keyId), "KeyId este între 1 și 255 (3.4).");
        }

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var aesKey = RandomNumberGenerator.GetBytes(LicenseKeyMaterial.AesKeyLength);
        var mask = RandomNumberGenerator.GetBytes(LicenseKeyMaterial.AesKeyLength);
        var masked = new byte[LicenseKeyMaterial.AesKeyLength];
        for (var i = 0; i < masked.Length; i++)
        {
            masked[i] = (byte)(aesKey[i] ^ mask[i]);
        }

        return new GeneratedLicenseKeys(
            keyId,
            ecdsa.ExportPkcs8PrivateKey(),
            ecdsa.ExportSubjectPublicKeyInfo(),
            aesKey,
            masked,
            mask);
    }
}

/// <summary>
/// Cheile generate pentru un <c>KeyId</c>. <see cref="PrivateKeyPkcs8"/> se salvează numai
/// local, criptat DPAPI, și nu intră niciodată în depozit sau în kit (3.5, R7).
/// </summary>
public sealed record GeneratedLicenseKeys(
    byte KeyId,
    byte[] PrivateKeyPkcs8,
    byte[] PublicKeySpki,
    byte[] AesKey,
    byte[] MaskedAesKey,
    byte[] AesKeyMask)
{
    /// <summary>Materialul public corespunzător, pentru verificări locale.</summary>
    public LicenseKeyMaterial ToKeyMaterial()
        => new(KeyId, PublicKeySpki, MaskedAesKey, AesKeyMask);

    /// <summary>
    /// Textul C# de lipit în <c>LicenseKeys.cs</c> (3.5). Nu conține cheia privată.
    /// </summary>
    public string ToLicenseKeysSnippet()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"        yield return new LicenseKeyMaterial(")
          .AppendLine()
          .Append(CultureInfo.InvariantCulture, $"            keyId: {KeyId},")
          .AppendLine()
          .Append(CultureInfo.InvariantCulture, $"            publicKeySpki: Convert.FromBase64String(\"{Convert.ToBase64String(PublicKeySpki)}\"),")
          .AppendLine()
          .Append(CultureInfo.InvariantCulture, $"            maskedAesKey: Convert.FromBase64String(\"{Convert.ToBase64String(MaskedAesKey)}\"),")
          .AppendLine()
          .Append(CultureInfo.InvariantCulture, $"            aesKeyMask: Convert.FromBase64String(\"{Convert.ToBase64String(AesKeyMask)}\"));");
        return sb.ToString();
    }
}
