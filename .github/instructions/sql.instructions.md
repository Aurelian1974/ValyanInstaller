---
applyTo: "**/*.sql"
description: "Reguli SQL pentru baza ERP, case sensitive, migrații DbUp"
---

# SQL · bază case sensitive

Baza clientului are collation **`Latin1_General_100_CS_AS_SC_UTF8`** (case sensitive, accent sensitive, UTF-8). Serverul pe care stă e de regulă case **in**sensitive (`SQL_Latin1_General_CP1_CI_AS`), deci `tempdb` are alt collation decât baza.

## Majuscule și nume

- Orice identificator se scrie **exact ca în declarație**: `core.Partner`, nu `core.partner`; `@InvoiceId`, nu `@invoiceId` (`SQL-22`).
- Vederile de sistem se scriu cu litere mici: `sys.databases`, `sys.objects`, `sys.schemas`. `SYS.Databases` dă eroare.
- `User` e cuvânt rezervat: mereu `core.[User]`.
- Jurnalul DbUp e exact `core.SchemaVersion`.
- Codurile de sistem se compară exact: `ADMIN`, `NOMENCLATOARE`, `AUDITOR`, `STANDARD`.

## Scheme

Schemele permise: `core`, `ids`, `hangfire`, `intg`, `fin`, `inv`, `sales`, `purch`, `wms`, `tms`, `mfg`, `hr`, `pos`, `cons`, `crm`, `rpt`. **Nimic în `dbo`** (`SQL-01`); singurele referințe permise sunt `master.dbo.` și `msdb.dbo.`.

Ordinea fixă a modulelor (și la migrații, și la obiecte programabile): `core`, `ids`, `hangfire`, `intg`, `fin`, `inv`, `sales`, `purch`, `wms`, `tms`, `mfg`, `hr`, `pos`, `cons`, `crm`, `rpt`.

## Colații în cod

- În tabelele temporare `#t`, coloanele text se declară `COLLATE DATABASE_DEFAULT` — altfel join-urile dau „Cannot resolve the collation conflict” (`SQL-23`). Parametrii tabelă `tt_*` și variabilele tabelă nu au problema.
- Unicitatea codurilor introduse de utilizatori: coloană calculată persistată `CodeKey AS UPPER(Code) PERSISTED` + index unic (`SQL-24`).
- Căutarea după denumire: `NameSearch AS (Name COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED`, indexată, folosită cu `LIKE @Term + N'%'` (SARGable, `SQL-25`, `SP-09`).
- Niciodată `ALTER DATABASE … COLLATE`.

## Migrații (`database/migrations/<modul>/`)

- Nume: `^\d{4}\.\d{2}\.\d{3}_[A-Za-z0-9_]+\.sql$`, de exemplu `2026.11.001_Create_Company.sql` (`MIG-01`). În jurnal: `<modul>/<fișier>`.
- Rulate **o singură dată**, `WithTransactionPerScript`, doar înainte (`MIG-03`). Un script livrat nu se mai modifică niciodată (`MIG-02`) — corecția se face cu o migrare nouă.
- O migrare referă doar obiecte din schema modulului ei și din `core`.
- Tipurile tabelă (`CREATE TYPE … AS TABLE`) stau în migrații, nu în `Programmability/`.

## Obiecte programabile (`database/programmability/<modul>/`)

- Un singur obiect pe fișier, început (după comentarii) cu **`CREATE OR ALTER`** (`MIG-04`), urmat opțional de `GRANT` pentru alte roluri de modul.
- Rulate la fiecare instalare, actualizare și reparare, cu `NullJournal`. Ordinea: module → `Functions/` → `Views/` → `Procedures/` → alfabetic; prefix `NN_` când există dependență.
- Eliminarea unui obiect retras se face printr-o migrare (`DROP PROCEDURE IF EXISTS …`).

## Date livrate (`database/seed/<modul>/`)

`MERGE` pe cheia naturală, idempotent, fără a șterge rânduri create de client (`MIG-06`). Nu încarcă `core.Permission` — permisiunile le generează aplicația la pornire.

## Proceduri

`SET NOCOUNT ON; SET XACT_ABORT ON;` la început. Tranzacția e deschisă de apelant când specificația spune asta (`SP-03`). Erori cu `THROW` și coduri din plaja modulului (schema `ids`: 64000–64999). Fără `SELECT *`. Fără cursoare acolo unde o instrucțiune pe set e posibilă.

## Fișiere

UTF-8 **fără BOM**; textele românești ca literali `N'…'`; loturile separate prin `GO` pe rând propriu. `CREATE SCHEMA` trebuie să fie prima instrucțiune din lotul ei.
