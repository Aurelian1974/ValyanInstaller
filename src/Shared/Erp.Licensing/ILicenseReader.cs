namespace Erp.Licensing;

/// <summary>
/// Citirea și verificarea fișierului de licență (4.2). Implementarea nu aruncă excepții
/// pentru fișiere invalide: întoarce un <see cref="LicenseReadResult"/> cu mesajul în română.
/// </summary>
public interface ILicenseReader
{
    /// <summary>Verificările din 4.2, în ordine; primul eșec oprește verificarea.</summary>
    LicenseReadResult Read(byte[] file);

    /// <inheritdoc cref="Read(byte[])"/>
    LicenseReadResult Read(byte[] file, LicenseReadOptions options);
}
