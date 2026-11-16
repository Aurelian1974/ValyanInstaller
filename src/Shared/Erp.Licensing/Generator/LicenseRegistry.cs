using System.IO;
using System.Globalization;
using System.Text;

namespace Erp.Licensing.Generator;

/// <summary>
/// Registrul local al licențelor emise: <c>%APPDATA%\ErpLicenseGen\licente-emise.csv</c> (3.6).
/// Din el se calculează următorul număr de licență și se poate reface o licență pierdută.
/// Nu conține chei.
/// </summary>
public sealed class LicenseRegistry
{
    private const char Delimiter = ';';

    private static readonly string[] Header =
    [
        "NumarLicenta", "Data", "Client", "CuiUri", "Module",
        "Valabilitate", "Utilizatori", "AngajatiSalarizare", "CasePos", "Fisier",
    ];

    private readonly string path;

    public LicenseRegistry(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    /// <summary>Calea implicită, pe calculatorul care emite licențele (3.6).</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ErpLicenseGen",
        "licente-emise.csv");

    /// <summary>Calea fișierului CSV.</summary>
    public string FilePath => path;

    /// <summary>Licențele emise, în ordinea din fișier. Fișierul lipsă = registru gol.</summary>
    public IReadOnlyList<LicenseRegistryEntry> Read()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var entries = new List<LicenseRegistryEntry>();
        foreach (var fields in CsvFile.ReadRows(File.ReadAllText(path, Encoding.UTF8), Delimiter))
        {
            if (fields.Count == 0 || string.Equals(fields[0], Header[0], StringComparison.Ordinal))
            {
                continue;                                 // antetul sau un rând gol
            }

            if (LicenseRegistryEntry.TryFromFields(fields, out var entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>Numărul de licențe emise.</summary>
    public int Count => Read().Count;

    /// <summary>
    /// Următorul număr de licență al anului dat: cel mai mare număr existent + 1 (3.2, 3.6).
    /// </summary>
    public LicenseNumber NextNumber(int year)
    {
        var last = Read()
            .Select(e => e.Number)
            .Where(n => n.Year == year)
            .DefaultIfEmpty()
            .Max();

        return last.Sequence == 0 ? LicenseNumber.First(year) : last.Next();
    }

    /// <summary>Adaugă o licență emisă. Creează folderul și antetul la prima scriere.</summary>
    public void Append(LicenseRegistryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
        var text = new StringBuilder();
        if (isNew)
        {
            // BOM-ul face ca Excel să deschidă corect diacriticele din denumirea clientului.
            text.Append('﻿').AppendLine(CsvFile.WriteRow(Header, Delimiter));
        }

        text.AppendLine(CsvFile.WriteRow(entry.ToFields(), Delimiter));
        File.AppendAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

/// <summary>Un rând din registrul licențelor emise (3.6).</summary>
public sealed record LicenseRegistryEntry
{
    public required LicenseNumber Number { get; init; }

    public required DateOnly IssuedAt { get; init; }

    public required string CustomerName { get; init; }

    /// <summary>CUI-urile în forma din licență, separate prin „:” (3.3).</summary>
    public required string FiscalCodes { get; init; }

    /// <summary>Codurile modulelor, separate prin spațiu.</summary>
    public required IReadOnlyList<string> Modules { get; init; }

    /// <summary><c>null</c> = „Nelimitat” (3.3).</summary>
    public DateOnly? ValidUntil { get; init; }

    public required LicenseLimits Limits { get; init; }

    /// <summary>Numele fișierului salvat.</summary>
    public required string FileName { get; init; }

    /// <summary>Rândul corespunzător unei licențe generate.</summary>
    public static LicenseRegistryEntry FromLicense(LicenseContent content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);

        if (!LicenseNumber.TryParse(content.LicenseId, out var number))
        {
            throw new ArgumentException(
                "Numărul licenței nu are forma LIC-AAAA-NNNNNN (3.2).", nameof(content));
        }

        return new LicenseRegistryEntry
        {
            Number = number,
            IssuedAt = content.IssuedAt,
            CustomerName = content.CustomerName,
            FiscalCodes = content.FiscalCodes,
            Modules = content.Modules,
            ValidUntil = content.ValidUntil,
            Limits = content.Limits,
            FileName = fileName,
        };
    }

    /// <summary>Valabilitatea pentru afișare și pentru registru.</summary>
    public string ValidUntilText => ValidUntil is { } until
        ? until.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
        : "Nelimitat";

    internal IReadOnlyList<string> ToFields() =>
    [
        Number.ToString(),
        IssuedAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        CustomerName,
        FiscalCodes,
        string.Join(' ', Modules),
        ValidUntilText,
        Limits.Users.ToString(CultureInfo.InvariantCulture),
        Limits.PayrollEmployees.ToString(CultureInfo.InvariantCulture),
        Limits.PosTerminals.ToString(CultureInfo.InvariantCulture),
        FileName,
    ];

    internal static bool TryFromFields(IReadOnlyList<string> fields, out LicenseRegistryEntry entry)
    {
        entry = null!;
        if (fields.Count < 10 || !LicenseNumber.TryParse(fields[0], out var number))
        {
            return false;
        }

        if (!DateOnly.TryParseExact(fields[1], "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var issuedAt))
        {
            return false;
        }

        DateOnly? validUntil = DateOnly.TryParseExact(
            fields[5], "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var until)
            ? until
            : null;

        entry = new LicenseRegistryEntry
        {
            Number = number,
            IssuedAt = issuedAt,
            CustomerName = fields[2],
            FiscalCodes = fields[3],
            Modules = fields[4].Split(' ', StringSplitOptions.RemoveEmptyEntries),
            ValidUntil = validUntil,
            Limits = new LicenseLimits
            {
                Users = ParseInt(fields[6]),
                PayrollEmployees = ParseInt(fields[7]),
                PosTerminals = ParseInt(fields[8]),
            },
            FileName = fields[9],
        };
        return true;

        static int ParseInt(string text)
            => int.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }
}
