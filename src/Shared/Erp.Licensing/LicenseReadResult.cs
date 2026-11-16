using System.Globalization;

namespace Erp.Licensing;

/// <summary>Motivul refuzului unei licențe (4.2).</summary>
public enum LicenseFailureCode
{
    /// <summary>Verificarea 1: marcaj, versiune sau lungimi greșite.</summary>
    NotAnErpLicense = 1,

    /// <summary>Verificarea 2: <c>KeyId</c> necunoscut sau semnătură ECDSA invalidă.</summary>
    NotIssuedByProducer = 2,

    /// <summary>Verificarea 3 sau 4: decriptarea, JSON-ul sau conținutul nu sunt valide.</summary>
    Corrupted = 3,

    /// <summary>Verificarea 5: licența a expirat.</summary>
    Expired = 4,
}

/// <summary>
/// Mesajele exacte afișate operatorului (4.2). Textele sunt în română (R11) și nu
/// conțin niciun detaliu criptografic sau date din licență (R8).
/// </summary>
public static class LicenseMessages
{
    public const string NotAnErpLicense = "Fișierul nu este o licență ERP.";

    public const string NotIssuedByProducer = "Licența nu este emisă de producător sau a fost modificată.";

    public const string Corrupted = "Licența este deteriorată.";

    /// <summary>Mesajul de expirare, cu data scrisă în forma zi.lună.an (4.2).</summary>
    public static string Expired(DateOnly validUntil)
        => $"Licența a expirat la {validUntil.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}. Cereți furnizorului o licență nouă.";
}

/// <summary>Rezultatul citirii unui fișier de licență (4.2).</summary>
public sealed class LicenseReadResult
{
    private LicenseReadResult(LicenseContent? license, LicenseFailureCode? failureCode, string? message)
    {
        License = license;
        FailureCode = failureCode;
        Message = message;
    }

    /// <summary>Licența e acceptată.</summary>
    public bool IsValid => FailureCode is null;

    /// <summary>
    /// Conținutul licenței. Nenul când licența e validă și, în plus, când singurul motiv
    /// de refuz e expirarea — aplicația are nevoie de el pentru perioada de grație (4.2).
    /// </summary>
    public LicenseContent? License { get; }

    /// <summary>Motivul refuzului.</summary>
    public LicenseFailureCode? FailureCode { get; }

    /// <summary>Mesajul în română pentru operator; nul când licența e validă.</summary>
    public string? Message { get; }

    /// <summary>Conținutul licenței valide; aruncă dacă licența a fost refuzată.</summary>
    public LicenseContent Value => License is not null && IsValid
        ? License
        : throw new InvalidOperationException(Message ?? LicenseMessages.NotAnErpLicense);

    internal static LicenseReadResult Valid(LicenseContent license)
        => new(license, null, null);

    internal static LicenseReadResult Failed(LicenseFailureCode code, string message)
        => new(null, code, message);

    internal static LicenseReadResult ExpiredLicense(LicenseContent license, DateOnly validUntil)
        => new(license, LicenseFailureCode.Expired, LicenseMessages.Expired(validUntil));
}

/// <summary>Opțiuni de citire (4.2).</summary>
public sealed record LicenseReadOptions
{
    /// <summary>Verificările complete, inclusiv expirarea: installer-ul le folosește (4.1).</summary>
    public static LicenseReadOptions Default { get; } = new();

    /// <summary>
    /// Fără verificarea expirării: aplicația citește licența așa și aplică apoi perioada
    /// de grație din 02.01, fluxul 4.4 (4.2, verificarea 5).
    /// </summary>
    public static LicenseReadOptions WithoutExpiryCheck { get; } = new() { CheckExpiry = false };

    /// <summary>Verifică <c>validUntil</c> față de data curentă.</summary>
    public bool CheckExpiry { get; init; } = true;
}
