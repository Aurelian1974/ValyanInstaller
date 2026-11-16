using System.Globalization;

namespace Erp.Licensing.Generator;

/// <summary>Câmpurile ecranului generatorului, pentru legarea mesajelor de validare (3.2).</summary>
public static class LicenseDraftField
{
    public const string CustomerName = nameof(CustomerName);
    public const string FiscalCodes = nameof(FiscalCodes);
    public const string Modules = nameof(Modules);
    public const string ValidUntil = nameof(ValidUntil);
    public const string Users = nameof(Users);
    public const string PayrollEmployees = nameof(PayrollEmployees);
    public const string PosTerminals = nameof(PosTerminals);
}

/// <summary>
/// Conținutul ecranului „Licență nouă” (3.2), independent de interfață: ViewModel-ul îl
/// completează din câmpuri, iar validarea întoarce mesajele afișate lângă fiecare câmp.
/// </summary>
public sealed class LicenseDraft
{
    /// <summary>Denumirea clientului, 2–200 de caractere (3.2).</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Textul din câmpul „CUI-uri”, așa cum l-a scris operatorul (3.2).</summary>
    public string FiscalCodesText { get; set; } = string.Empty;

    /// <summary>Codurile modulelor bifate; Nucleul se adaugă oricum (3.2).</summary>
    public IList<string> Modules { get; init; } = new List<string> { LicenseModules.Core };

    public int Users { get; set; } = 100;

    public int PayrollEmployees { get; set; } = 300;

    public int PosTerminals { get; set; } = 10;

    /// <summary>„Nelimitat” bifat: licența nu expiră (3.3).</summary>
    public bool IsUnlimited { get; set; }

    /// <summary>Data până la care e valabilă; ignorată când <see cref="IsUnlimited"/> e adevărat.</summary>
    public DateOnly? ValidUntil { get; set; }

    /// <summary>Numărul licenței, luat din registru (3.6).</summary>
    public LicenseNumber Number { get; set; }

    /// <summary>Valoarea implicită a câmpului „Valabilă până la”: un an de azi (3.2).</summary>
    public static DateOnly DefaultValidUntil(DateOnly today) => today.AddYears(1);

