using Erp.Licensing;
using Erp.Licensing.Generator;

namespace Erp.Setup.Tests.Licensing;

/// <summary>Registrul licențelor emise (3.6).</summary>
public sealed class LicenseRegistryTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("erp-registry-").FullName;

    private LicenseRegistry Registry => new(Path.Combine(folder, "licente-emise.csv"));

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Registrul_lipsa_e_gol_si_porneste_de_la_prima_licenta()
    {
        var registry = Registry;

        Assert.Empty(registry.Read());
        Assert.Equal(new LicenseNumber(2026, 1), registry.NextNumber(2026));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Numarul_urmator_se_calculeaza_pe_an()
    {
        var registry = Registry;
        registry.Append(Entry(2026, 1));
        registry.Append(Entry(2026, 7));
        registry.Append(Entry(2027, 2));

        Assert.Equal(new LicenseNumber(2026, 8), registry.NextNumber(2026));
        Assert.Equal(new LicenseNumber(2027, 3), registry.NextNumber(2027));
        Assert.Equal(new LicenseNumber(2028, 1), registry.NextNumber(2028));
        Assert.Equal(3, registry.Count);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Un_rand_se_citeste_inapoi_intact()
    {
        var registry = Registry;
        var entry = Entry(2026, 123) with
        {
            CustomerName = "S.C. \"Exemplu\"; Filiala Nord",
            FiscalCodes = "10000001:12345678",
            ValidUntil = null,
        };

        registry.Append(entry);
        var read = Assert.Single(registry.Read());

        Assert.Equal(entry.Number, read.Number);
        Assert.Equal(entry.CustomerName, read.CustomerName);
        Assert.Equal(entry.FiscalCodes, read.FiscalCodes);
        Assert.Equal(entry.Modules, read.Modules);
        Assert.Null(read.ValidUntil);
        Assert.Equal("Nelimitat", read.ValidUntilText);
        Assert.Equal(entry.Limits, read.Limits);
        Assert.Equal(entry.FileName, read.FileName);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Registrul_nu_contine_chei_si_are_antet_o_singura_data()
    {
        var registry = Registry;
        registry.Append(Entry(2026, 1));
        registry.Append(Entry(2026, 2));

        var text = File.ReadAllText(registry.FilePath);

        Assert.Equal(1, text.Split("NumarLicenta").Length - 1);
        Assert.DoesNotContain("BEGIN", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sign-", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Rand_scris_dintr_o_licenta_generata()
    {
        using var keys = LicenseTestKeys.Create();
        var content = LicenseTestContent.Create();
        var fileName = LicenseFileName.Suggest(content.CustomerName, new LicenseNumber(2026, 123));

        var entry = LicenseRegistryEntry.FromLicense(content, fileName);

        Assert.Equal(new LicenseNumber(2026, 123), entry.Number);
        Assert.Equal(content.FiscalCodes, entry.FiscalCodes);
        Assert.Equal("Grupul-Exemplu-LIC-2026-000123.lic", entry.FileName);
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static LicenseRegistryEntry Entry(int year, int sequence) => new()
    {
        Number = new LicenseNumber(year, sequence),
        IssuedAt = new DateOnly(year, 11, 15),
        CustomerName = "Grupul Exemplu",
        FiscalCodes = "10000001",
        Modules = [LicenseModules.Core, LicenseModules.Finance],
        ValidUntil = new DateOnly(year + 1, 12, 31),
        Limits = new LicenseLimits { Users = 100, PayrollEmployees = 300, PosTerminals = 10 },
        FileName = $"Grupul-Exemplu-LIC-{year}-{sequence:000000}.lic",
    };
}
