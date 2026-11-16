# ValyanInstaller · instrucțiuni pentru Claude Code

Depozit al celor **două aplicații de instalare** ale ERP-ului:

1. **`ErpLicenseGen.exe`** — generatorul de licențe, folosit **numai intern** (conține cheia privată de semnare; nu se livrează niciodată clientului).
2. **`ErpSetup.exe`** — installer-ul rulat la client (Windows 10 22H2 / 11 / Server 2019–2025): creează baza SQL, site-ul IIS, serviciul Windows și utilizatorul administrator, apoi actualizează, repară, resetează accesul, dezinstalează, produce pachet de diagnostic.

## Specificația — sursa unică de adevăr

| Document | Cale |
| --- | --- |
| Specificația de implementare (obligatorie, citită integral înainte de a scrie cod) | `08-instalare-actualizare/INSTALARE_ACTUALIZARE.md` |
| Machetele ecranelor (HTML + PNG, de validat) | `08-instalare-actualizare/ecrane/` |
| Fluxul actualizării și al revenirii automate | `08-instalare-actualizare/flux-actualizare.png` |
| Reguli de cod (`SQL-`, `SP-`, `MIG-`, `NET-`, `SEC-`, `TST-`) | `../bune-practici/BUNE_PRACTICI.md` — în monorepo: `docs/arhitectura/bune-practici/BUNE_PRACTICI.md` |
| Nucleul (tabele `core.*`, permisiuni, asistent, ecrane 01/09/09b/10) | `docs/arhitectura/02-module-principale/01-nucleu/NUCLEU.md` |

Specificația are prioritate față de orice obișnuință a modelului. Când specificația și codul existent diferă, câștigă specificația; dacă un pas pare imposibil sau contradictoriu, **oprește-te și întreabă** — nu „simplifica” tăcut (R12).

## Reguli neschimbabile (R1–R12 din secțiunea 0)

| # | Regulă |
| --- | --- |
| R1 | Ambele aplicații sunt **WPF .NET 10** (`net10.0-windows`, `<UseWPF>true</UseWPF>`), **MVVM** cu `CommunityToolkit.Mvvm`, doar controale WPF standard. Publicare self-contained single-file `win-x64` (`PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract=true`, **fără trimming**). Operații lungi `async` + `IProgress<T>`. `ErpSetup.exe`: manifest `requireAdministrator`. **Fără** MSI, WiX sau PowerShell ca logică principală. |
| R2 | Date: **Dapper + SQL parametrizat sau proceduri stocate**. Fără EF Core sau orice alt ORM. Fără Duende, fără OpenIddict, fără componente cu licență plătită (DevExpress, Telerik, Syncfusion…). |
| R3 | Migrații: **DbUp** cu scripturi `.sql` (`MIG-01`–`MIG-07`). |
| R4 | Baza se creează cu collation **`Latin1_General_100_CS_AS_SC_UTF8`** (case sensitive). Orice identificator se scrie exact cu majusculele din declarație. |
| R5 | SQL **numai cu Windows authentication** (`Integrated Security=true`). Nu există parole SQL, nici fișiere cu parole SQL. |
| R6 | Instalare nouă: **exact un** utilizator Administrator. Actualizarea nu creează și nu modifică niciun utilizator. |
| R7 | Licența: CUI-uri doar cifre, separate prin `:`, fișier **AES-256-GCM** + semnătură **ECDSA P-256**. Cheia privată doar în generator, niciodată în ERP sau în Git. |
| R8 | Niciun secret în jurnale sau mesaje de eroare (`SEC-02`, `NET-18`). Singura excepție: parola temporară a administratorului, afișată o singură dată pe ecran. |
| R9 | Installer-ul **nu șterge niciodată** o bază de date și nu scrie peste o bază existentă cu date. |
| R10 | Orice pas e **idempotent**: a doua rulare nu dublează și nu strică nimic. |
| R11 | Textele pentru utilizator în **română**; identificatorii din cod și SQL în **engleză** (`NET-19`). |
| R12 | Nimic nu se simplifică tăcut. |

## Structura soluției (secțiunea 6 — respectă-o exact)

```
src/
├── Tools/Erp.LicenseGen/        aplicația 1 (WPF), doar intern
├── Tools/Erp.Setup/             aplicația 2 (WPF): ferestre + ViewModel-uri
├── Setup/Erp.Setup.Core/        IInstallStep, verificările, starea, actualizarea, revenirea
├── Setup/Erp.Setup.Windows/     IIS (Microsoft.Web.Administration), DISM, servicii, firewall, ACL, certificate
├── Setup/Erp.Setup.Sql/         baza, loginuri, runner DbUp, joburi SQL Agent
├── Setup/Erp.Setup.Tests/
├── Shared/Erp.Licensing/        citire/decriptare/verificare licență + LicenseKeys.cs
├── Shared/Erp.Configuration/    appsettings.Installation.json, șirul de conexiune
└── Shared/Erp.Security.Passwords/ PasswordHasher compatibil Identity V3
build/package-kit.ps1            compilează, publică, copiază scripturile, generează manifestul, semnează
```

