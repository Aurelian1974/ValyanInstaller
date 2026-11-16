using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql;

/// <summary>
/// Rulează scripturile din <c>database/bootstrap/</c> (5.5): schemele, rolurile și tabelele
/// licenței. Se rulează la fiecare instalare, actualizare și reparare; toate sunt idempotente (R10).
/// Loturile se despart după rândurile care conțin numai <c>GO</c> (5.8).
/// </summary>
public sealed class BootstrapScriptRunner
{
    private readonly ISetupConnectionFactory connections;

    public BootstrapScriptRunner(ISetupConnectionFactory connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        this.connections = connections;
    }

    /// <summary>Folderul cu scripturi, lângă binarul installer-ului.</summary>
    public static string DefaultScriptFolder =>
        Path.Combine(AppContext.BaseDirectory, "database", "bootstrap");

    /// <summary>Scripturile în ordinea numelui (001_, 002_, 003_ …).</summary>
    public static IReadOnlyList<string> ScriptFiles(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Folderul cu scripturi lipsește din kit: {folder}");
        }

        return [.. Directory.EnumerateFiles(folder, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal)];
    }

    /// <summary>Rulează toate scripturile; întoarce numele celor rulate, în ordine.</summary>
    public async Task<IReadOnlyList<string>> RunAsync(
        string folder,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var executed = new List<string>();

        await using var connection = await connections.OpenAsync(ct).ConfigureAwait(false);

        foreach (var file in ScriptFiles(folder))
        {
            var name = Path.GetFileName(file);
            progress?.Report(name);

            var text = await File.ReadAllTextAsync(file, Encoding.UTF8, ct).ConfigureAwait(false);
            var batchNumber = 0;

            foreach (var batch in SplitBatches(text))
            {
                batchNumber++;
                try
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = batch;
                    command.CommandTimeout = 300;
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                catch (SqlException ex)
                {
                    throw new BootstrapScriptException(name, batchNumber, ex);
                }
            }

            executed.Add(name);
        }

        return executed;
    }

    /// <summary>Desparte textul la rândurile care conțin numai <c>GO</c>.</summary>
    public static IEnumerable<string> SplitBatches(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var batch = new StringBuilder();
        foreach (var line in script.Split('\n'))
        {
            var trimmed = line.Trim().TrimEnd('\r');
            if (string.Equals(trimmed, "GO", StringComparison.OrdinalIgnoreCase))
            {
                if (batch.ToString().Trim().Length > 0)
                {
                    yield return batch.ToString();
                }

                batch.Clear();
                continue;
            }

            batch.Append(line).Append('\n');
        }

        if (batch.ToString().Trim().Length > 0)
        {
            yield return batch.ToString();
        }
    }
}

/// <summary>Eșecul unui script de bootstrap, cu numele scriptului și lotul (5.8).</summary>
public sealed class BootstrapScriptException : Exception
{
    public BootstrapScriptException(string scriptName, int batchNumber, SqlException inner)
        : base(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Scriptul {scriptName}, lotul {batchNumber}, a eșuat: {inner.Message}"),
            inner)
    {
        ScriptName = scriptName;
        BatchNumber = batchNumber;
    }

    public string ScriptName { get; }

    public int BatchNumber { get; }
}
