using Erp.Licensing;

namespace Erp.Setup.Tests.Licensing;

/// <summary>Textul canonic și semnătura detașată a conținutului (3.3, decizia D15).</summary>
public sealed class LicensePayloadTests
{
    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Textul_canonic_e_exact_cel_din_specificatie()
    {
        var canonical = LicensePayload.Canonical(LicenseTestContent.Create());

        Assert.Equal(
            string.Join(
                '\n',
                "ERPLIC-P1",
                "LIC-2026-000123",
                "ERP",
                "Grupul Exemplu",
                "10000001:12345678:23456789:34567890",
                "Core Finance Sales Purchasing Inventory Integrations",
                "100|300|10",
                "2026-11-15",
                "2027-12-31"),
            canonical);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Nelimitat_se_scrie_cu_minus()
    {
        var canonical = LicensePayload.Canonical(LicenseTestContent.Create() with { ValidUntil = null });

        Assert.EndsWith("\n-", canonical, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Semnatura_detasata_se_verifica_cu_cheia_publica()
    {
        using var keys = LicenseTestKeys.Create();
        var issued = keys.Issue(LicenseTestContent.Create());

        Assert.NotNull(issued.Content.ContentSignature);
        Assert.True(LicensePayload.Verify(issued.Content, keys.Keys.ToKeyMaterial()));
        Assert.True(LicensePayload.Verify(
            LicensePayload.Canonical(issued.Content),
            Convert.FromBase64String(issued.Content.ContentSignature!),
            keys.Ring));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-07")]
    public void Orice_camp_modificat_invalideaza_semnatura()
    {
        using var keys = LicenseTestKeys.Create();
        var issued = keys.Issue(LicenseTestContent.Create(fiscalCodes: "10000001:12345678"));
        var material = keys.Keys.ToKeyMaterial();

        // Exact ce ar încerca un sysadmin în baza de date (4.3, regula 9).
        LicenseContent[] tampered =
        [
            issued.Content with { FiscalCodes = "10000001:12345678:99999999" },
            issued.Content with { Limits = new LicenseLimits { Users = 500, PayrollEmployees = 300, PosTerminals = 10 } },
            issued.Content with { Modules = [.. issued.Content.Modules, LicenseModules.Wms] },
            issued.Content with { ValidUntil = new DateOnly(2099, 12, 31) },
            issued.Content with { CustomerName = "Alt Client" },
            issued.Content with { LicenseId = "LIC-2026-000999" },
        ];

        foreach (var content in tampered)
        {
            Assert.False(LicensePayload.Verify(content, material), content.FiscalCodes);
        }
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void O_semnatura_de_la_alta_cheie_nu_se_verifica()
    {
        using var producer = LicenseTestKeys.Create();
        using var other = LicenseTestKeys.Create();
        var issued = producer.Issue(LicenseTestContent.Create());

        Assert.False(LicensePayload.Verify(issued.Content, other.Keys.ToKeyMaterial()));
        Assert.False(LicensePayload.Verify(
            LicensePayload.Canonical(issued.Content),
            Convert.FromBase64String(issued.Content.ContentSignature!),
            other.Ring));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nu-e-base64")]
    [InlineData("QUJD")]                  // Base64 valid, dar numai 3 octeți
    [Trait("Criteriu", "08-IA-02")]
    public void O_semnatura_malformata_e_refuzata(string? base64)
    {
        Assert.False(LicensePayload.TryDecodeSignature(base64, out var signature));
        Assert.Empty(signature);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Scrierea_recalculeaza_mereu_semnatura()
    {
        using var keys = LicenseTestKeys.Create();
        var content = LicenseTestContent.Create();

        // Chiar dacă i se dă o semnătură lipsă sau greșită, generatorul o calculează din nou:
        // în licență nu poate ajunge o semnătură care nu corespunde conținutului (D15).
        var fromNull = keys.Issue(content with { ContentSignature = null });
        var fromWrong = keys.Issue(content with { ContentSignature = Convert.ToBase64String(new byte[64]) });

        Assert.True(LicensePayload.Verify(fromNull.Content, keys.Keys.ToKeyMaterial()));
        Assert.True(LicensePayload.Verify(fromWrong.Content, keys.Keys.ToKeyMaterial()));

        // ECDSA e nedeterministă: aceleași date, semnate de două ori, dau semnături diferite,
        // ambele valide. Deci semnătura nu se compară niciodată octet cu octet, ci se verifică.
        Assert.NotEqual(fromNull.Content.ContentSignature, fromWrong.Content.ContentSignature);
    }
}
