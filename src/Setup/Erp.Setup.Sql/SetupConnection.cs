using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql;

/// <summary>Deschide conexiuni la SQL Server pentru pașii installer-ului.</summary>
public interface ISetupConnectionFactory
{
    /// <summary>Numele bazei ERP (sau <c>master</c> pentru pașii dinaintea creării ei).</summary>
    string DatabaseName { get; }

    /// <summary>O conexiune nouă, deja deschisă.</summary>
    Task<SqlConnection> OpenAsync(CancellationToken ct = default);
}

/// <summary>
/// Conexiunea installer-ului: **numai Windows authentication**, cu contul operatorului (R5).
/// Nu există parolă SQL și nici fișier cu parole SQL.
/// </summary>
public sealed class WindowsAuthConnectionFactory : ISetupConnectionFactory
{
    private readonly string connectionString;

    public WindowsAuthConnectionFactory(string server, string databaseName, bool trustServerCertificate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        DatabaseName = databaseName;

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = databaseName,
            IntegratedSecurity = true,                      // R5
            Encrypt = SqlConnectionEncryptOption.Mandatory, // C-10
            TrustServerCertificate = trustServerCertificate,
            ApplicationName = "ERP-Setup",
            ConnectTimeout = 15,
            CommandTimeout = 120,
            Pooling = false,
        };

        connectionString = builder.ConnectionString;
    }

    public string DatabaseName { get; }

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