    /// <summary>
    /// Verifică toate câmpurile și, dacă nu există erori, produce conținutul licenței (3.3).
    /// </summary>
    /// <param name="today">Data curentă, pentru „emis la” și pentru valabilitate.</param>
    /// <param name="enforceCheckDigit">
    /// Vezi <see cref="FiscalCode.ParseList(string, bool?)"/>; implicit urmează
    /// <see cref="LicensingPolicy.EnforceFiscalCodeCheckDigit"/> (decizia D12).
    /// </param>
    public LicenseDraftValidation Validate(DateOnly today, bool? enforceCheckDigit = null)
    {
        var errors = new List<LicenseDraftMessage>();
        var warnings = new List<LicenseDraftMessage>();

        var customerName = CustomerName.Trim();
        if (customerName.Length == 0)
        {
            errors.Add(new(LicenseDraftField.CustomerName, "Scrieți denumirea clientului."));
        }
        else if (customerName.Length is < 2 or > 200)
        {
            errors.Add(new(LicenseDraftField.CustomerName, "Denumirea clientului are între 2 și 200 de caractere."));
        }

        var codes = FiscalCode.ParseList(FiscalCodesText, enforceCheckDigit);
        if (codes.Accepted.Count == 0 && codes.Invalid.Count == 0)
        {
            errors.Add(new(LicenseDraftField.FiscalCodes, "Scrieți cel puțin un CUI."));
        }

        if (codes.Invalid.Count > 0)
        {
            errors.Add(new(
                LicenseDraftField.FiscalCodes,
                codes.Invalid.Count == 1
                    ? $"CUI invalid: {codes.Invalid[0]}."
                    : $"CUI-uri invalide: {string.Join(", ", codes.Invalid)}."));
        }

        if (codes.Duplicates.Count > 0)
        {
            errors.Add(new(
                LicenseDraftField.FiscalCodes,
                $"CUI-uri repetate: {string.Join(", ", codes.Duplicates.Distinct(StringComparer.Ordinal))}."));
        }

        if (codes.HasWeakCheckDigits)
        {
            warnings.Add(new(
                LicenseDraftField.FiscalCodes,
                $"Cifra de control nu e corectă la: {string.Join(", ", codes.WeakCheckDigit)}. "
                + "Se acceptă doar în timpul dezvoltării (decizia D12)."));
        }

        var modules = NormalizeModules();
        if (!LicenseModules.AreKnown(modules))
        {
            errors.Add(new(LicenseDraftField.Modules, "Lista de module conține un cod necunoscut."));
        }

        DateOnly? validUntil = null;
        if (!IsUnlimited)
        {
            if (ValidUntil is not { } until)
            {
                errors.Add(new(LicenseDraftField.ValidUntil, "Alegeți data până la care e valabilă licența sau bifați „Nelimitat”."));
            }
            else if (until < today)
            {
                errors.Add(new(
                    LicenseDraftField.ValidUntil,
                    $"Data nu poate fi în trecut; azi e {today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}."));
            }
            else
            {
                validUntil = until;
            }
        }

        if (Users < 1)
        {
            errors.Add(new(LicenseDraftField.Users, "Numărul de utilizatori e cel puțin 1."));
        }

        if (PayrollEmployees < 0)
        {
            errors.Add(new(LicenseDraftField.PayrollEmployees, "Numărul de angajați nu poate fi negativ."));
        }

        if (PosTerminals < 0)
        {
            errors.Add(new(LicenseDraftField.PosTerminals, "Numărul de case POS nu poate fi negativ."));
        }

        if (Number.Sequence == 0)
        {
            errors.Add(new(LicenseDraftField.CustomerName, "Numărul licenței nu a fost calculat din registru."));
        }

        if (errors.Count > 0)
        {
            return new LicenseDraftValidation(errors, warnings, codes, null);
        }

        var content = new LicenseContent
        {
            LicenseId = Number.ToString(),
            Product = "ERP",
            CustomerName = customerName,
            FiscalCodes = codes.NormalizedList,
            Modules = modules,
            Limits = new LicenseLimits
            {
                Users = Users,
                PayrollEmployees = PayrollEmployees,
                PosTerminals = PosTerminals,
            },
            IssuedAt = today,
            ValidUntil = validUntil,
        };

        return new LicenseDraftValidation(errors, warnings, codes, content);
    }

    /// <summary>Modulele bifate, cu Nucleul întâi și în ordinea din 3.2, fără duplicate.</summary>
    private List<string> NormalizeModules()
    {
        var selected = Modules.ToHashSet(StringComparer.Ordinal);
        selected.Add(LicenseModules.Mandatory);

        var ordered = LicenseModules.All
            .Select(m => m.Key)
            .Where(selected.Contains)
            .ToList();

        // Codurile necunoscute se păstrează, ca validarea să le poată semnala.
        ordered.AddRange(selected.Where(code => !ordered.Contains(code, StringComparer.Ordinal)));
        return ordered;
    }
}

/// <summary>Un mesaj legat de un câmp al ecranului (3.2).</summary>
public sealed record LicenseDraftMessage(string Field, string Text);

/// <summary>Rezultatul validării ecranului „Licență nouă” (3.2).</summary>
public sealed record LicenseDraftValidation(
    IReadOnlyList<LicenseDraftMessage> Errors,
    IReadOnlyList<LicenseDraftMessage> Warnings,
    FiscalCodeParseResult FiscalCodes,
    LicenseContent? Content)
{
    /// <summary>„Generează licența” e activ numai când nu există erori.</summary>
    public bool CanGenerate => Errors.Count == 0 && Content is not null;

    /// <summary>Mesajele pentru un câmp, în ordine.</summary>
    public IEnumerable<string> ErrorsFor(string field)
        => Errors.Where(e => string.Equals(e.Field, field, StringComparison.Ordinal)).Select(e => e.Text);

    /// <summary>Textul afișat sub câmpul CUI: „✓ 4 CUI-uri valide” sau numărul de probleme.</summary>
    public string FiscalCodesSummary => FiscalCodes.Accepted.Count switch
    {
        0 => "Niciun CUI valid",
        1 => "1 CUI valid",
        var n => $"{n.ToString(CultureInfo.InvariantCulture)} CUI-uri valide",
    };
}
