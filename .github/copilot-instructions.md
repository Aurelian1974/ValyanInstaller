# Instrucțiuni GitHub Copilot · ValyanInstaller

Depozitul conține cele **două aplicații de instalare** ale ERP-ului:

1. **`ErpLicenseGen.exe`** — generatorul de licențe, **numai intern** (conține cheia privată de semnare; nu se livrează clientului).
2. **`ErpSetup.exe`** — installer-ul rulat la client (Windows 10 22H2 / 11 / Server 2019–2025): creează baza SQL, site-ul IIS, serviciul Windows și utilizatorul administrator; actualizează, repară, resetează accesul administratorului, dezinstalează, produce pachet de diagnostic.

Răspunde și comentează în **română**. Identificatorii din cod și SQL sunt în **engleză** (`NET-19`).

## Citește specificația înainte de a sugera cod

| Document | Cale |
| --- | --- |
| Specificația de implementare (obligatorie) | `08-instalare-actualizare/INSTALARE_ACTUALIZARE.md` |
| Machetele ecranelor | `08-instalare-actualizare/ecrane/` |
| Fluxul actualizării și al revenirii | `08-instalare-actualizare/flux-actualizare.png` |
| Reguli de cod (`SQL-`, `SP-`, `MIG-`, `NET-`, `SEC-`, `TST-`) | `docs/arhitectura/bune-practici/BUNE_PRACTICI.md` |
| Nucleul (`core.*`, permisiuni, asistent) | `docs/arhitectura/02-module-principale/01-nucleu/NUCLEU.md` |

Specificația are prioritate față de orice convenție implicită. Nu „simplifica” niciun pas: dacă ceva pare imposibil, semnalează-l în loc să inventezi o variantă mai simplă.

## Reguli neschimbabile (R1–R12)

- **R1** WPF **.NET 10** (`net10.0-windows`, `<UseWPF>true</UseWPF>`), **MVVM** cu `CommunityToolkit.Mvvm`, doar controale WPF standard. Publicare self-contained single-file `win-x64` (`PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract=true`, **fără trimming**). Operații lungi `async` + `IProgress<T>`. `ErpSetup.exe`: manifest `requireAdministrator`. Fără MSI, WiX sau PowerShell ca logică principală.
- **R2** **Dapper** + SQL parametrizat sau proceduri stocate. Fără EF Core sau alt ORM, fără Duende, fără OpenIddict, fără componente cu licență plătită.
- **R3** Migrații cu **DbUp** (`MIG-01`–`MIG-07`).
- **R4** Baza e **case sensitive**: `Latin1_General_100_CS_AS_SC_UTF8`. Identificatorii se scriu exact cu majusculele din declarație.
- **R5** SQL **numai cu Windows authentication** (`Integrated Security=true`). Nicio parolă SQL, niciun fișier cu parole SQL.
- **R6** Instalare nouă: exact un Administrator. Actualizarea nu creează și nu modifică utilizatori.
- **R7** Licența: CUI-uri doar cifre separate prin `:`, **AES-256-GCM** + **ECDSA P-256**. Cheia privată doar în generator, niciodată în ERP sau în Git.
- **R8** Niciun secret în jurnale sau mesaje de eroare (`SEC-02`, `NET-18`). Excepția unică: parola temporară a administratorului, afișată o singură dată pe ecran.
- **R9** Installer-ul nu șterge niciodată o bază și nu scrie peste o bază existentă cu date.
- **R10** Orice pas e **idempotent**.
- **R11** Textele pentru utilizator în română.
- **R12** Nimic nu se simplifică tăcut.

## Structura soluției

```
src/Tools/Erp.LicenseGen/        aplicația 1 (WPF), intern
src/Tools/Erp.Setup/             aplicația 2 (WPF): ferestre + ViewModel-uri
src/Setup/Erp.Setup.Core/        IInstallStep, verificări, stare, actualizare, revenire
src/Setup/Erp.Setup.Windows/     IIS, DISM, servicii, firewall, ACL, certificate
src/Setup/Erp.Setup.Sql/         baza, loginuri, runner DbUp, joburi SQL Agent
src/Setup/Erp.Setup.Tests/
src/Shared/Erp.Licensing/        citire/decriptare/verificare licență + LicenseKeys.cs
src/Shared/Erp.Configuration/    appsettings.Installation.json, șir de conexiune
src/Shared/Erp.Security.Passwords/
build/package-kit.ps1            compilează, publică, generează manifestul, semnează
```

