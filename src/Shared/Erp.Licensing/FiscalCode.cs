using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Erp.Licensing;

/// <summary>
/// Normalizarea și validarea CUI-urilor (3.2, 4.3).
/// Forma normalizată conține numai cifre; lista se scrie separată prin „:” (3.3).
/// </summary>
public static partial class FiscalCode
{
    /// <summary>Separatorul listei de CUI-uri din licență (3.3).</summary>
    public const char ListSeparator = ':';

    /// <summary>Numărul maxim de cifre al unui CUI românesc.</summary>
    public const int MaxDigits = 10;

    /// <summary>Cheia de control a CUI-ului românesc (ANAF).</summary>
    private static readonly int[] CheckWeights = [7, 5, 3, 2, 1, 7, 5, 3, 2];

    /// <summary>Regula pentru un singur CUI normalizat (3.3).</summary>
    [GeneratedRegex(@"^[1-9][0-9]{1,9}$", RegexOptions.CultureInvariant)]
    public static partial Regex SingleCodePattern();

    /// <summary>Regula pentru întreaga listă din câmpul <c>fiscalCodes</c> (3.3).</summary>
    [GeneratedRegex(@"^[1-9][0-9]{1,9}(:[1-9][0-9]{1,9})*$", RegexOptions.CultureInvariant)]
    public static partial Regex ListPattern();

    /// <summary>
    /// Prefixul „RO”, eventual urmat de spații, înaintea cifrelor (3.2). Se elimină înainte
    /// de împărțirea listei, pentru că spațiul e și separator între CUI-uri: altfel
    /// „RO 12345678” s-ar rupe în două.
    /// </summary>
    [GeneratedRegex(@"[Rr][Oo][\s]*(?=[0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex RoPrefixPattern();

    /// <summary>
    /// Normalizează un CUI la partea numerică: elimină prefixul „RO”, spațiile și orice
    /// caracter care nu e cifră (3.2, 4.3). Nu verifică cifra de control.
    /// </summary>
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var digits = new StringBuilder(MaxDigits);
        foreach (var c in input)
        {
            if (char.IsAsciiDigit(c))
            {
                if (digits.Length == MaxDigits)
                {
                    return false;       // mai multe cifre decât poate avea un CUI
                }

                digits.Append(c);
            }
        }

        var candidate = digits.ToString();
        if (!SingleCodePattern().IsMatch(candidate))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    /// <summary>
    /// Verifică cifra de control a unui CUI deja normalizat (numai cifre).
    /// Folosit la introducerea CUI-urilor în generator (3.2) și în ERP, nu la citirea licenței.
    /// </summary>
    public static bool HasValidCheckDigit(string normalizedCode)
    {
        ArgumentNullException.ThrowIfNull(normalizedCode);
        if (!SingleCodePattern().IsMatch(normalizedCode))
        {
            return false;
        }

        var body = normalizedCode[..^1];
        var controlDigit = normalizedCode[^1] - '0';

        var sum = 0;
        var weightIndex = CheckWeights.Length - body.Length;      // alinierea la dreapta
        for (var i = 0; i < body.Length; i++)
        {
            sum += (body[i] - '0') * CheckWeights[weightIndex + i];
        }

        var expected = sum * 10 % 11;
        if (expected == 10)
        {
            expected = 0;
        }

        return expected == controlDigit;
    }

    /// <summary>
    /// Normalizează și validează complet un CUI (formă + cifră de control).
    /// </summary>
    public static bool TryParse(string? input, [NotNullWhen(true)] out string? normalized)
        => TryNormalize(input, out normalized) && HasValidCheckDigit(normalized);

    /// <summary>
    /// Împarte textul introdus de operator (rânduri noi, virgule, spații, „;” sau „:”)
    /// în CUI-uri normalizate, în ordinea introducerii, fără duplicate (3.2).
    /// </summary>
    public static FiscalCodeParseResult ParseList(string? input)
    {
        var accepted = new List<string>();
        var invalid = new List<string>();
        var duplicates = new List<string>();

        if (string.IsNullOrWhiteSpace(input))
        {
            return new FiscalCodeParseResult([], [], []);
        }

        var tokens = RoPrefixPattern().Replace(input, string.Empty).Split(
            ['\r', '\n', ',', ';', ' ', '\t', ListSeparator],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            if (!TryParse(token, out var code))
            {
                invalid.Add(token);
                continue;
            }

            if (accepted.Contains(code, StringComparer.Ordinal))
            {
                duplicates.Add(code);
                continue;
            }

            accepted.Add(code);
        }

        return new FiscalCodeParseResult(accepted, invalid, duplicates);
    }

    /// <summary>Scrie lista în forma din licență: <c>10000001:12345678</c> (3.3).</summary>
    public static string FormatList(IEnumerable<string> normalizedCodes)
    {
        ArgumentNullException.ThrowIfNull(normalizedCodes);
        return string.Join(ListSeparator, normalizedCodes);
    }

    /// <summary>Împarte câmpul <c>fiscalCodes</c> al licenței în CUI-uri (4.3).</summary>
    public static string[] SplitList(string fiscalCodes)
    {
        ArgumentNullException.ThrowIfNull(fiscalCodes);
        return fiscalCodes.Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Verifică lista așa cum apare în licență: forma din 3.3 și absența duplicatelor.
    /// </summary>
    public static bool IsValidList(string? fiscalCodes)
    {
        if (fiscalCodes is null || !ListPattern().IsMatch(fiscalCodes))
        {
            return false;
        }

        var codes = SplitList(fiscalCodes);
        return codes.Distinct(StringComparer.Ordinal).Count() == codes.Length;
    }

    /// <summary>Forma pentru afișare: prefixul „RO” se adaugă doar pe ecran (4.1).</summary>
    public static string ToDisplay(string normalizedCode)
        => string.Concat("RO", normalizedCode);

    /// <summary>
    /// Calculează cifra de control pentru un CUI fără ea (folosit în teste și în unelte).
    /// </summary>
    public static string WithCheckDigit(string bodyDigits)
    {
        ArgumentNullException.ThrowIfNull(bodyDigits);
        if (bodyDigits.Length is < 1 or > MaxDigits - 1 || !bodyDigits.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Partea numerică trebuie să aibă 1–9 cifre.", nameof(bodyDigits));
        }

        var sum = 0;
        var weightIndex = CheckWeights.Length - bodyDigits.Length;
        for (var i = 0; i < bodyDigits.Length; i++)
        {
            sum += (bodyDigits[i] - '0') * CheckWeights[weightIndex + i];
        }

        var control = sum * 10 % 11;
        if (control == 10)
        {
            control = 0;
        }

        return string.Concat(bodyDigits, control.ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>Rezultatul analizei listei introduse de operator (3.2).</summary>
public sealed record FiscalCodeParseResult(
    IReadOnlyList<string> Accepted,
    IReadOnlyList<string> Invalid,
    IReadOnlyList<string> Duplicates)
{
    /// <summary>Generarea licenței e permisă numai dacă nu există CUI greșit sau dublat.</summary>
    public bool IsUsable => Accepted.Count > 0 && Invalid.Count == 0 && Duplicates.Count == 0;

    /// <summary>Lista normalizată, afișată sub câmp și scrisă în licență (3.2, 3.3).</summary>
    public string NormalizedList => FiscalCode.FormatList(Accepted);
}
