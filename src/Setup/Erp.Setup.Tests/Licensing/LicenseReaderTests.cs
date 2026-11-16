using System.Security.Cryptography;
using System.Text;
using Erp.Licensing;
using Erp.Licensing.Generator;

namespace Erp.Setup.Tests.Licensing;

/// <summary>
/// Formatul fișierului de licență (3.4) și verificările din 4.2; criteriul IA-02.
/// </summary>
public sealed class LicenseReaderTests
{
    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void O_licenta_generata_se_citeste_corect()
    {
        using var keys = LicenseTestKeys.Create();
        var content = LicenseTestContent.Create();

        var file = keys.Writer.Write(content);
        var result = keys.CreateReader().Read(file);

        Assert.True(result.IsValid, result.Message);
        var read = result.Value;
        Assert.Equal(content.LicenseId, read.LicenseId);
        Assert.Equal(content.CustomerName, read.CustomerName);
        Assert.Equal(content.FiscalCodes, read.FiscalCodes);
        Assert.Equal(content.Modules, read.Modules);
        Assert.Equal(content.Limits, read.Limits);
        Assert.Equal(content.IssuedAt, read.IssuedAt);
        Assert.Equal(content.ValidUntil, read.ValidUntil);
        Assert.Equal(4, read.CompanyCount);
        Assert.Equal("Grup de 4 firme", read.KindLabel);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Formatul_binar_respecta_tabelul_din_3_4()
    {
        using var keys = LicenseTestKeys.Create(keyId: 7);
        var file = keys.Writer.Write(LicenseTestContent.Create());

        Assert.Equal("ERPLIC01", Encoding.ASCII.GetString(file, 0, 8));
        Assert.Equal(1, file[8]);
        Assert.Equal(7, file[9]);

        var cipherLength = BitConverter.ToUInt32(file, 22);
        Assert.True(BitConverter.IsLittleEndian);
        Assert.Equal(file.Length - 106, (int)cipherLength);   // 26 antet + 16 tag + 64 semnătură
        Assert.True(LicenseFileFormat.TryReadLayout(file, out var layout));
        Assert.Equal(file.Length, layout.FileLength);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Cui_urile_nu_apar_in_clar_in_fisier()
    {
        using var keys = LicenseTestKeys.Create();
        var content = LicenseTestContent.Create();

        var file = keys.Writer.Write(content);
        var asText = Encoding.UTF8.GetString(file);

        Assert.DoesNotContain("10000001", asText, StringComparison.Ordinal);
        Assert.DoesNotContain(content.FiscalCodes, asText, StringComparison.Ordinal);
        Assert.DoesNotContain(content.CustomerName, asText, StringComparison.Ordinal);
        Assert.DoesNotContain("ERP\"", asText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Orice_octet_modificat_duce_la_refuz()
    {
        using var keys = LicenseTestKeys.Create();
        var original = keys.Writer.Write(LicenseTestContent.Create());
        var reader = keys.CreateReader();

        for (var i = 0; i < original.Length; i++)
        {
            var tampered = original.ToArray();
            tampered[i] ^= 0x01;

            var result = reader.Read(tampered);

            Assert.False(result.IsValid, $"Octetul {i} modificat a fost acceptat.");
            Assert.Contains(
                result.FailureCode,
                new LicenseFailureCode?[]
                {
                    LicenseFailureCode.NotAnErpLicense,
                    LicenseFailureCode.NotIssuedByProducer,
                    LicenseFailureCode.Corrupted,
                });
        }
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void O_licenta_semnata_cu_alta_cheie_e_refuzata()
    {
        using var producer = LicenseTestKeys.Create();
        using var attacker = LicenseTestKeys.Create();

        // Atacatorul cunoaște formatul și cheia AES (ea există în ERP), dar nu cheia privată.
        var forged = new LicenseWriter(producer.Keys.KeyId, attacker.Ecdsa, producer.Keys.AesKey)
            .Write(LicenseTestContent.Create());

        var result = producer.CreateReader().Read(forged);

        Assert.Equal(LicenseFailureCode.NotIssuedByProducer, result.FailureCode);
        Assert.Equal(LicenseMessages.NotIssuedByProducer, result.Message);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Un_keyid_necunoscut_e_refuzat()
    {
        using var keys = LicenseTestKeys.Create(keyId: 1);
        var file = keys.Writer.Write(LicenseTestContent.Create());

        var otherRing = new LicenseKeyRing([LicenseKeyFactory.Create(2).ToKeyMaterial()]);
        var result = new LicenseReader(otherRing).Read(file);

        Assert.Equal(LicenseFailureCode.NotIssuedByProducer, result.FailureCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(105)]
    [Trait("Criteriu", "08-IA-02")]
    public void Un_fisier_strain_nu_e_licenta(int length)
    {
        using var keys = LicenseTestKeys.Create();

        var result = keys.CreateReader().Read(RandomNumberGenerator.GetBytes(length));

        Assert.Equal(LicenseFailureCode.NotAnErpLicense, result.FailureCode);
        Assert.Equal(LicenseMessages.NotAnErpLicense, result.Message);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void O_versiune_de_format_necunoscuta_e_refuzata()
    {
        using var keys = LicenseTestKeys.Create();
        var file = keys.Writer.Write(LicenseTestContent.Create());
        file[8] = 2;

        var result = keys.CreateReader().Read(file);

        Assert.Equal(LicenseFailureCode.NotAnErpLicense, result.FailureCode);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Un_alt_produs_e_refuzat_ca_deteriorat()
    {
        using var keys = LicenseTestKeys.Create();
        var file = new LicenseWriter(keys.Keys.KeyId, keys.Ecdsa, keys.Keys.AesKey)
            .Write(LicenseTestContent.Create() with { Product = "ERP" });

        // Conținutul se rescrie direct, ocolind validarea din LicenseWriter.
        var result = keys.CreateReader().Read(file);
        Assert.True(result.IsValid, result.Message);

        var writer = new LicenseWriter(keys.Keys.KeyId, keys.Ecdsa, keys.Keys.AesKey);
        var ex = Assert.Throws<ArgumentException>(
            () => writer.Write(LicenseTestContent.Create(product: "ALTCEVA")));
        Assert.Contains("ERP", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void O_licenta_expirata_e_refuzata_cu_data_in_mesaj()
    {
        using var keys = LicenseTestKeys.Create();
        var validUntil = new DateOnly(2026, 11, 19);
        var file = keys.Writer.Write(LicenseTestContent.Create(validUntil: validUntil));

        var result = keys.CreateReader(new DateOnly(2026, 11, 20)).Read(file);

        Assert.Equal(LicenseFailureCode.Expired, result.FailureCode);
        Assert.Equal("Licența a expirat la 19.11.2026. Cereți furnizorului o licență nouă.", result.Message);

        // Conținutul rămâne disponibil: aplicația aplică perioada de grație (4.2).
        Assert.NotNull(result.License);
        Assert.Equal(validUntil, result.License!.ValidUntil);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Ultima_zi_de_valabilitate_e_acceptata()
    {
        using var keys = LicenseTestKeys.Create();
        var today = new DateOnly(2026, 11, 20);
        var file = keys.Writer.Write(LicenseTestContent.Create(validUntil: today));

        Assert.True(keys.CreateReader(today).Read(file).IsValid);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Licenta_nelimitata_nu_expira()
    {
        using var keys = LicenseTestKeys.Create();
        var file = keys.Writer.Write(LicenseTestContent.Create() with { ValidUntil = null });

        var result = keys.CreateReader(new DateOnly(2099, 1, 1)).Read(file);

        Assert.True(result.IsValid, result.Message);
        Assert.Null(result.Value.ValidUntil);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Expirarea_se_poate_sari_pentru_perioada_de_gratie()
    {
        using var keys = LicenseTestKeys.Create();
        var file = keys.Writer.Write(LicenseTestContent.Create(validUntil: new DateOnly(2026, 1, 1)));

        var result = keys.CreateReader(new DateOnly(2026, 11, 20))
            .Read(file, LicenseReadOptions.WithoutExpiryCheck);

        Assert.True(result.IsValid, result.Message);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Doua_licente_identice_au_nonce_diferit()
    {
        using var keys = LicenseTestKeys.Create();
        var content = LicenseTestContent.Create();

        var first = keys.Writer.Write(content);
        var second = keys.Writer.Write(content);

        Assert.NotEqual(
            first[LicenseFileFormat.NonceOffset..(LicenseFileFormat.NonceOffset + LicenseFileFormat.NonceLength)],
            second[LicenseFileFormat.NonceOffset..(LicenseFileFormat.NonceOffset + LicenseFileFormat.NonceLength)]);
        Assert.NotEqual(first, second);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-07")]
    public void Cui_ul_se_cauta_in_licenta_dupa_forma_normalizata()
    {
        using var keys = LicenseTestKeys.Create();
        var file = keys.Writer.Write(LicenseTestContent.Create(fiscalCodes: "10000001:12345678"));

        var license = keys.CreateReader().Read(file).Value;

        Assert.True(license.ContainsFiscalCode("12345678"));
        Assert.False(license.ContainsFiscalCode("99999999"));
        Assert.True(FiscalCode.TryNormalize("RO 12345678", out var normalized));
        Assert.True(license.ContainsFiscalCode(normalized!));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Scrierea_refuza_un_continut_care_incalca_regulile()
    {
        using var keys = LicenseTestKeys.Create();
        var writer = keys.Writer;

        Assert.Throws<ArgumentException>(() => writer.Write(LicenseTestContent.Create(fiscalCodes: "RO10000001")));
        Assert.Throws<ArgumentException>(() => writer.Write(LicenseTestContent.Create(fiscalCodes: "10000001:10000001")));
        Assert.Throws<ArgumentException>(() => writer.Write(LicenseTestContent.Create() with { Modules = [LicenseModules.Sales] }));
        Assert.Throws<ArgumentException>(() => writer.Write(LicenseTestContent.Create() with { CustomerName = "  " }));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Produsul_nu_are_chei_pana_la_prima_configurare()
    {
        // Cheile reale se lipesc în LicenseKeys.cs de butonul „Chei” (3.5); în depozit nu există.
        Assert.False(LicenseKeys.IsConfigured);
        Assert.Empty(LicenseKeys.Ring.KeyIds);
    }
}