Toată logica în `Erp.Setup.Core` (testabilă fără interfață); ecranele WPF doar colectează valori și afișează progresul. ViewModel-urile se testează unitar fără fereastră. Fiecare pas de instalare implementează:

```csharp
public interface IInstallStep
{
    string Id { get; }     // "I-07"
    string Title { get; }  // text în română, afișat pe ecranul 5
    Task<StepResult> ExecuteAsync(SetupContext context, IProgress<string> progress, CancellationToken ct);
    Task CompensateAsync(SetupContext context, CancellationToken ct);   // doar la actualizare (9.2)
}
```

## Ordinea de lucru

Implementează **pe sarcinile T1–T11 din secțiunea 12, în ordine, câte un pull request pe sarcină** (`GIT-01`–`GIT-04`). Nu amesteca sarcini. O sarcină e gata doar când trece criteriul ei de acceptare IA-xx din secțiunea 11; fiecare test poartă criteriul: `[Trait("Criteriu", "08-IA-07")]`.

Branch de dezvoltare: `claude/dazzling-pasteur-i7niyd`. Nu creezi pull request decât la cerere explicită.

## Repere pe care le greșesc modelele

- **Licența (3.3, 3.4):** JSON UTF-8 cu `fiscalCodes` ca **un singur șir** `"10000001:12345678"`, regula `^[1-9][0-9]{1,9}(:[1-9][0-9]{1,9})*$`, fără duplicate. Fișierul `.lic`: marcaj `ERPLIC01` (8) · versiune `1` (1) · `KeyId` (1) · nonce (12) · lungime uint32 LE (4) · text criptat (n) · tag GCM (16) · semnătură ECDSA IEEE P1363 (64). AAD = octeții 0…25. Se **criptează, apoi se semnează**. Verificarea respectă ordinea din 4.2 și mesajele exacte de acolo.
- **CUI:** normalizare la partea numerică (se elimină `RO`, spațiile, orice non-cifră), validare cifră de control, comparație exactă. La firmă/grup nelicențiat, ERP-ul refuză cu `403` + `CORE_FISCAL_CODE_NOT_LICENSED` + mesajul din 4.3; grupul nu se salvează parțial (IA-07).
- **Licența în bază (D13, D14):** installer-ul scrie CUI-urile licențiate în `core.InstallationLicense` și `core.InstallationLicenseFiscalCode`; ERP-ul le citește din `core.v_LicensedFiscalCode` și **nu decriptează licența**. Tabelele sunt doar-citire pentru aplicație (`DENY` pe `INSERT`/`UPDATE`/`DELETE` pentru rolurile de modul). O licență nouă intră numai prin butonul „Actualizează licența” din `ErpSetup.exe` (10.5); ecranul de încărcare din aplicație nu mai există.
- **DDL:** nu acceptă parametri — text construit în C# cu `QuoteName()` pentru nume, `SqlLiteral()` pentru căi validate, `CultureInfo.InvariantCulture` pentru numere. Aceasta e abaterea scrisă de la `SP-06` și e permisă **numai** în `Erp.Setup.Sql` (secțiunea 14).
- **Case sensitive:** `core.[User]` (cuvânt rezervat, mereu în paranteze), vederi de sistem cu litere mici (`sys.databases`), `JournalToSqlTable("core", "SchemaVersion")` exact așa. În `#t`, coloanele text cu `COLLATE DATABASE_DEFAULT` (`SQL-23`).
- **Administrator (5.9):** `administrator` (litere mici), `IsSystem=1`, rol `ADMIN` cu `CompanyId = NULL`, `MustChangePassword=1`, `TwoFactorRequired=1`. Parolă de 20 de caractere din `RandomNumberGenerator`, alfabet fără caractere confundabile, minimum 2 din fiecare categorie; hash `PasswordHasher<TUser>` (V3). Parola în clar: doar în memorie, până la închiderea ecranului 6. Dacă există deja un administrator activ → pasul I-10 e „sărit”, fără parolă pe ecran.
- **Loginuri (5.7):** SQL local → `[IIS APPPOOL\ERP-<Instanță>]` și `[NT SERVICE\ErpWorker-<Instanță>]`; SQL la distanță în domeniu → `[DOMENIU\CALCULATOR$]`. Utilizatorii din bază sunt membri în rolurile `role_*` de modul, **niciodată `db_owner`** (`SP-19`). Pool-ul și serviciul se creează (oprite) la I-07, pentru ca `CREATE LOGIN` să poată referi conturile virtuale.
- **Ordinea pașilor:** I-01 … I-18 din 5.4, cu reluare din `instance.json` (`state = Installing`, `completedSteps`); baza existentă se acceptă la reluare doar dacă proprietatea extinsă `ErpInstallId` coincide cu `installId`.
- **Actualizarea:** U-01 … U-10 din 9.2, cu **revenire automată** la orice eșec din U-03…U-09 și restaurarea bazei dacă U-06 a început. Migrațiile merg doar înainte (`MIG-03`). Protecția versiune program ↔ versiune bază: `AssemblyMetadata("ErpLastMigration", …)` vs. `core.SchemaVersion` (9.4).
- **Procese externe:** `Process.Start` cu `ArgumentList`, fără shell, fără concatenare de text (DISM, `sc.exe`, Hosting Bundle). Cod de ieșire ≠ 0 → pasul eșuează; 3010 la DISM = repornire necesară.
- **Health-check (5.12):** `GET /health/ready` la 5 s, maximum 180 s, conectare la `127.0.0.1` dar cu SNI și antet `Host` egale cu `hostName` (`SocketsHttpHandler.ConnectCallback`). Contractul JSON de răspuns e fix.

