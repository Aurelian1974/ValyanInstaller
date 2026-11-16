using System.Globalization;
using Erp.Licensing;

namespace Erp.Setup.Tests.Licensing;

/// <summary>Normalizarea și validarea CUI-urilor (3.2, 4.3); criteriul IA-01.</summary>
public sealed class FiscalCodeTests
{
    [Theory]
    [InlineData("RO10000001", "10000001")]
    [InlineData("ro 12345678", "12345678")]
    [InlineData("  RO 1 234 5678  ", "12345678")]
    [InlineData("12345678", "12345678")]
    [Trait("Criteriu", "08-IA-01")]
    public void Normalizeaza_la_partea_numerica(string input, string expected)
    {
        Assert.True(FiscalCode.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("RO")]
    [InlineData("1")]
    [InlineData("01234567")]          // nu poate începe cu 0 (3.3)
    [InlineData("12345678901")]       // peste 10 cifre
    [Trait("Criteriu", "08-IA-01")]
    public void Refuza_formele_imposibile(string input)
        => Assert.False(FiscalCode.TryNormalize(input, out _));

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Verifica_cifra_de_control()
    {
        // Cifra de control se calculează cu cheia ANAF 753217532.
        var valid = FiscalCode.WithCheckDigit("1234567");
        Assert.True(FiscalCode.HasValidCheckDigit(valid));

        // Orice altă cifră finală e refuzată.
        var lastDigit = valid[^1] - '0';
        var wrong = string.Concat(valid[..^1], ((lastDigit + 1) % 10).ToString(CultureInfo.InvariantCulture));
        Assert.False(FiscalCode.HasValidCheckDigit(wrong));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Lista_introdusa_de_operator_se_normalizeaza_si_se_ordoneaza()
    {
        var a = FiscalCode.WithCheckDigit("1000000");
        var b = FiscalCode.WithCheckDigit("2345678");

        var result = FiscalCode.ParseList($"RO{a}\r\n ro {b}", enforceCheckDigit: true);

        Assert.True(result.IsUsable);
        Assert.Equal([a, b], result.Accepted);
        Assert.Equal($"{a}:{b}", result.NormalizedList);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void In_produsul_livrat_un_cui_invalid_blocheaza_generarea()
    {
        var valid = FiscalCode.WithCheckDigit("1234567");
        var invalid = WithWrongCheckDigit(valid);

        var result = FiscalCode.ParseList($"{valid}, {invalid}", enforceCheckDigit: true);

        Assert.False(result.IsUsable);
        Assert.Equal([valid], result.Accepted);
        Assert.Equal([invalid], result.Invalid);
        Assert.Empty(result.WeakCheckDigit);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void In_dezvoltare_un_cui_invalid_e_acceptat_dar_semnalat()
    {
        // Decizia D12: în timpul dezvoltării se pot genera licențe de test cu CUI-uri
        // inventate (exemplele din specificație sunt de acest fel), dar ele rămân marcate.
        var valid = FiscalCode.WithCheckDigit("1234567");
        var invalid = WithWrongCheckDigit(valid);

        var result = FiscalCode.ParseList($"{valid}, {invalid}", enforceCheckDigit: false);

        Assert.True(result.IsUsable);
        Assert.Equal([valid, invalid], result.Accepted);
        Assert.Empty(result.Invalid);
        Assert.Equal([invalid], result.WeakCheckDigit);
        Assert.True(result.HasWeakCheckDigits);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Exemplele_din_specificatie_nu_trec_cifra_de_control()
    {
        // 3.2 și 3.3 folosesc CUI-uri ilustrative; la livrare generatorul le va refuza.
        foreach (var code in new[] { "10000001", "12345678", "23456789", "34567890" })
        {
            Assert.False(FiscalCode.HasValidCheckDigit(code), code);
        }
    }

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Forma_gresita_e_refuzata_in_ambele_regimuri()
    {
        // Cifra de control e singura verificare care se relaxează în dezvoltare;
        // forma (numai cifre, 2–10, fără zero la început) se cere oricum.
        foreach (var enforce in new[] { true, false })
        {
            var result = FiscalCode.ParseList("abc, 01234567, 123456789012", enforceCheckDigit: enforce);

            Assert.False(result.IsUsable);
            Assert.Empty(result.Accepted);
            Assert.Equal(3, result.Invalid.Count);
        }
    }

    private static string WithWrongCheckDigit(string validCode)
        => string.Concat(
            validCode[..^1],
            ((validCode[^1] - '0' + 1) % 10).ToString(CultureInfo.InvariantCulture));

    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Lista_semnaleaza_dublurile()
    {
        var code = FiscalCode.WithCheckDigit("1234567");

        var result = FiscalCode.ParseList($"{code}\n RO{code}", enforceCheckDigit: true);

        Assert.False(result.IsUsable);
        Assert.Equal([code], result.Accepted);
        Assert.Equal([code], result.Duplicates);
    }

    [Theory]
    [InlineData("10000001", true)]
    [InlineData("10000001:12345678", true)]
    [InlineData("10000001:12345678:23456789:34567890", true)]
    [InlineData("10000001:10000001", false)]      // duplicat
    [InlineData("RO10000001", false)]             // litere
    [InlineData("10000001;12345678", false)]      // alt separator
    [InlineData("01000001", false)]               // începe cu 0
    [InlineData("10000001:", false)]
    [InlineData("", false)]
    [Trait("Criteriu", "08-IA-02")]
    public void Regula_listei_din_licenta(string fiscalCodes, bool expected)
        => Assert.Equal(expected, FiscalCode.IsValidList(fiscalCodes));

    [Fact]
    [Trait("Criteriu", "08-IA-07")]
    public void Prefixul_RO_se_adauga_doar_la_afisare()
        => Assert.Equal("RO12345678", FiscalCode.ToDisplay("12345678"));
}
