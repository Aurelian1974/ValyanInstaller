-- Licența instalării (4.1, 4.3, deciziile D13 și D14).
--
-- Tabelele le scrie NUMAI installer-ul, cu contul Windows al operatorului. Aplicația le citește:
-- rolurile de modul primesc SELECT, iar scrierea e refuzată explicit cu DENY (4.3, regula 8).
-- Idempotent (R10): rulat de la zero sau peste o instalare existentă, nu pierde nimic.
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'core.InstallationLicense', N'U') IS NULL
BEGIN
    CREATE TABLE core.InstallationLicense
    (
        Id                int               IDENTITY(1,1) NOT NULL CONSTRAINT PK_InstallationLicense PRIMARY KEY,
        LicenseId         nvarchar(32)      NOT NULL,          -- LIC-AAAA-NNNNNN (3.2)
        CustomerName      nvarchar(200)     NOT NULL,
        FiscalCodes       nvarchar(200)     NOT NULL,          -- forma din licență: cifre separate prin „:” (3.3)
        Modules           nvarchar(400)     NOT NULL,          -- coduri separate prin spațiu (3.3)
        MaxUsers          int               NOT NULL,
        MaxPayrollEmployees int             NOT NULL,
        MaxPosTerminals   int               NOT NULL,
        IssuedAt          date              NOT NULL,
        ValidUntil        date              NULL,              -- NULL = Nelimitat (3.3)
        LicenseFile       varbinary(max)    NOT NULL,          -- octeții criptați, ca licența să intre în backup (4.1)
        KeyId             tinyint           NOT NULL,          -- perechea de chei folosită (3.4)
        IsCurrent         bit               NOT NULL CONSTRAINT DF_InstallationLicense_IsCurrent DEFAULT (1),
        LoadedAt          datetime2(3)      NOT NULL CONSTRAINT DF_InstallationLicense_LoadedAt DEFAULT (sysutcdatetime()),
        LoadedBy          nvarchar(128)     NOT NULL,          -- contul Windows al operatorului
        SetupVersion      nvarchar(20)      NOT NULL,
        CONSTRAINT CK_InstallationLicense_FiscalCodes CHECK (FiscalCodes NOT LIKE N'%[^0-9:]%'),
        CONSTRAINT CK_InstallationLicense_Limits CHECK (MaxUsers >= 1 AND MaxPayrollEmployees >= 0 AND MaxPosTerminals >= 0)
    );

    -- O singură licență curentă; cele vechi rămân în istoric (4.1).
    CREATE UNIQUE INDEX UX_InstallationLicense_Current
        ON core.InstallationLicense (IsCurrent)
        WHERE IsCurrent = 1;
END;
GO

IF OBJECT_ID(N'core.InstallationLicenseFiscalCode', N'U') IS NULL
BEGIN
    CREATE TABLE core.InstallationLicenseFiscalCode
    (
        Id                   int          IDENTITY(1,1) NOT NULL CONSTRAINT PK_InstallationLicenseFiscalCode PRIMARY KEY,
        InstallationLicenseId int         NOT NULL
            CONSTRAINT FK_InstallationLicenseFiscalCode_InstallationLicense
            REFERENCES core.InstallationLicense (Id) ON DELETE CASCADE,
        FiscalCode           varchar(10)  NOT NULL,            -- numai cifre, forma normalizată (4.3)
        Ordinal              int          NOT NULL,            -- ordinea din licență
        CONSTRAINT CK_InstallationLicenseFiscalCode_Digits CHECK (FiscalCode NOT LIKE '%[^0-9]%' AND LEN(FiscalCode) >= 2),
        CONSTRAINT UQ_InstallationLicenseFiscalCode UNIQUE (InstallationLicenseId, FiscalCode)
    );

    CREATE INDEX IX_InstallationLicenseFiscalCode_FiscalCode
        ON core.InstallationLicenseFiscalCode (FiscalCode)
        INCLUDE (InstallationLicenseId);
END;
GO

-- Tip tabelă pentru compararea listelor de CUI-uri (10.5). Tipurile tabelă nu au
-- CREATE OR ALTER, deci se creează o singură dată (5.8).
IF TYPE_ID(N'core.tt_FiscalCode') IS NULL
    CREATE TYPE core.tt_FiscalCode AS TABLE
    (
        FiscalCode varchar(10) NOT NULL PRIMARY KEY
    );
GO

-- Vederea pe care o citește ERP-ul: CUI-urile licenței curente (4.3, regulile 2 și 3).
CREATE OR ALTER VIEW core.v_LicensedFiscalCode
AS
SELECT c.FiscalCode,
       c.Ordinal,
       l.LicenseId,
       l.ValidUntil
FROM core.InstallationLicenseFiscalCode AS c
JOIN core.InstallationLicense AS l ON l.Id = c.InstallationLicenseId
WHERE l.IsCurrent = 1;
GO

-- Doar-citire pentru aplicație (4.3, regula 8): DENY are prioritate față de GRANT-ul pe schemă.
DECLARE @Sql nvarchar(max) = N'';

SELECT @Sql = @Sql
    + N'GRANT SELECT ON OBJECT::core.InstallationLicense TO ' + QUOTENAME(N'role_' + s.Name) + N';' + CHAR(10)
    + N'GRANT SELECT ON OBJECT::core.InstallationLicenseFiscalCode TO ' + QUOTENAME(N'role_' + s.Name) + N';' + CHAR(10)
    + N'DENY INSERT, UPDATE, DELETE ON OBJECT::core.InstallationLicense TO ' + QUOTENAME(N'role_' + s.Name) + N';' + CHAR(10)
    + N'DENY INSERT, UPDATE, DELETE ON OBJECT::core.InstallationLicenseFiscalCode TO ' + QUOTENAME(N'role_' + s.Name) + N';' + CHAR(10)
FROM (VALUES
    (N'core'), (N'ids'), (N'hangfire'), (N'intg'), (N'fin'), (N'inv'), (N'sales'), (N'purch'),
    (N'wms'), (N'tms'), (N'mfg'), (N'hr'), (N'pos'), (N'cons'), (N'crm'), (N'rpt')
) AS s (Name)
WHERE DATABASE_PRINCIPAL_ID(N'role_' + s.Name) IS NOT NULL;

IF @Sql <> N''
    EXEC sys.sp_executesql @Sql;
GO
