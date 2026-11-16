namespace Erp.Licensing;

/// <summary>
/// Codurile modulelor care pot apărea în licență (3.2, 3.3) și denumirile lor în română.
/// Codurile sunt tehnice și se compară exact, cu <see cref="StringComparer.Ordinal"/> (NET-21).
/// </summary>
public static class LicenseModules
{
    public const string Core = "Core";
    public const string Finance = "Finance";
    public const string Sales = "Sales";
    public const string Purchasing = "Purchasing";
    public const string Inventory = "Inventory";
    public const string Wms = "Wms";
    public const string Tms = "Tms";
    public const string Manufacturing = "Manufacturing";
    public const string Hr = "Hr";
    public const string Pos = "Pos";
    public const string Construction = "Construction";
    public const string Crm = "Crm";
    public const string Reporting = "Reporting";
    public const string Integrations = "Integrations";

    /// <summary>Nucleul e mereu licențiat: bifat și blocat în generator (3.2).</summary>
    public const string Mandatory = Core;

    /// <summary>Denumirile afișate, în ordinea din ecranul generatorului (3.2).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> All { get; } =
    [
        new(Core, "Nucleu"),
        new(Finance, "Finanțe"),
        new(Sales, "Vânzări"),
        new(Purchasing, "Achiziții"),
        new(Inventory, "Stocuri"),
        new(Wms, "WMS"),
        new(Tms, "Transport"),
        new(Manufacturing, "Producție"),
        new(Hr, "HR"),
        new(Pos, "POS"),
        new(Construction, "Construcții"),
        new(Crm, "CRM"),
        new(Reporting, "Raportare"),
        new(Integrations, "Integrări"),
    ];

    /// <summary>Denumirea în română a unui cod; codul necunoscut se afișează ca atare.</summary>
    public static string DisplayName(string moduleCode)
    {
        foreach (var (code, name) in All)
        {
            if (string.Equals(code, moduleCode, StringComparison.Ordinal))
            {
                return name;
            }
        }

        return moduleCode;
    }

    /// <summary>Adevărat dacă toate codurile sunt cunoscute.</summary>
    public static bool AreKnown(IEnumerable<string> moduleCodes)
    {
        ArgumentNullException.ThrowIfNull(moduleCodes);
        return moduleCodes.All(code => All.Any(m => string.Equals(m.Key, code, StringComparison.Ordinal)));
    }
}
