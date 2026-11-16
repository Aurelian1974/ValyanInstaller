-- 5.5: schemele bazei (SQL-01). Rulat la fiecare instalare, actualizare și reparare; idempotent (R10).
-- Fiecare CREATE SCHEMA trebuie să fie prima instrucțiune din lotul lui, deci se execută
-- câte unul, prin sp_executesql.
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @Schemas TABLE (Name sysname NOT NULL PRIMARY KEY);

INSERT INTO @Schemas (Name)
VALUES (N'core'), (N'ids'), (N'hangfire'), (N'intg'), (N'fin'), (N'inv'), (N'sales'), (N'purch'),
       (N'wms'), (N'tms'), (N'mfg'), (N'hr'), (N'pos'), (N'cons'), (N'crm'), (N'rpt');

DECLARE @Name sysname;
DECLARE @Sql nvarchar(max);

WHILE EXISTS (SELECT 1 FROM @Schemas)
BEGIN
    SELECT TOP (1) @Name = s.Name
    FROM @Schemas AS s
    ORDER BY s.Name;

    IF SCHEMA_ID(@Name) IS NULL
    BEGIN
        SET @Sql = N'CREATE SCHEMA ' + QUOTENAME(@Name) + N' AUTHORIZATION dbo;';
        EXEC sys.sp_executesql @Sql;
    END;

    DELETE FROM @Schemas WHERE Name = @Name;
END;
GO