## Baza de date de test

Testele de integrare (`TST-09`, IA-04, IA-07, IA-11) rulează pe un SQL Server real, pe o bază creată cu `Latin1_General_100_CS_AS_SC_UTF8` pe un server case **in**sensitive, ca greșelile de majuscule să apară în CI. Serverul se ia din `ERP_TEST_SQL`, altfel din `127.0.0.1,11433`; fără server, testele acelea se sar (`Skip.IfNot`), nu trec tăcut. Autentificarea SQL se folosește **numai** în teste, pentru că pe Linux nu există Windows authentication; produsul respectă R5.

```bash
docker run -d --name erp-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Erp-Test-2026!parola' -e MSSQL_COLLATION=SQL_Latin1_General_CP1_CI_AS -p 11433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

## Interfața WPF

Preia aspectul machetelor din `08-instalare-actualizare/ecrane/`: albastru `#2f6fed` (hover `#2459c9`, fundal `#eef3fd`), bleumarin `#0b1f3a` pentru coloana pașilor, text `#1a2433`, secundar `#5b6676`, linii `#e3e7ee`, verde `#1f9d63`, roșu `#d14343`, ambru `#b07a00`; font Segoe UI, monospațiat Cascadia Mono. Ecranele 0–6 și variantele de eroare (02b, 05b, 07b) sunt descrise în 5.2; machetele sunt **de validat**, nu bătute în cuie — dacă le schimbi, actualizează și tabelul din 5.2.

## Interzis

Entity Framework Core sau alt ORM · Duende · OpenIddict · `Microsoft.AspNetCore.Identity.EntityFrameworkCore` · `MapIdentityApi` · biblioteci de controale cu plată · MSI/WiX · PowerShell ca logică principală · parole SQL · `db_owner` pentru aplicație · `ALTER DATABASE … COLLATE` · ștergerea unei baze sau a backup-urilor · chei private în Git · secrete în jurnale · obiecte în `dbo` (excepții: `master.dbo.`, `msdb.dbo.`).

## Înainte de livrare (T11, înaintea primului kit dat unui client)

Lista de pași care transformă starea de dezvoltare în produs livrabil. Niciunul nu se face mai devreme, dar niciunul nu se uită:

| # | Pas | Unde |
| --- | --- | --- |
| L1 | Generează cheile cu butonul „Chei” și lipește textul în `LicenseKeys.cs`; cheia privată rămâne doar pe calculatorul care emite licențe (3.5, R7) | `src/Shared/Erp.Licensing/LicenseKeys.cs` |
| L2 | `LicensingPolicy.EnforceFiscalCodeCheckDigit` → `true`: un CUI cu cifră de control greșită blochează generarea (decizia D12, 3.2) | `src/Shared/Erp.Licensing/LicensingPolicy.cs` |
| L3 | Regenerează licențele de test care conțineau CUI-uri inventate (exemplele din specificație nu trec cifra de control) | — |
| L4 | Semnează `ErpSetup.exe` Authenticode și verifică manifestul kitului (C-05) | `build/package-kit.ps1` |

## Când modifici regulile

`CLAUDE.md` și `.github/copilot-instructions.md` trebuie să rămână echivalente. Dacă o regulă se schimbă, schimbă-o în **specificație** (sursa de adevăr) și apoi în ambele fișiere de instrucțiuni, în același commit.
