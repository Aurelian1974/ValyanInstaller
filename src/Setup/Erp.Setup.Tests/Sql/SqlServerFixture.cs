using Erp.Setup.Sql;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Tests.Sql;

/// <summary>
/// O bază de test cu collation-ul din R4 (`Latin1_General_100_CS_AS_SC_UTF8`), creată pe un
/// server cu collation implicit case insensitive — scenariul cerut de `TST-09`, ca greșelile
/// de majuscule și conflictele cu `tempdb` să apară în CI, nu la client.
/// <para>
/// Serverul se ia din variabila de mediu <c>ERP_TEST_SQL</c>; în lipsa ei, din containerul
/// local de pe portul 11433. Autentificarea SQL se folosește NUMAI aici: produsul se conectează
/// exclusiv cu Windows authentication (R5), ceea ce nu există pe Linux, în CI.
/// </para>
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Server=127.0.0.1,11433;User Id=sa;Password=Erp-Test-2026!parola;TrustServerCertificate=true;Encrypt=true;Pooling=false";

    public const string Collation = "Latin1_General_100_CS_AS_SC_UTF8";

    private string masterConnectionString = string.Empty;

    /// <summary>Numele bazei de test, unic pentru fiecare rulare.</summary>
    public string DatabaseName { get; } = $"ERP_TEST_{Guid.NewGuid():N}"[..24];

    /// <summary>Fals dacă nu există SQL Server: testele de integrare se sar.</summary>
    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    public ISetupConnectionFactory Connections { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        masterConnectionString = Environment.GetEnvironmentVariable("ERP_TEST_SQL") ?? DefaultConnectionString;

        try
        {
            await using var connection = new SqlConnection(WithDatabase(masterConnectionString, "master"));
            await connection.OpenAsync();

            await using (var command = connection.CreateCommand())
            {
                // Numele bazei e generat aici (litere, cifre, „_”), deci se poate pune direct
                // în QUOTENAME; în produs, aceeași regulă e aplicată de Erp.Setup.Sql (5.5).
                command.CommandText =
                    $"CREATE DATABASE [{DatabaseName}] COLLATE {Collation};";
                await command.ExecuteNonQueryAsync();
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    $"ALTER DATABASE [{DatabaseName}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;";
                await command.ExecuteNonQueryAsync();
            }

            Connections = new TestConnectionFactory(WithDatabase(masterConnectionString, DatabaseName), DatabaseName);
            IsAvailable = true;
        }
        catch (SqlException ex)
        {
            IsAvailable = false;
            UnavailableReason = ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        await using var connection = new SqlConnection(WithDatabase(masterConnectionString, "master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}];";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Rulează scripturile de bootstrap (5.5) în baza de test.</summary>
    public async Task<IReadOnlyList<string>> RunBootstrapAsync()
        => await new BootstrapScriptRunner(Connections).RunAsync(ScriptFolder);

    /// <summary>Folderul cu scripturi, copiat lângă binarul testelor.</summary>
    public static string ScriptFolder => Path.Combine(AppContext.BaseDirectory, "database", "bootstrap");

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = (SqlConnection)await Connections.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default! : (T)value;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = (SqlConnection)await Connections.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string WithDatabase(string connectionString, string database)
        => new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database }.ConnectionString;

    private sealed class TestConnectionFactory(string connectionString, string databaseName) : ISetupConnectionFactory
    {
        public string DatabaseName { get; } = databaseName;

        public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
        {
            var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}

/// <summary>Colecția care împarte o singură bază de test între clasele de teste.</summary>
[CollectionDefinition(Name)]
public sealed class SqlServerTestGroup : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}
