using Erp.Licensing.Generator;

namespace Erp.Setup.Tests.Licensing;

/// <summary>Numerotarea licențelor și numele fișierului propus (3.2, 3.6).</summary>
public sealed class LicenseNumberTests
{
    [Fact]
    [Trait("Criteriu", "08-IA-01")]
    public void Se_scrie_cu_sase_cifre()
        => Assert.Equal("LIC-2026-000123", new LicenseNumber(2026, 123).ToString());

    [Theory]
    [InlineData("LIC-2026-000123", 2026, 123)]
    [InlineData("  LIC-2027-999999  ", 2027, 999999)]
    public void Se_citeste_din_text(string text, int year, int sequence)
    {
        Assert.True(LicenseNumber.TryParse(text, out var number));
        Assert.Equal(new LicenseNumber(year, sequence), number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("LIC-2026-123")]
    [InlineData("LIC-26-000123")]
    [InlineData("lic-2026-000123")]
    [InlineData("LIC-2026-000000")]
    public void Refuza_formele_greșite(string? text)
        => Assert.False(LicenseNumber.TryParse(text, out _));

    [Fact]
    public void Urmatorul_numar_creste_in_acelasi_an()
        => Assert.Equal(new LicenseNumber(2026, 124), new LicenseNumber(2026, 123).Next());

    [Fact]
    public void La_limita_anului_se_opreste_cu_mesaj()
        => Assert.Throws<InvalidOperationException>(() => new LicenseNumber(2026, 999_999).Next());

    [Theory]
    [InlineData("Grupul Exemplu", "Grupul-Exemplu-LIC-2026-000123.lic")]
    [InlineData("  S.C. Exemplu / Filiala*Nord  ", "S.C.-Exemplu-Filiala-Nord-LIC-2026-000123.lic")]
    [InlineData("///", "Client-LIC-2026-000123.lic")]
    [Trait("Criteriu", "08-IA-01")]
    public void Numele_fisierului_propus(string customer, string expected)
        => Assert.Equal(expected, LicenseFileName.Suggest(customer, new LicenseNumber(2026, 123)));
}
