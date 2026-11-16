-- 5.5: câte un rol de bază de date per schemă, cu drepturi la nivel de schemă, ca să acopere
-- și obiectele create mai târziu. Idempotent (R10). Fără db_owner pentru aplicație (SP-19).
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @Sql nvarchar(max) = N'';

SELECT @Sql = @Sql
    + CASE WHEN DATABASE_PRINCIPAL_ID(N'role_' + s.Name) IS NULL
           THEN N'CREATE ROLE ' + QUOTENAME(N'role_' + s.Name) + N' AUTHORIZATION dbo;' + CHAR(10)
           ELSE N'' END
    + N'GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::' + QUOTENAME(s.Name)
    + N' TO ' + QUOTENAME(N'role_' + s.Name) + N';' + CHAR(10)
FROM (VALUES
    (N'core'), (N'ids'), (N'hangfire'), (N'intg'), (N'fin'), (N'inv'), (N'sales'), (N'purch'),
    (N'wms'), (N'tms'), (N'mfg'), (N'hr'), (N'pos'), (N'cons'), (N'crm'), (N'rpt')
) AS s (Name);

EXEC sys.sp_executesql @Sql;
GO