Logica stă în `Erp.Setup.Core`, testabilă fără interfață. Ecranele WPF doar colectează valori și afișează progresul. Fiecare pas implementează `IInstallStep` (`Id` = `"I-07"`, `Title` în română, `ExecuteAsync`, `CompensateAsync`).

## Repere concrete

- **Fișierul `.lic`:** `ERPLIC01` (8) · versiune `1` (1) · `KeyId` (1) · nonce (12) · lungime uint32 LE (4) · text criptat (n) · tag GCM (16) · semnătură ECDSA IEEE P1363 (64). AAD = octeții 0…25. Se criptează, apoi se semnează. `fiscalCodes` e un singur șir: `^[1-9][0-9]{1,9}(:[1-9][0-9]{1,9})*$`.
- **CUI:** normalizare la cifre (se elimină `RO`, spațiile), validarea cifrei de control, comparație exactă cu `fiscalCodes.Split(':')`. Firmă nelicențiată → `403` + `CORE_FISCAL_CODE_NOT_LICENSED`; un grup cu o firmă nelicențiată nu se salvează deloc.
- **DDL** (`CREATE DATABASE`, `CREATE LOGIN … FROM WINDOWS`, `BACKUP`/`RESTORE`, joburi SQL Agent): nu acceptă parametri, deci textul se construiește în C# cu `QuoteName()` pentru nume, `SqlLiteral()` pentru căi validate și `CultureInfo.InvariantCulture` pentru numere. Abatere scrisă de la `SP-06`, permisă **numai** în `Erp.Setup.Sql`. Oriunde altundeva: SQL parametrizat.
- **Administrator:** `administrator` (litere mici), `IsSystem=1`, rol `ADMIN` cu `CompanyId = NULL`, `MustChangePassword=1`, `TwoFactorRequired=1`; parolă de 20 de caractere din `RandomNumberGenerator`, hash `PasswordHasher<TUser>` (V3), în clar doar în memorie.
- **Loginuri:** SQL local → `[IIS APPPOOL\ERP-<Instanță>]` + `[NT SERVICE\ErpWorker-<Instanță>]`; SQL la distanță → `[DOMENIU\CALCULATOR$]`. Membri în rolurile `role_*`, **niciodată `db_owner`** (`SP-19`).
- **Pași:** instalare I-01…I-18 (5.4), actualizare U-01…U-10 cu revenire automată (9.2), reluare din `instance.json`.
- **Procese externe:** `Process.Start` cu `ArgumentList`, fără shell. Cod de ieșire ≠ 0 → pasul eșuează; DISM 3010 = repornire necesară.

## Interfața WPF

Culorile machetelor: albastru `#2f6fed` (hover `#2459c9`, fundal `#eef3fd`), bleumarin `#0b1f3a`, text `#1a2433`, secundar `#5b6676`, linii `#e3e7ee`, verde `#1f9d63`, roșu `#d14343`, ambru `#b07a00`; Segoe UI, monospațiat Cascadia Mono. Ecranele 0–6 și variantele de eroare sunt în 5.2.

## Interzis

EF Core sau alt ORM · Duende · OpenIddict · `Microsoft.AspNetCore.Identity.EntityFrameworkCore` · `MapIdentityApi` · biblioteci de controale cu plată (DevExpress, Telerik, Syncfusion) · MSI/WiX · PowerShell ca logică principală · parole SQL · `db_owner` pentru aplicație · `ALTER DATABASE … COLLATE` · ștergerea unei baze sau a backup-urilor · chei private în Git · secrete în jurnale · obiecte în `dbo` (excepții: `master.dbo.`, `msdb.dbo.`).

## Înainte de livrare (T11)

- Cheile se generează cu butonul „Chei” și se lipesc în `LicenseKeys.cs`; cheia privată nu intră în depozit.
- `LicensingPolicy.EnforceFiscalCodeCheckDigit` trece pe `true`: în dezvoltare o cifră de control greșită e doar avertisment, în produsul finalizat blochează generarea (decizia D12). Nu propune trecerea pe `true` înainte de livrare și nu o scoate din cod.
- Licențele de test cu CUI-uri inventate se regenerează; `ErpSetup.exe` se semnează Authenticode.

## Pull request-uri

Implementare pe sarcinile T1–T11 din secțiunea 12, în ordine, **un pull request pe sarcină** (`GIT-01`–`GIT-04`). O sarcină e gata când trece criteriul IA-xx din secțiunea 11; testele poartă criteriul: `[Trait("Criteriu", "08-IA-07")]`.

Acest fișier și `CLAUDE.md` din rădăcină trebuie să rămână echivalente: o regulă se schimbă mai întâi în specificație, apoi în ambele fișiere, în același commit.
