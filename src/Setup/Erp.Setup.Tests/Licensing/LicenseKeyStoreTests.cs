using System.Security.Cryptography;
using Erp.Licensing;
using Erp.Licensing.Generator;

namespace Erp.Setup.Tests.Licensing;

/// <summary>Depozitul cheii de semnare (3.5).</summary>
public sealed class LicenseKeyStoreTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("erp-keys-").FullName;

    private LicenseKeyStore Store => new(folder, new XorProtector());

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Cheia_salvata_se_incarca_si_semneaza_valid()
    {
        var store = Store;
        var keys = LicenseKeyFactory.Create(1);

        store.Save(keys);

        Assert.Equal([1], store.KeyIds);
        Assert.Equal((byte)1, store.CurrentKeyId);
        Assert.True(store.MatchesPublicKey(1, keys.ToKeyMaterial()));

        // O licență semnată cu cheia încărcată din depozit e acceptată de ERP.
        using var signingKey = store.LoadSigningKey(1);
        var file = new LicenseWriter(1, signingKey, keys.AesKey).Write(LicenseTestContent.Create());
        var result = new LicenseReader(new LicenseKeyRing([keys.ToKeyMaterial()])).Read(file);
        Assert.True(result.IsValid, result.Message);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Cheia_privata_nu_sta_in_clar_pe_disc()
    {
        var store = Store;
        var keys = LicenseKeyFactory.Create(1);

        store.Save(keys);
        var onDisk = File.ReadAllBytes(Path.Combine(folder, "sign-1.bin"));

        Assert.NotEqual(keys.PrivateKeyPkcs8, onDisk);
        Assert.DoesNotContain(
            Convert.ToBase64String(keys.PrivateKeyPkcs8),
            Convert.ToBase64String(onDisk),
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void O_cheie_existenta_nu_se_suprascrie()
    {
        var store = Store;
        store.Save(LicenseKeyFactory.Create(1));

        var ex = Assert.Throws<InvalidOperationException>(() => store.Save(LicenseKeyFactory.Create(1)));
        Assert.Contains("KeyId nou", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Rotirea_cheii_pastreaza_cheia_veche()
    {
        var store = Store;
        store.Save(LicenseKeyFactory.Create(1));
        store.Save(LicenseKeyFactory.Create(2));

        Assert.Equal([1, 2], store.KeyIds);
        Assert.Equal((byte)2, store.CurrentKeyId);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Copia_de_siguranta_se_protejeaza_cu_parola_si_se_poate_reface()
    {
        var store = Store;
        var keys = LicenseKeyFactory.Create(3);
        store.Save(keys);

        var backup = store.ExportBackup(3, "parola-de-siguranta-2026");

        Assert.Throws<CryptographicException>(() =>
        {
            using var wrong = ECDsa.Create();
            wrong.ImportEncryptedPkcs8PrivateKey("altă-parolă-greșită", backup, out _);
        });

        var restored = new LicenseKeyStore(Directory.CreateTempSubdirectory("erp-keys-restore-").FullName, new XorProtector());
        restored.ImportBackup(3, backup, "parola-de-siguranta-2026");

        Assert.True(restored.MatchesPublicKey(3, keys.ToKeyMaterial()));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Parola_prea_scurta_e_refuzata()
    {
        var store = Store;
        store.Save(LicenseKeyFactory.Create(1));

        Assert.Throws<ArgumentException>(() => store.ExportBackup(1, "scurta"));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void O_cheie_inexistenta_da_mesaj_clar()
        => Assert.Throws<FileNotFoundException>(() => Store.LoadSigningKey(9));

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Cheia_privata_nepotrivita_cu_cea_publica_e_detectata()
    {
        var store = Store;
        store.Save(LicenseKeyFactory.Create(1));

        // Cheia publică a altei perechi, ca o lipire greșită în LicenseKeys.cs.
        Assert.False(store.MatchesPublicKey(1, LicenseKeyFactory.Create(1).ToKeyMaterial()));
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    /// <summary>Înlocuitorul DPAPI în teste: aceeași formă, fără dependență de Windows.</summary>
    private sealed class XorProtector : ISecretProtector
    {
        private const byte Mask = 0x5A;

        public byte[] Protect(byte[] data) => [.. data.Select(b => (byte)(b ^ Mask))];

        public byte[] Unprotect(byte[] data) => Protect(data);
    }
}
