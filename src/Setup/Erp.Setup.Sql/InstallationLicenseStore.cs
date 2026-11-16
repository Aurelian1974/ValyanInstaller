using System.Data;
using Dapper;
using Erp.Licensing;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql;

/// <summary>
/// Scrierea și citirea licenței în baza de date (4.1, 4.3, decizia D13).
/// <para>
/// Installer-ul scrie aici atât octeții criptați, cât și conținutul lizibil, inclusiv câte un
/// rând per CUI, ca ERP-ul să poată verifica CUI-urile cu un <c>SELECT</c>, fără criptografie.
/// Aplicația nu poate scrie aceste tabele: are doar <c>SELECT</c>, iar scrierea e refuzată cu
/// <c>DENY</c> (4.3, regula 8).
/// </para>
/// <para>Tot SQL-ul de aici e parametrizat (R2): nu se construiește text cu valori.</para>
/// </summary>
public sealed class InstallationLicenseStore
{
    private readonly ISetupConnectionFactory connections;

    public InstallationLicenseStore(ISetupConnectionFactory connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        this.connections = connections;
    }

    /// <summary>
    /// Scrie licența ca licență curentă (I-09 și 10.5). Idempotent (R10): dacă aceeași licență
    /// e deja curentă, nu se scrie nimic și se întoarce rândul existent.
    /// </summary>
    public async Task<SaveLicenseResult> SaveAsync(
        LicenseContent license,
        byte[] licenseFile,
        byte keyId,
        string loadedBy,
        string setupVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(license);
        ArgumentNullException.ThrowIfNull(licenseFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(loadedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(setupVersion);

        if (!FiscalCode.IsValidList(license.FiscalCodes))
        {
            throw new ArgumentException(
                "Lista de CUI-uri nu respectă regula din 3.3; licența nu se scrie în bază.",
                nameof(license));
        }

        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);

        var existing = await ReadCurrentFileAsync(connection, ct).ConfigureAwait(false);
        if (existing is { } current && current.File.AsSpan().SequenceEqual(licenseFile))
        {
            return new SaveLicenseResult(current.Id, WasWritten: false);
        }

        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE core.InstallationLicense SET IsCurrent = 0 WHERE IsCurrent = 1;",
                    transaction: transaction,
                    cancellationToken: ct)).ConfigureAwait(false);

