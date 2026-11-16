using Erp.Licensing;
using Erp.Setup.Sql;
using Erp.Setup.Tests.Licensing;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Tests.Sql;

/// <summary>
/// Licența în baza de date (4.1, 4.3, deciziile D13 și D14), pe un SQL Server real, cu baza
/// creată case sensitive pe un server case insensitive (`TST-09`).
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class InstallationLicenseStoreTests : IAsyncLifetime
{
    private readonly SqlServerFixture sql;

    public InstallationLicenseStoreTests(SqlServerFixture sql) => this.sql = sql;

    private InstallationLicenseStore StoreFor(LicenseTestKeys keys) => new(sql.Connections, keys.Ring);

    public async Task InitializeAsync()
    {
        if (!sql.IsAvailable)
        {
            return;
        }

        await sql.RunBootstrapAsync();
        await sql.ExecuteAsync("DELETE FROM core.InstallationLicense;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    [Trait("Criteriu", "08-IA-04")]
    public async Task Baza_de_test_are_collation_ul_cerut()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        Assert.Equal(SqlServerFixture.Collation, await sql.ScalarAsync<string>(
            "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128));"));
        Assert.Equal(1, await sql.ScalarAsync<int>(
            "SELECT CAST(d.is_read_committed_snapshot_on AS int) FROM sys.databases AS d WHERE d.name = DB_NAME();"));
    }

    [SkippableFact]
    [Trait("Criteriu", "08-IA-11")]
    public async Task Bootstrap_ul_e_idempotent_si_creeaza_schemele_si_rolurile()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        var executed = await sql.RunBootstrapAsync();          // a doua rulare, peste prima (R10)

        Assert.Equal(["001_Schemas.sql", "002_Roles.sql", "003_InstallationLicense.sql"], executed);
        Assert.Equal(16, await sql.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.schemas
            WHERE name IN (N'core', N'ids', N'hangfire', N'intg', N'fin', N'inv', N'sales', N'purch',
                           N'wms', N'tms', N'mfg', N'hr', N'pos', N'cons', N'crm', N'rpt');
            """));
        Assert.Equal(16, await sql.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.database_principals WHERE type = 'R' AND name LIKE 'role[_]%';"));
        Assert.Equal(0, await sql.ScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID(N'core.InstallationLicenseFiscalCode', N'U') IS NULL THEN 0 ELSE 1 END;"));
        Assert.Equal(0, await sql.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID('dbo') AND type IN ('U', 'V', 'P');"));
    }

    [SkippableFact]
    [Trait("Criteriu", "08-IA-07")]
    public async Task Licenta_scrisa_se_citeste_cu_toate_cui_urile()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        using var keys = LicenseTestKeys.Create();
        var issued = keys.Issue(LicenseTestContent.Create(fiscalCodes: "10000001:12345678:23456789"));
        var license = issued.Content;

        var result = await StoreFor(keys).SaveAsync(license, issued.File, keyId: 1, "CONTOSO\\andrei.matei", "2027.1.0");

        Assert.True(result.WasWritten);

        var stored = await StoreFor(keys).ReadCurrentAsync();
        Assert.NotNull(stored);
        Assert.Equal(license.LicenseId, stored!.LicenseId);
        Assert.Equal(license.CustomerName, stored.CustomerName);
        Assert.Equal(["10000001", "12345678", "23456789"], stored.FiscalCodes);
        Assert.Equal(license.Modules, stored.Modules);
        Assert.Equal(license.Limits.Users, stored.MaxUsers);
        Assert.Equal(license.ValidUntil, stored.ValidUntil);
        Assert.Equal("CONTOSO\\andrei.matei", stored.LoadedBy);

        // Exact ce face ERP-ul: un SELECT, fără criptografie (4.3, regula 2).
        Assert.Equal(["10000001", "12345678", "23456789"], await StoreFor(keys).ReadLicensedFiscalCodesAsync());
        Assert.True(stored.IsLicensed("12345678"));
        Assert.False(stored.IsLicensed("99999999"));
    }

    [SkippableFact]
    [Trait("Criteriu", "08-IA-09")]
    public async Task Aceeasi_licenta_scrisa_din_nou_nu_dubleaza_nimic()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        using var keys = LicenseTestKeys.Create();
        var issued = keys.Issue(LicenseTestContent.Create());

        var first = await StoreFor(keys).SaveAsync(issued.Content, issued.File, 1, "CONTOSO\\operator", "2027.1.0");
        var second = await StoreFor(keys).SaveAsync(issued.Content, issued.File, 1, "CONTOSO\\operator", "2027.1.0");

        Assert.True(first.WasWritten);
        Assert.False(second.WasWritten);                 // R10: a doua rulare nu scrie nimic
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await StoreFor(keys).CountAsync());
        Assert.Equal(4, await sql.ScalarAsync<int>("SELECT COUNT(*) FROM core.v_LicensedFiscalCode;"));
    }

    [SkippableFact]
    [Trait("Criteriu", "08-IA-07")]
    public async Task O_licenta_noua_inlocuieste_lista_si_pastreaza_istoricul()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        using var keys = LicenseTestKeys.Create();
        var initial = keys.Issue(LicenseTestContent.Create(fiscalCodes: "10000001"));
        await StoreFor(keys).SaveAsync(initial.Content, initial.File, 1, "CONTOSO\\operator", "2027.1.0");

        // „Actualizează licența” (10.5): o firmă nouă în grup.
        var extended = keys.Issue(
            LicenseTestContent.Create(fiscalCodes: "10000001:12345678") with { LicenseId = "LIC-2026-000124" });
        var result = await StoreFor(keys).SaveAsync(extended.Content, extended.File, 1, "CONTOSO\\operator", "2027.1.0");

        Assert.True(result.WasWritten);
        Assert.Equal(2, await StoreFor(keys).CountAsync());                       // istoricul rămâne (4.1)
        Assert.Equal(1, await sql.ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.InstallationLicense WHERE IsCurrent = 1;"));
        Assert.Equal(["10000001", "12345678"], await StoreFor(keys).ReadLicensedFiscalCodesAsync());

        var stored = await StoreFor(keys).ReadCurrentAsync();
        Assert.Equal("LIC-2026-000124", stored!.LicenseId);
    }

    [SkippableFact]
    [Trait("Criteriu", "08-IA-07")]
    public async Task Aplicatia_poate_citi_dar_nu_poate_scrie_tabelele_licentei()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        using var keys = LicenseTestKeys.Create();
        var issued = keys.Issue(LicenseTestContent.Create());
        await StoreFor(keys).SaveAsync(issued.Content, issued.File, 1, "CONTOSO\\operator", "2027.1.0");

        // Un utilizator ca al aplicației: membru în rolurile de modul, fără db_owner (SP-19).
        await sql.ExecuteAsync(
            """
            IF DATABASE_PRINCIPAL_ID(N'erp_web_test') IS NULL
                CREATE USER erp_web_test WITHOUT LOGIN WITH DEFAULT_SCHEMA = core;
            ALTER ROLE role_core ADD MEMBER erp_web_test;
            """);

        // Citirea merge (4.3, regula 2).
        Assert.Equal(4, await sql.ScalarAsync<int>(
            """
            EXECUTE AS USER = 'erp_web_test';
            SELECT COUNT(*) FROM core.v_LicensedFiscalCode;
            REVERT;
            """));

        // Scrierea e refuzată (4.3, regula 8): DENY are prioritate față de GRANT-ul pe schemă.
        foreach (var statement in new[]
                 {
                     "UPDATE core.InstallationLicense SET FiscalCodes = N'99999999';",
                     "DELETE FROM core.InstallationLicense;",
                 })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => sql.ExecuteAsync(
                $"EXECUTE AS USER = 'erp_web_test'; {statement} REVERT;"));

            Assert.Equal(229, ex.Number);                 // „The … permission was denied”
        }

        Assert.Equal(4, await sql.ScalarAsync<int>("SELECT COUNT(*) FROM core.v_LicensedFiscalCode;"));
    }

    [SkippableFact]
    [Trait("Criteriu", "08-IA-11")]
    public async Task Numele_obiectelor_se_scriu_exact_pe_o_baza_case_sensitive()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        // Scris corect: merge.
        Assert.Equal(0, await sql.ScalarAsync<int>("SELECT COUNT(*) FROM core.InstallationLicense;"));

        // Scris cu alte majuscule: eroare, pe baza case sensitive (R4, SQL-22).
        var ex = await Assert.ThrowsAsync<SqlException>(
            () => sql.ScalarAsync<int>("SELECT COUNT(*) FROM core.installationlicense;"));
        Assert.Equal(208, ex.Number);                     // „Invalid object name”
    }

    [SkippableFact]
    [Trait("Criteriu", "08-IA-07")]
    public async Task Baza_refuza_un_cui_care_nu_e_numeric()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        using var keys = LicenseTestKeys.Create();
        var issued = keys.Issue(LicenseTestContent.Create());
        await StoreFor(keys).SaveAsync(issued.Content, issued.File, 1, "CONTOSO\\operator", "2027.1.0");

        var ex = await Assert.ThrowsAsync<SqlException>(() => sql.ExecuteAsync(
            "UPDATE core.InstallationLicense SET FiscalCodes = N'RO12345678';"));

        Assert.Equal(547, ex.Number);                     // încălcare de CHECK constraint
    }

    [SkippableFact]
    [Trait("Criteriu", "08-IA-07")]
    public async Task O_lista_de_cui_uri_nevalida_nu_ajunge_in_baza()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        using var keys = LicenseTestKeys.Create();
        var license = LicenseTestContent.Create() with { FiscalCodes = "RO10000001", ContentSignature = "x" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => StoreFor(keys).SaveAsync(license, [1, 2, 3], 1, "CONTOSO\\operator", "2027.1.0"));

        Assert.Equal(0, await StoreFor(keys).CountAsync());
    }

    [SkippableFact]
    [Trait("Criteriu", "08-IA-07")]
    public async Task Avertizeaza_firmele_active_care_nu_mai_sunt_licentiate()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        // La o instalare nouă, core.Company (creată de migrațiile ERP) încă nu există:
        // lista e goală, fără eroare (10.5).
        using var keys = LicenseTestKeys.Create();
        Assert.Empty(await StoreFor(keys).FindUnlicensedActiveCompaniesAsync(["10000001"]));

        await sql.ExecuteAsync(
            """
            CREATE TABLE core.Company
            (
                Id         int           IDENTITY(1,1) NOT NULL CONSTRAINT PK_Company PRIMARY KEY,
                Name       nvarchar(200) NOT NULL,
                FiscalCode varchar(10)   NOT NULL,
                IsActive   bit           NOT NULL
            );

            INSERT INTO core.Company (Name, FiscalCode, IsActive)
            VALUES (N'Exemplu Unu', '10000001', 1),
                   (N'Exemplu Doi', '12345678', 1),
                   (N'Exemplu Închis', '23456789', 0);
            """);

        // Licența nouă nu mai conține 12345678: firma activă ar trece în doar-citire.
        var unlicensed = await StoreFor(keys).FindUnlicensedActiveCompaniesAsync(["10000001"]);

        var company = Assert.Single(unlicensed);
        Assert.Equal("Exemplu Doi", company.Name);
        Assert.Equal("12345678", company.FiscalCode);

        // Cu lista completă, nicio firmă nu e afectată; firma inactivă nu contează.
        Assert.Empty(await StoreFor(keys).FindUnlicensedActiveCompaniesAsync(["10000001", "12345678"]));

        await sql.ExecuteAsync("DROP TABLE core.Company;");
    }

    [SkippableFact]
    [Trait("Criteriu", "08-IA-07")]
    public async Task Un_sysadmin_care_adauga_un_cui_invalideaza_semnatura()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        using var keys = LicenseTestKeys.Create();
        var issued = keys.Issue(LicenseTestContent.Create(fiscalCodes: "10000001:12345678"));
        var license = issued.Content;
        var store = StoreFor(keys);
        await store.SaveAsync(license, issued.File, 1, "CONTOSO\\operator", "2027.1.0");

        Assert.True((await store.ReadCurrentAsync())!.SignatureValid);
        Assert.Equal(["10000001", "12345678"], await store.ReadVerifiedFiscalCodesAsync());

        // Un cont sysadmin ocolește DENY-ul și adaugă o firmă în lista semnată (4.3, regula 9).
        await sql.ExecuteAsync(
            "UPDATE core.InstallationLicense SET FiscalCodes = N'10000001:12345678:99999999' WHERE IsCurrent = 1;");

        // Vederea arată CUI-ul adăugat …
        Assert.Equal(3, await sql.ScalarAsync<int>("SELECT COUNT(*) FROM core.v_LicensedFiscalCode;"));

        // … dar semnătura nu mai e validă, deci lista se refuză întreagă.
        Assert.False((await store.ReadCurrentAsync())!.SignatureValid);
        var ex = await Assert.ThrowsAsync<LicenseTamperedException>(() => store.ReadVerifiedFiscalCodesAsync());
        Assert.Equal(license.LicenseId, ex.LicenseId);
    }

    [SkippableFact]
    [Trait("Criteriu", "08-IA-07")]
    public async Task Modificarea_limitelor_sau_a_modulelor_invalideaza_semnatura()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        using var keys = LicenseTestKeys.Create();
        var issued = keys.Issue(LicenseTestContent.Create());
        var store = StoreFor(keys);

        foreach (var tampering in new[]
                 {
                     "UPDATE core.InstallationLicense SET MaxUsers = 500 WHERE IsCurrent = 1;",
                     "UPDATE core.InstallationLicense SET Modules = N'Core Finance Sales Purchasing Inventory Integrations Wms' WHERE IsCurrent = 1;",
                     "UPDATE core.InstallationLicense SET ValidUntil = '2099-12-31' WHERE IsCurrent = 1;",
                     "UPDATE core.InstallationLicense SET CustomerName = N'Alt Client' WHERE IsCurrent = 1;",
                 })
        {
            await sql.ExecuteAsync("DELETE FROM core.InstallationLicense;");
            await store.SaveAsync(issued.Content, issued.File, 1, "CONTOSO\\operator", "2027.1.0");
            Assert.True((await store.ReadCurrentAsync())!.SignatureValid);

            await sql.ExecuteAsync(tampering);

            Assert.False((await store.ReadCurrentAsync())!.SignatureValid, tampering);
        }
    }

    [SkippableFact]
    [Trait("Criteriu", "08-IA-07")]
    public async Task O_semnatura_de_la_alta_cheie_nu_e_acceptata()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        using var producer = LicenseTestKeys.Create();
        using var other = LicenseTestKeys.Create();
        var issued = producer.Issue(LicenseTestContent.Create());

        await StoreFor(producer).SaveAsync(issued.Content, issued.File, 1, "CONTOSO\\operator", "2027.1.0");

        // Instalarea verificată cu alt inel de chei: semnătura nu se validează.
        var foreignStore = new InstallationLicenseStore(sql.Connections, other.Ring);
        Assert.False((await foreignStore.ReadCurrentAsync())!.SignatureValid);
    }

    [SkippableFact]
    [Trait("Criteriu", "08-IA-07")]
    public async Task O_licenta_fara_semnatura_detasata_nu_se_instaleaza()
    {
        Skip.IfNot(sql.IsAvailable, sql.UnavailableReason);

        using var keys = LicenseTestKeys.Create();
        var license = LicenseTestContent.Create();           // fără contentSignature (licență veche)

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => StoreFor(keys).SaveAsync(license, [1, 2, 3], 1, "CONTOSO\\operator", "2027.1.0"));

        Assert.Contains("D15", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, await StoreFor(keys).CountAsync());
    }
}
