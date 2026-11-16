-- Licența instalării (4.1, 4.3, deciziile D13, D14 și D15).
--
-- Tabela o scrie NUMAI installer-ul, cu contul Windows al operatorului. Aplicația o citește:
-- rolurile de modul primesc SELECT, iar scrierea e refuzată explicit cu DENY (4.3, regula 8).
-- Peste asta, rândul poartă semnătura detașată a licenței (D15): un rând modificat de un cont
-- sysadmin nu mai are semnătură validă, deci ERP-ul refuză lista întreagă (4.3, regulile 2 și 9).
--
-- CUI-urile NU se păstrează în rânduri separate: ar fi date nesemnate, deci o cale de ocolire.
-- Vederea le desparte din coloana semnată, cu STRING_SPLIT (SQL Server 2022, nivel 160).
--
-- Idempotent (R10): rulat de la zero sau peste o instalare existentă, nu pierde nimic.
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'core.InstallationLicense', N'U') IS NULL
BEGIN
    CREATE TABLE core.InstallationLicense
    (
        Id                  int            IDENTITY(1,1) NOT NULL CONSTRAINT PK_InstallationLicense PRIMARY KEY,
        LicenseId           nvarchar(32)   NOT NULL,          -- LIC-AAAA-NNNNNN (3.2)
        CustomerName        nvarchar(200)  NOT NULL,
        FiscalCodes         nvarchar(200)  NOT NULL,          -- cifre separate prin „:” (3.3)
        Modules             nvarchar(400)  NOT NULL,          -- coduri separate prin spațiu (3.3)
        MaxUsers            int            NOT NULL,
        MaxPayrollEmployees int            NOT NULL,
        MaxPosTerminals     int            NOT NULL,
        IssuedAt            date           NOT NULL,
        ValidUntil          date           NULL,              -- NULL = Nelimitat (3.3)
        ContentSignature    varbinary(64)  NOT NULL,          -- semnătura detașată, 64 de octeți (D15)
        LicenseFile         varbinary(max) NOT NULL,          -- octeții criptați, pentru backup (4.1)
        KeyId               tinyint        NOT NULL,          -- perechea de chei folosită (3.4)
        IsCurrent           bit            NOT NULL CONSTRAINT DF_InstallationLicense_IsCurrent DEFAULT (1),
        LoadedAt            datetime2(3)   NOT NULL CONSTRAINT DF_InstallationLicense_LoadedAt DEFAULT (sysutcdatetime()),
        LoadedBy            nvarchar(128)  NOT NULL,          -- contul Windows al operatorului
        SetupVersion        nvarchar(20)   NOT NULL,
        CONSTRAINT CK_InstallationLicense_FiscalCodes CHECK (FiscalCodes NOT LIKE N'%[^0-9:]%'),
        CONSTRAINT CK_InstallationLicense_Signature CHECK (DATALENGTH(ContentSignature) = 64),
        CONSTRAINT CK_InstallationLicense_Limits CHECK (MaxUsers >= 1 AND MaxPayrollEmployees >= 0 AND MaxPosTerminals >= 0)
    );

    -- O singură licență curentă; cele vechi rămân în istoric (4.1).
    CREATE UNIQUE INDEX UX_InstallationLicense_Current
        ON core.InstallationLicense (IsCurrent)
        WHERE IsCurrent = 1;
END;
GO

-- Tabela cu CUI-uri separate a existat înaintea deciziei D15; rândurile ei nu erau acoperite de
-- semnătură, deci datele se iau acum din coloana semnată. Se elimină numai dacă există.
IF OBJECT_ID(N'core.InstallationLicenseFiscalCode', N'U') IS NOT NULL
    DROP TABLE core.InstallationLicenseFiscalCode;
GO

-- Tip tabelă pentru compararea listelor de CUI-uri (10.5). Tipurile tabelă nu au
-- CREATE OR ALTER, deci se creează o singură dată (5.8).
IF TYPE_ID(N'core.tt_FiscalCode') IS NULL
    CREATE TYPE core.tt_FiscalCode AS TABLE
    (
        FiscalCode varchar(10) NOT NULL PRIMARY KEY
    );
GO

-- Licența curentă, cu tot ce are nevoie ERP-ul ca să verifice semnătura (4.3, regula 2).
CREATE OR ALTER VIEW core.v_InstallationLicense
AS
SELECT l.LicenseId,
       l.CustomerName,
       l.FiscalCodes,
       l.Modules,
       l.MaxUsers,
       l.MaxPayrollEmployees,
       l.MaxPosTerminals,
       l.IssuedAt,
       l.ValidUntil,
       l.ContentSignature,
       l.KeyId,
       l.LoadedAt
FROM core.InstallationLicense AS l
WHERE l.IsCurrent = 1;
GO

-- CUI-urile licenței curente, despărțite din coloana SEMNATĂ (D15).
CREATE OR ALTER VIEW core.v_LicensedFiscalCode
AS
SELECT CAST(c.value AS varchar(10)) AS FiscalCode,
       c.ordinal                    AS Ordinal,
       l.LicenseId,
       l.ValidUntil
FROM core.InstallationLicense AS l
CROSS APPLY STRING_SPLIT(l.FiscalCodes, N':', 1) AS c
WHERE l.IsCurrent = 1;
GO

-- Doar-citire pentru aplicație (4.3, regula 8): DENY are prioritate față de GRANT-ul pe schemă.
DECLARE @Sql nvarchar(max) = N'';

SELECT @Sql = @Sql
    + N'GRANT SELECT ON OBJECT::core.InstallationLicense TO ' + QUOTENAME(N'role_' + s.Name) + N';' + CHAR(10)
    + N'DENY INSERT, UPDATE, DELETE ON OBJECT::core.InstallationLicense TO ' + QUOTENAME(N'role_' + s.Name) + N';' + CHAR(10)
FROM (VALUES
    (N'core'), (N'ids'), (N'hangfire'), (N'intg'), (N'fin'), (N'inv'), (N'sales'), (N'purch'),
    (N'wms'), (N'tms'), (N'mfg'), (N'hr'), (N'pos'), (N'cons'), (N'crm'), (N'rpt')
) AS s (Name)
WHERE DATABASE_PRINCIPAL_ID(N'role_' + s.Name) IS NOT NULL;

IF @Sql <> N''
    EXEC sys.sp_executesql @Sql;
GO
