using System.Security.Cryptography;
using Erp.Licensing;
using Erp.Licensing.Generator;

namespace Erp.Setup.Tests.Licensing;

/// <summary>
/// Chei de test, generate în proces. Produsul își ia cheile din <c>LicenseKeys.cs</c>,
/// unde le lipește butonul „Chei”; în teste nu există niciun material criptografic fix (3.5).
/// </summary>
internal sealed class LicenseTestKeys : IDisposable
{
    private LicenseTestKeys(GeneratedLicenseKeys keys)
    {
        Keys = keys;
        Ecdsa = ECDsa.Create();
        Ecdsa.ImportPkcs8PrivateKey(keys.PrivateKeyPkcs8, out _);
        Ring = new LicenseKeyRing([keys.ToKeyMaterial()]);
        Writer = new LicenseWriter(keys.KeyId, Ecdsa, keys.AesKey);
    }

    public GeneratedLicenseKeys Keys { get; }

    public ECDsa Ecdsa { get; }

    public ILicenseKeyRing Ring { get; }

    public LicenseWriter Writer { get; }

    public static LicenseTestKeys Create(byte keyId = 1)
        => new(LicenseKeyFactory.Create(keyId));

    /// <summary>Un cititor cu o dată „de azi” fixă, pentru verificarea expirării (4.2).</summary>
    public LicenseReader CreateReader(DateOnly today)
        => new(Ring, new FixedClock(today));

    public LicenseReader CreateReader() => CreateReader(new DateOnly(2026, 11, 20));

    public void Dispose() => Ecdsa.Dispose();

    private sealed class FixedClock(DateOnly today) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
            => new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

/// <summary>Conținut de licență pentru teste, conform exemplului din 3.3.</summary>
internal static class LicenseTestContent
{
    public static LicenseContent Create(
        string? fiscalCodes = null,
        DateOnly? validUntil = null,
        string product = "ERP")
        => new()
        {
            LicenseId = "LIC-2026-000123",
            Product = product,
            CustomerName = "Grupul Exemplu",
            FiscalCodes = fiscalCodes ?? "10000001:12345678:23456789:34567890",
            Modules =
            [
                LicenseModules.Core,
                LicenseModules.Finance,
                LicenseModules.Sales,
                LicenseModules.Purchasing,
                LicenseModules.Inventory,
                LicenseModules.Integrations,
            ],
            Limits = new LicenseLimits { Users = 100, PayrollEmployees = 300, PosTerminals = 10 },
            IssuedAt = new DateOnly(2026, 11, 15),
            ValidUntil = validUntil ?? new DateOnly(2027, 12, 31),
        };
}
