---
applyTo: "**/*.cs"
description: "Reguli C# pentru ErpSetup, ErpLicenseGen și bibliotecile partajate"
---

# C# · installer și generator de licențe

Țintă: **.NET 10**, `net10.0-windows`, nullable activ, `TreatWarningsAsErrors`.

## Acces la date

- **Dapper** cu SQL parametrizat sau proceduri stocate. **Fără EF Core sau orice alt ORM** (R2).
- Șirul de conexiune se construiește cu `SqlConnectionStringBuilder`: `IntegratedSecurity = true`, `Encrypt = Mandatory`, `ApplicationName = "ERP-<Instanță>-<Componentă>"`. **Nicio parolă SQL** nicăieri (R5).
- Singura excepție de la SQL parametrizat: DDL-ul installer-ului în `Erp.Setup.Sql` (`CREATE DATABASE`, `CREATE LOGIN … FROM WINDOWS`, `BACKUP`/`RESTORE`, joburi SQL Agent), unde textul se construiește cu `QuoteName()` pentru nume, `SqlLiteral()` pentru căi validate (absolute, fără `'`, `;`, `--`, caractere de control) și `CultureInfo.InvariantCulture` pentru numere. Abatere scrisă de la `SP-06`, valabilă numai în acest proiect.

## Pași de instalare

Fiecare pas e o clasă care implementează `IInstallStep` (`Id` = `"I-07"`, `Title` în română), e **idempotentă** (R10) și raportează progresul prin `IProgress<string>`. `CompensateAsync` se folosește doar la actualizare (revenirea din 9.2). Orchestratorul reia din `instance.json` (`state`, `completedSteps`).

## Procese externe

`Process.Start` cu `ProcessStartInfo.ArgumentList` — fără shell, fără concatenare de text (DISM, `sc.exe`, Hosting Bundle, `iisreset`). Cod de ieșire ≠ 0 → pasul eșuează; 3010 de la DISM = repornire necesară. IIS se configurează prin `Microsoft.Web.Administration.ServerManager`, cu un singur `CommitChanges()` per pas; obiectele existente se actualizează, nu se recreează.

## Criptografie

- Licență: `AesGcm` (cheie 256 de biți, nonce 12 octeți aleator per licență, tag 16 octeți, AAD = antetul fișierului) + `ECDsa` P-256/SHA-256, semnătură `IEEE P1363` de 64 de octeți, peste tot fișierul în afară de semnătură. Se criptează, apoi se semnează.
- Parole: `PasswordHasher<TUser>` din `Microsoft.Extensions.Identity.Core` (format V3). Parola temporară: 20 de caractere din `RandomNumberGenerator`, alfabet fără caractere confundabile, minimum 2 din fiecare categorie.
- Aleatorul: numai `RandomNumberGenerator`, niciodată `Random`.
- Cheia privată ECDSA: doar în `%APPDATA%\ErpLicenseGen\keys\`, protejată DPAPI `CurrentUser`. Niciodată în Git, niciodată în installer sau în ERP (R7).

## Secrete și jurnale

Niciun secret în jurnale sau în mesaje de eroare (R8, `SEC-02`, `NET-18`): nici parola temporară, nici conținutul decriptat al licenței, nici CUI-urile în clar. Parola în clar există doar în memorie, până la închiderea ecranului 6. Mesajele de eroare SQL se afișează fără date din tabele.

## Comparații de text

Coduri tehnice: `StringComparer.Ordinal`. Coduri introduse de utilizatori: `StringComparer.OrdinalIgnoreCase` după normalizare (`NET-21`). Normalizările (CUI, IBAN, coduri ISO) sunt aceleași în C# și în bază.

## WPF și MVVM

MVVM cu `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`). Niciun apel SQL, IIS sau de fișier în code-behind: logica stă în `Erp.Setup.Core`. Operațiile lungi sunt `async` cu `CancellationToken`, ca fereastra să nu se blocheze. ViewModel-urile se testează unitar fără fereastră.

## Teste

xUnit; fiecare test poartă criteriul de acceptare: `[Trait("Criteriu", "08-IA-07")]` (`TST-01`). Integrarea SQL rulează pe Testcontainers (`mcr.microsoft.com/mssql/server:2022-latest`), pe o bază `Latin1_General_100_CS_AS_SC_UTF8` creată pe un server case insensitive (`TST-09`).
