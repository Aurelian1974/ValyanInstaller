using Erp.Licensing;
using Erp.Licensing.Generator;

namespace Erp.Setup.Tests.Licensing;

/// <summary>Validarea ecranului „Licență nouă” (3.2); criteriul IA-01.</summary>
public sealed class LicenseDraftTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Un_ecran_completat_corect_produce_continutul_licentei()
    {
        var a = FiscalCode.WithCheckDigit("1000000");
        var b = FiscalCode.WithCheckDigit("2345678");
        var draft = Draft($"RO{a}\r\nro {b}");

        var validation = draft.Validate(Today, enforceCheckDigit: true);

        Assert.True(validation.CanGenerate);
        Assert.Empty(validation.Warnings);
        var content = validation.Content!;
        Assert.Equal("LIC-2026-000123", content.LicenseId);
        Assert.Equal("ERP", content.Product);
        Assert.Equal("Grupul Exemplu", content.CustomerName);
        Assert.Equal($"{a}:{b}", content.FiscalCodes);
        Assert.Equal(Today, content.IssuedAt);
        Assert.Equal(new DateOnly(2027, 12, 31), content.ValidUntil);
        Assert.Equal("Grup de 2 firme", content.KindLabel);
        Assert.Equal("2 CUI-uri valide", validation.FiscalCodesSummary);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Nucleul_e_mereu_licentiat_si_ordinea_modulelor_e_cea_din_3_2()
    {
        var draft = Draft();
        draft.Modules.Clear();
        draft.Modules.Add(LicenseModules.Reporting);
        draft.Modules.Add(LicenseModules.Finance);

        var content = draft.Validate(Today, enforceCheckDigit: false).Content!;

        Assert.Equal([LicenseModules.Core, LicenseModules.Finance, LicenseModules.Reporting], content.Modules);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Denumirea_clientului_e_obligatorie()
    {
        var draft = Draft();
        draft.CustomerName = "  ";

        var validation = draft.Validate(Today, enforceCheckDigit: false);

        Assert.False(validation.CanGenerate);
        Assert.Contains("Scrieți denumirea clientului.", validation.ErrorsFor(LicenseDraftField.CustomerName));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Fara_cui_nu_se_poate_genera()
    {
        var draft = Draft(string.Empty);

        var validation = draft.Validate(Today, enforceCheckDigit: false);

        Assert.False(validation.CanGenerate);
        Assert.Contains("Scrieți cel puțin un CUI.", validation.ErrorsFor(LicenseDraftField.FiscalCodes));
        Assert.Equal("Niciun CUI valid", validation.FiscalCodesSummary);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Un_cui_invalid_blocheaza_generarea_la_livrare_si_e_avertisment_in_dezvoltare()
    {
        var draft = Draft("10000001");            // exemplul din specificație: cifră de control greșită

        var atRelease = draft.Validate(Today, enforceCheckDigit: true);
        Assert.False(atRelease.CanGenerate);
        Assert.Contains("CUI invalid: 10000001.", atRelease.ErrorsFor(LicenseDraftField.FiscalCodes));

        var inDevelopment = draft.Validate(Today, enforceCheckDigit: false);
        Assert.True(inDevelopment.CanGenerate);
        var warning = Assert.Single(inDevelopment.Warnings);
        Assert.Contains("10000001", warning.Text, StringComparison.Ordinal);
        Assert.Equal(LicenseDraftField.FiscalCodes, warning.Field);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Cui_urile_repetate_sunt_semnalate()
    {
        var code = FiscalCode.WithCheckDigit("1234567");
        var draft = Draft($"{code}, RO{code}");

        var validation = draft.Validate(Today, enforceCheckDigit: true);

        Assert.False(validation.CanGenerate);
        Assert.Contains($"CUI-uri repetate: {code}.", validation.ErrorsFor(LicenseDraftField.FiscalCodes));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Nelimitat_inseamna_fara_data_de_expirare()
    {
        var draft = Draft();
        draft.IsUnlimited = true;
        draft.ValidUntil = new DateOnly(2020, 1, 1);        // ignorată când „Nelimitat” e bifat

        var content = draft.Validate(Today, enforceCheckDigit: false).Content!;

        Assert.Null(content.ValidUntil);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void O_data_de_expirare_in_trecut_e_refuzata()
    {
        var draft = Draft();
        draft.ValidUntil = Today.AddDays(-1);

        var validation = draft.Validate(Today, enforceCheckDigit: false);

        Assert.False(validation.CanGenerate);
        Assert.Contains("Data nu poate fi în trecut; azi e 16.11.2026.", validation.ErrorsFor(LicenseDraftField.ValidUntil));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Valoarea_implicita_e_un_an_de_azi()
        => Assert.Equal(new DateOnly(2027, 11, 16), LicenseDraft.DefaultValidUntil(Today));

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Limitele_se_verifica()
    {
        var draft = Draft();
        draft.Users = 0;
        draft.PayrollEmployees = -1;
        draft.PosTerminals = -5;

        var validation = draft.Validate(Today, enforceCheckDigit: false);

        Assert.False(validation.CanGenerate);
        Assert.Contains("Numărul de utilizatori e cel puțin 1.", validation.ErrorsFor(LicenseDraftField.Users));
        Assert.Single(validation.ErrorsFor(LicenseDraftField.PayrollEmployees));
        Assert.Single(validation.ErrorsFor(LicenseDraftField.PosTerminals));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Licenta_generata_din_ecran_se_citeste_inapoi()
    {
        using var keys = LicenseTestKeys.Create();
        var content = Draft(FiscalCode.WithCheckDigit("1234567"))
            .Validate(Today, enforceCheckDigit: true)
            .Content!;

        var file = keys.Writer.Write(content);
        var result = keys.CreateReader(Today).Read(file);

        Assert.True(result.IsValid, result.Message);
        Assert.Equal(content, result.Value);
    }

    private static LicenseDraft Draft(string? fiscalCodes = null) => new()
    {
        CustomerName = "Grupul Exemplu",
        FiscalCodesText = fiscalCodes ?? FiscalCode.WithCheckDigit("1234567"),
        ValidUntil = new DateOnly(2027, 12, 31),
        Number = new LicenseNumber(2026, 123),
    };
}
