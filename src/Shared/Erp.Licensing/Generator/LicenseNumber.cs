using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Erp.Licensing.Generator;

/// <summary>
/// Numărul licenței, <c>LIC-&lt;AAAA&gt;-&lt;NNNNNN&gt;</c> (3.2). Se calculează din registrul
/// local al licențelor emise (3.6).
/// </summary>
public readonly partial record struct LicenseNumber(int Year, int Sequence)
    : IComparable<LicenseNumber>
{
    /// <summary>Numărul maxim de licențe emise într-un an, dat de cele 6 cifre.</summary>
    public const int MaxSequence = 999_999;

    [GeneratedRegex(@"^LIC-(?<year>[0-9]{4})-(?<seq>[0-9]{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>Prima licență a unui an.</summary>
    public static LicenseNumber First(int year) => new(year, 1);

    public static bool TryParse(string? text, out LicenseNumber number)
    {
        number = default;
        if (text is null)
        {
            return false;
        }

        var match = Pattern().Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        var year = int.Parse(match.Groups["year"].ValueSpan, CultureInfo.InvariantCulture);
        var sequence = int.Parse(match.Groups["seq"].ValueSpan, CultureInfo.InvariantCulture);
        if (sequence == 0)
        {
            return false;
        }

        number = new LicenseNumber(year, sequence);
        return true;
    }

    /// <summary>Numărul următor din același an.</summary>
    public LicenseNumber Next()
        => Sequence >= MaxSequence
            ? throw new InvalidOperationException(
                $"Anul {Year.ToString(CultureInfo.InvariantCulture)} a atins numărul maxim de licențe.")
            : this with { Sequence = Sequence + 1 };

    public int CompareTo(LicenseNumber other)
        => Year != other.Year ? Year.CompareTo(other.Year) : Sequence.CompareTo(other.Sequence);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"LIC-{Year:0000}-{Sequence:000000}");

    public static bool operator <(LicenseNumber left, LicenseNumber right) => left.CompareTo(right) < 0;

    public static bool operator >(LicenseNumber left, LicenseNumber right) => left.CompareTo(right) > 0;

    public static bool operator <=(LicenseNumber left, LicenseNumber right) => left.CompareTo(right) <= 0;

    public static bool operator >=(LicenseNumber left, LicenseNumber right) => left.CompareTo(right) >= 0;
}

/// <summary>Numele de fișier propus la salvare: <c>&lt;Client&gt;-&lt;NumărLicență&gt;.lic</c> (3.2).</summary>
public static class LicenseFileName
{
    public const string Extension = ".lic";

    /// <summary>
    /// Caracterele interzise în numele de fișier pe Windows. Lista e scrisă explicit, nu luată
    /// din <c>Path.GetInvalidFileNameChars()</c>, care întoarce altceva pe Linux (în CI) decât
    /// pe Windows, unde rulează generatorul.
    /// </summary>
    private static readonly char[] InvalidNameChars =
        [.. "<>:\"/\\|?*", .. Enumerable.Range(0, 32).Select(c => (char)c)];

    /// <summary>Caracterele interzise în numele de fișier se înlocuiesc cu „-”.</summary>
    public static string Suggest(string customerName, LicenseNumber number)
    {
        ArgumentNullException.ThrowIfNull(customerName);

        var cleaned = new string([.. customerName
            .Trim()
            .Select(c => InvalidNameChars.Contains(c) || c == ' ' ? '-' : c)]);

        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim('-', '.');
        if (cleaned.Length == 0)
        {
            cleaned = "Client";
        }

        if (cleaned.Length > 60)
        {
            cleaned = cleaned[..60].TrimEnd('-', '.');
        }

        return $"{cleaned}-{number}{Extension}";
    }
}
