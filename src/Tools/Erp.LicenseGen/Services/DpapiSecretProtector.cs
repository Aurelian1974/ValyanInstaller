using System.Runtime.Versioning;
using System.Security.Cryptography;
using Erp.Licensing.Generator;

namespace Erp.LicenseGen.Services;

/// <summary>
/// Protejarea cheii private cu DPAPI <c>CurrentUser</c>: fișierul <c>sign-&lt;KeyId&gt;.bin</c>
/// poate fi citit numai de contul Windows al lui Aurelian, pe acest calculator (3.5).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    /// <summary>Entropie suplimentară, ca datele să nu fie decriptabile de altă aplicație.</summary>
    private static readonly byte[] Entropy = "ErpLicenseGen.SigningKey.v1"u8.ToArray();

    public byte[] Protect(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
    }
}
