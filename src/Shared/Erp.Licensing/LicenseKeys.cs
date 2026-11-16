namespace Erp.Licensing;

/// <summary>
/// Cheile livrate cu produsul: cheia publică ECDSA și cheia AES mascată, pe <c>KeyId</c> (3.5).
/// <para>
/// Textul de mai jos îl generează butonul „Chei” din <c>ErpLicenseGen</c>, la prima
/// configurare, și se lipește aici. Cheia privată de semnare rămâne numai pe calculatorul
/// care generează licențele, protejată DPAPI, și nu intră niciodată în depozit (R7, SEC-02).
/// </para>
/// <para>
/// La compromitere se adaugă un <c>KeyId</c> nou; intrările vechi rămân în listă, ca
/// licențele deja emise să fie în continuare acceptate (3.5).
/// </para>
/// </summary>
public static class LicenseKeys
{
    /// <summary>Inelul de chei folosit de ERP, de installer și de generator.</summary>
    public static ILicenseKeyRing Ring { get; } = new LicenseKeyRing(Materials());

    /// <summary>
    /// Adevărat dacă produsul are chei configurate. Cât timp e <c>false</c>, orice
    /// licență e refuzată la verificarea 2 din 4.2 („nu este emisă de producător”).
    /// </summary>
    public static bool IsConfigured => Ring.KeyIds.Count > 0;

    private static IEnumerable<LicenseKeyMaterial> Materials()
    {
        // ─────────────────────────────────────────────────────────────────────────────
        // Aici se lipesc cheile generate de butonul „Chei” (3.5). Exemplu de formă:
        //
        // yield return new LicenseKeyMaterial(
        //     keyId: 1,
        //     publicKeySpki: Convert.FromBase64String("MFkwEwYHKoZIzj0CAQYIKoZI…"),
        //     maskedAesKey: Convert.FromBase64String("7cD1…"),
        //     aesKeyMask:   Convert.FromBase64String("Q0Vt…"));
        //
        // Până la prima configurare lista e goală intenționat: nicio cheie nu e
        // inventată în cod, iar CI-ul nu conține material criptografic real.
        // ─────────────────────────────────────────────────────────────────────────────
        yield break;
    }
}
