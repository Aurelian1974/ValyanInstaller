using System.Text.Json.Serialization;

namespace Erp.Licensing;

/// <summary>
/// Conținutul licenței, așa cum stă în JSON-ul criptat (3.3).
/// Există doar în memorie: nu se scrie pe disc și nu se jurnalizează (R8).
/// </summary>
public sealed record LicenseContent
{
    /// <summary>Numărul licenței, <c>LIC-&lt;AAAA&gt;-&lt;NNNNNN&gt;</c> (3.2).</summary>
    [JsonPropertyName("licenseId")]
    public required string LicenseId { get; init; }

    /// <summary>Produsul; trebuie să fie exact <c>ERP</c> (4.2, verificarea 4).</summary>
    [JsonPropertyName("product")]
    public required string Product { get; init; }

    /// <summary>Denumirea clientului (3.2).</summary>
    [JsonPropertyName("customerName")]
    public required string CustomerName { get; init; }

    /// <summary>CUI-urile, un singur șir, doar cifre, separate prin „:” (3.3).</summary>
    [JsonPropertyName("fiscalCodes")]
    public required string FiscalCodes { get; init; }

    /// <summary>Codurile modulelor licențiate (<see cref="LicenseModules"/>).</summary>
    [JsonPropertyName("modules")]
    public required IReadOnlyList<string> Modules { get; init; }

    /// <summary>Limitele licenței (3.3).</summary>
    [JsonPropertyName("limits")]
    public required LicenseLimits Limits { get; init; }

    /// <summary>Data emiterii.</summary>
    [JsonPropertyName("issuedAt")]
    public required DateOnly IssuedAt { get; init; }

    /// <summary>Data expirării; <c>null</c> înseamnă „Nelimitat” (3.3).</summary>
    [JsonPropertyName("validUntil")]
    public DateOnly? ValidUntil { get; init; }

    /// <summary>CUI-urile ca listă (4.3).</summary>
    [JsonIgnore]
    public string[] FiscalCodeList => FiscalCode.SplitList(FiscalCodes);

    /// <summary>Numărul de firme licențiate = numărul de CUI-uri (3.2).</summary>
    [JsonIgnore]
    public int CompanyCount => FiscalCodeList.Length;

    /// <summary>„Firmă” sau „Grup de N firme” (3.2).</summary>
    [JsonIgnore]
    public string KindLabel => CompanyCount == 1
        ? "Firmă"
        : $"Grup de {CompanyCount} firme";

    /// <summary>Adevărat dacă modulul e licențiat; codurile se compară exact (NET-21).</summary>
    public bool HasModule(string moduleCode)
        => Modules.Contains(moduleCode, StringComparer.Ordinal);

    /// <summary>Adevărat dacă CUI-ul normalizat figurează în licență (4.3).</summary>
    public bool ContainsFiscalCode(string normalizedCode)
        => FiscalCodeList.Contains(normalizedCode, StringComparer.Ordinal);

    /// <summary>Adevărat dacă licența e expirată la data dată (4.2, verificarea 5).</summary>
    public bool IsExpiredAt(DateOnly date)
        => ValidUntil is { } until && until < date;
}

/// <summary>Limitele din licență (3.3).</summary>
public sealed record LicenseLimits
{
    [JsonPropertyName("users")]
    public required int Users { get; init; }

    [JsonPropertyName("payrollEmployees")]
    public required int PayrollEmployees { get; init; }

    [JsonPropertyName("posTerminals")]
    public required int PosTerminals { get; init; }
}
