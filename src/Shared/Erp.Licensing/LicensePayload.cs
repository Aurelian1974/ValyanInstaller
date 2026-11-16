using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Erp.Licensing;

/// <summary>
/// Textul canonic al conținutului licenței și semnătura detașată de deasupra lui (3.3, decizia D15).
/// <para>
/// Semnătura e produsă de generator, cu aceeași cheie privată ECDSA P-256 ca semnătura fișierului,
/// și e purtată în licență (`contentSignature`). Installer-ul o copiază în baza de date împreună
/// cu datele în clar, iar ERP-ul o verifică având numai cheia publică: fără decriptare și fără
/// cheia AES (4.1, 4.3). Un rând modificat în baza de date nu mai are semnătură validă.
/// </para>
/// </summary>
public static class LicensePayload
{
    /// <summary>Marcajul versiunii textului canonic, primul rând (3.3).</summary>
    public const string Version = "ERPLIC-P1";

    /// <summary>Lungimea semnăturii, ECDSA P-256 în format IEEE P1363 (3.4).</summary>
    public const int SignatureLength = LicenseFileFormat.SignatureLength;

    /// <summary>Marcajul pentru „Nelimitat” în textul canonic (3.3).</summary>
    private const string Unlimited = "-";

    /// <summary>
    /// Textul canonic din 3.3, construit din datele licenței. Deterministic și independent de
    /// cultură: aceleași date dau același text în generator, în installer și în ERP.
    /// </summary>
    public static string Canonical(
        string licenseId,
        string product,
        string customerName,
        string fiscalCodes,
        IEnumerable<string> modules,
        int users,
        int payrollEmployees,
        int posTerminals,
        DateOnly issuedAt,
        DateOnly? validUntil)
    {
        ArgumentNullException.ThrowIfNull(licenseId);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(customerName);
        ArgumentNullException.ThrowIfNull(fiscalCodes);
        ArgumentNullException.ThrowIfNull(modules);

        var text = new StringBuilder();
        text.Append(Version).Append('\n')
            .Append(licenseId).Append('\n')
            .Append(product).Append('\n')
            .Append(customerName).Append('\n')
            .Append(fiscalCodes).Append('\n')
            .AppendJoin(' ', modules).Append('\n')
            .Append(users.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(payrollEmployees.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(posTerminals.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(Date(issuedAt)).Append('\n')
            .Append(validUntil is { } until ? Date(until) : Unlimited);

        return text.ToString();
    }

    /// <inheritdoc cref="Canonical(string, string, string, string, IEnumerable{string}, int, int, int, DateOnly, DateOnly?)"/>
    public static string Canonical(LicenseContent license)
    {
        ArgumentNullException.ThrowIfNull(license);

        return Canonical(
            license.LicenseId,
            license.Product,
            license.CustomerName,
            license.FiscalCodes,
            license.Modules,
            license.Limits.Users,
            license.Limits.PayrollEmployees,
            license.Limits.PosTerminals,
            license.IssuedAt,
            license.ValidUntil);
    }

    /// <summary>Octeții semnați: textul canonic în UTF-8.</summary>
    public static byte[] CanonicalBytes(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        return LicenseFileFormat.ContentEncoding.GetBytes(canonical);
    }

    /// <summary>
    /// Verifică semnătura detașată peste un text canonic, cu cheia publică a unui <c>KeyId</c> (D15).
    /// </summary>
    public static bool Verify(string canonical, ReadOnlySpan<byte> signature, LicenseKeyMaterial material)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(material);

        if (signature.Length != SignatureLength)
        {
            return false;
        }

        using var ecdsa = material.CreateEcdsa();
        try
        {
            return ecdsa.VerifyData(
                CanonicalBytes(canonical),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Verifică semnătura detașată a unei licențe citite din fișier (4.2).</summary>
    public static bool Verify(LicenseContent license, LicenseKeyMaterial material)
    {
        ArgumentNullException.ThrowIfNull(license);

        if (!TryDecodeSignature(license.ContentSignature, out var signature))
        {
            return false;
        }

        return Verify(Canonical(license), signature, material);
    }

    /// <summary>
    /// Verifică semnătura detașată cu orice cheie publică din inel: ERP-ul nu știe dinainte cu
    /// care <c>KeyId</c> a fost emisă licența (3.5).
    /// </summary>
    public static bool Verify(string canonical, ReadOnlySpan<byte> signature, ILicenseKeyRing keyRing, byte? keyId = null)
    {
        ArgumentNullException.ThrowIfNull(keyRing);

        if (keyId is { } id)
        {
            return keyRing.TryGet(id, out var material) && Verify(canonical, signature, material);
        }

        foreach (var candidate in keyRing.KeyIds)
        {
            if (keyRing.TryGet(candidate, out var material) && Verify(canonical, signature, material))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Citește semnătura din Base64; respinge orice lungime diferită de 64 de octeți.</summary>
    public static bool TryDecodeSignature(string? base64, out byte[] signature)
    {
        signature = [];
        if (string.IsNullOrEmpty(base64))
        {
            return false;
        }

        var buffer = new byte[SignatureLength];
        if (!Convert.TryFromBase64String(base64, buffer, out var written) || written != SignatureLength)
        {
            return false;
        }

        signature = buffer;
        return true;
    }

    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