            var id = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    """
                    INSERT INTO core.InstallationLicense
                        (LicenseId, CustomerName, FiscalCodes, Modules,
                         MaxUsers, MaxPayrollEmployees, MaxPosTerminals,
                         IssuedAt, ValidUntil, LicenseFile, KeyId, IsCurrent, LoadedBy, SetupVersion)
                    VALUES
                        (@LicenseId, @CustomerName, @FiscalCodes, @Modules,
                         @MaxUsers, @MaxPayrollEmployees, @MaxPosTerminals,
                         @IssuedAt, @ValidUntil, @LicenseFile, @KeyId, 1, @LoadedBy, @SetupVersion);

                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """,
                    new
                    {
                        LicenseId = license.LicenseId,
                        CustomerName = license.CustomerName,
                        FiscalCodes = license.FiscalCodes,
                        Modules = string.Join(' ', license.Modules),
                        MaxUsers = license.Limits.Users,
                        MaxPayrollEmployees = license.Limits.PayrollEmployees,
                        MaxPosTerminals = license.Limits.PosTerminals,
                        IssuedAt = license.IssuedAt,
                        ValidUntil = license.ValidUntil,
                        LicenseFile = licenseFile,
                        KeyId = keyId,
                        LoadedBy = loadedBy,
                        SetupVersion = setupVersion,
                    },
                    transaction: transaction,
                    cancellationToken: ct)).ConfigureAwait(false);

            var codes = license.FiscalCodeList
                .Select((code, index) => new { InstallationLicenseId = id, FiscalCode = code, Ordinal = index + 1 })
                .ToArray();

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO core.InstallationLicenseFiscalCode (InstallationLicenseId, FiscalCode, Ordinal)
                    VALUES (@InstallationLicenseId, @FiscalCode, @Ordinal);
                    """,
                    codes,
                    transaction: transaction,
                    cancellationToken: ct)).ConfigureAwait(false);

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new SaveLicenseResult(id, WasWritten: true);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Licența curentă, cu CUI-urile ei; <c>null</c> dacă nu s-a încărcat niciuna.</summary>
    public async Task<StoredLicense?> ReadCurrentAsync(CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<StoredLicenseRow>(
            new CommandDefinition(
                """
                SELECT TOP (1)
                       l.Id, l.LicenseId, l.CustomerName, l.FiscalCodes, l.Modules,
                       l.MaxUsers, l.MaxPayrollEmployees, l.MaxPosTerminals,
                       l.IssuedAt, l.ValidUntil, l.KeyId, l.LoadedAt, l.LoadedBy, l.SetupVersion
                FROM core.InstallationLicense AS l
                WHERE l.IsCurrent = 1;
                """,
                cancellationToken: ct)).ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var codes = await connection.QueryAsync<string>(
            new CommandDefinition(
                """
                SELECT c.FiscalCode
                FROM core.InstallationLicenseFiscalCode AS c
                WHERE c.InstallationLicenseId = @Id
                ORDER BY c.Ordinal;
                """,
                new { row.Id },
                cancellationToken: ct)).ConfigureAwait(false);

        return new StoredLicense
        {
            Id = row.Id,
            LicenseId = row.LicenseId,
            CustomerName = row.CustomerName,
            FiscalCodes = [.. codes],
            Modules = row.Modules.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            MaxUsers = row.MaxUsers,
            MaxPayrollEmployees = row.MaxPayrollEmployees,
            MaxPosTerminals = row.MaxPosTerminals,
            IssuedAt = DateOnly.FromDateTime(row.IssuedAt),
            ValidUntil = row.ValidUntil is { } until ? DateOnly.FromDateTime(until) : null,
            KeyId = row.KeyId,
            LoadedAt = row.LoadedAt,
            LoadedBy = row.LoadedBy,
            SetupVersion = row.SetupVersion,
        };
    }

    /// <summary>
    /// CUI-urile licențiate, așa cum le citește ERP-ul din <c>core.v_LicensedFiscalCode</c> (4.3).
    /// </summary>
    public async Task<IReadOnlyList<string>> ReadLicensedFiscalCodesAsync(CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);

        var codes = await connection.QueryAsync<string>(
            new CommandDefinition(
                "SELECT v.FiscalCode FROM core.v_LicensedFiscalCode AS v ORDER BY v.Ordinal;",
                cancellationToken: ct)).ConfigureAwait(false);

        return [.. codes];
    }

    /// <summary>
    /// Firmele active al căror CUI nu e în lista dată (10.5): installer-ul avertizează înainte
    /// de a scrie o licență mai mică, pentru că aceste firme ar trece în doar-citire (4.3, regula 7).
    /// <para>
    /// Tabela `core.Company` e creată de migrațiile ERP-ului, deci la o instalare nouă încă nu
    /// există: atunci lista e goală, fără eroare.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<ActiveCompany>> FindUnlicensedActiveCompaniesAsync(
        IReadOnlyList<string> licensedFiscalCodes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(licensedFiscalCodes);

        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);

        var hasCompanyTable = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT CASE WHEN OBJECT_ID(N'core.Company', N'U') IS NULL THEN 0 ELSE 1 END;",
                cancellationToken: ct)).ConfigureAwait(false);

        if (hasCompanyTable == 0)
        {
            return [];
        }

        var codes = new DataTable();
        codes.Columns.Add("FiscalCode", typeof(string));
        foreach (var code in licensedFiscalCodes)
        {
            codes.Rows.Add(code);
        }

        var rows = await connection.QueryAsync<ActiveCompany>(
            new CommandDefinition(
                """
                SELECT c.Id, c.Name, c.FiscalCode
                FROM core.Company AS c
                WHERE c.IsActive = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM @Licensed AS l
                      WHERE l.FiscalCode = c.FiscalCode COLLATE DATABASE_DEFAULT)
                ORDER BY c.Name;
                """,
                new { Licensed = codes.AsTableValuedParameter("core.tt_FiscalCode") },
                cancellationToken: ct)).ConfigureAwait(false);

        return [.. rows];
    }

    /// <summary>Numărul de licențe din istoric (4.1).</summary>
    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM core.InstallationLicense;",
                cancellationToken: ct)).ConfigureAwait(false);
    }

    private static async Task<(int Id, byte[] File)?> ReadCurrentFileAsync(
        SqlConnection connection,
        CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<(int Id, byte[] File)?>(
            new CommandDefinition(
                "SELECT TOP (1) l.Id, l.LicenseFile FROM core.InstallationLicense AS l WHERE l.IsCurrent = 1;",
                cancellationToken: ct)).ConfigureAwait(false);

        return row;
    }

    private sealed record StoredLicenseRow(
        int Id,
        string LicenseId,
        string CustomerName,
        string FiscalCodes,
        string Modules,
        int MaxUsers,
        int MaxPayrollEmployees,
        int MaxPosTerminals,
        DateTime IssuedAt,              // coloana `date` ajunge în ADO.NET ca DateTime
        DateTime? ValidUntil,
        byte KeyId,
        DateTime LoadedAt,
        string LoadedBy,
        string SetupVersion);
}

/// <summary>O firmă activă din instalare (10.5).</summary>
public sealed record ActiveCompany(int Id, string Name, string FiscalCode);

/// <summary>Rezultatul scrierii licenței: dacă s-a scris sau era deja aceeași (R10).</summary>
public sealed record SaveLicenseResult(int Id, bool WasWritten);

/// <summary>Licența citită din baza de date (4.1).</summary>
public sealed record StoredLicense
{
    public required int Id { get; init; }

    public required string LicenseId { get; init; }

    public required string CustomerName { get; init; }

    /// <summary>CUI-urile, în ordinea din licență.</summary>
    public required IReadOnlyList<string> FiscalCodes { get; init; }

    public required IReadOnlyList<string> Modules { get; init; }

    public required int MaxUsers { get; init; }

    public required int MaxPayrollEmployees { get; init; }

    public required int MaxPosTerminals { get; init; }

    public required DateOnly IssuedAt { get; init; }

    public DateOnly? ValidUntil { get; init; }

    public required byte KeyId { get; init; }

    public required DateTime LoadedAt { get; init; }

    public required string LoadedBy { get; init; }

    public required string SetupVersion { get; init; }

    /// <summary>Adevărat dacă CUI-ul normalizat e licențiat (4.3).</summary>
    public bool IsLicensed(string normalizedFiscalCode)
        => FiscalCodes.Contains(normalizedFiscalCode, StringComparer.Ordinal);
}
