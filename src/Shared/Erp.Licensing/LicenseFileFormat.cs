using System.Buffers.Binary;
using System.Text;

namespace Erp.Licensing;

/// <summary>
/// Formatul binar al fișierului <c>.lic</c> (3.4):
/// <code>
/// 0      8   marcaj ASCII „ERPLIC01”
/// 8      1   versiunea formatului = 1
/// 9      1   KeyId (1–255)
/// 10    12   nonce AES-GCM
/// 22     4   lungimea textului criptat n (uint32, little-endian)
/// 26     n   textul criptat (JSON-ul din 3.3)
/// 26+n  16   tag-ul GCM
/// 42+n  64   semnătura ECDSA peste octeții 0 … 41+n
/// </code>
/// AAD pentru GCM = octeții 0 … 25 (marcaj, versiune, KeyId, nonce, lungime).
/// </summary>
public static class LicenseFileFormat
{
    /// <summary>Marcajul de la începutul fișierului.</summary>
    public static ReadOnlySpan<byte> Magic => "ERPLIC01"u8;

    public const int MagicLength = 8;
    public const int VersionOffset = 8;
    public const int KeyIdOffset = 9;
    public const int NonceOffset = 10;
    public const int NonceLength = 12;
    public const int CipherLengthOffset = 22;
    public const int CipherOffset = 26;

    /// <summary>Lungimea antetului, care e și AAD-ul pentru GCM (3.4).</summary>
    public const int HeaderLength = CipherOffset;

    public const int TagLength = 16;
    public const int SignatureLength = 64;

    /// <summary>Versiunea de format pe care o scrie și o citește această bibliotecă.</summary>
    public const byte CurrentVersion = 1;

    /// <summary>Lungimea fișierului fără textul criptat: antet + tag + semnătură.</summary>
    public const int FixedLength = HeaderLength + TagLength + SignatureLength;

    /// <summary>
    /// Limită de siguranță pentru textul criptat: o licență are câteva sute de octeți,
    /// deci orice fișier mai mare e refuzat înainte de orice operație criptografică.
    /// </summary>
    public const int MaxCipherLength = 64 * 1024;

    /// <summary>Antetul, folosit ca date autentificate suplimentare (AAD) pentru GCM (3.4).</summary>
    public static Range HeaderRange => 0..HeaderLength;

    /// <summary>Nonce-ul AES-GCM.</summary>
    public static Range NonceRange => NonceOffset..(NonceOffset + NonceLength);

    /// <summary>Codarea JSON-ului din licență (3.3).</summary>
    public static Encoding ContentEncoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Scrie antetul celor 26 de octeți (3.4).</summary>
    public static void WriteHeader(Span<byte> destination, byte keyId, ReadOnlySpan<byte> nonce, int cipherLength)
    {
        if (destination.Length < HeaderLength)
        {
            throw new ArgumentException("Antetul are nevoie de 26 de octeți.", nameof(destination));
        }

        if (nonce.Length != NonceLength)
        {
            throw new ArgumentException("Nonce-ul AES-GCM are 12 octeți.", nameof(nonce));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(cipherLength);

        Magic.CopyTo(destination);
        destination[VersionOffset] = CurrentVersion;
        destination[KeyIdOffset] = keyId;
        nonce.CopyTo(destination[NonceOffset..(NonceOffset + NonceLength)]);
        BinaryPrimitives.WriteUInt32LittleEndian(
            destination[CipherLengthOffset..(CipherLengthOffset + sizeof(uint))],
            (uint)cipherLength);
    }

    /// <summary>
    /// Verifică structura fișierului: marcajul, versiunea și lungimile (4.2, verificarea 1).
    /// </summary>
    public static bool TryReadLayout(ReadOnlySpan<byte> file, out LicenseFileLayout layout)
    {
        layout = default;

        if (file.Length < FixedLength)
        {
            return false;
        }

        if (!file[..MagicLength].SequenceEqual(Magic))
        {
            return false;
        }

        if (file[VersionOffset] != CurrentVersion)
        {
            return false;
        }

        var keyId = file[KeyIdOffset];
        if (keyId == 0)
        {
            return false;
        }

        var cipherLength = BinaryPrimitives.ReadUInt32LittleEndian(
            file[CipherLengthOffset..(CipherLengthOffset + sizeof(uint))]);

        if (cipherLength == 0 || cipherLength > MaxCipherLength)
        {
            return false;
        }

        if (file.Length != FixedLength + (int)cipherLength)
        {
            return false;
        }

        layout = new LicenseFileLayout(keyId, (int)cipherLength);
        return true;
    }
}

/// <summary>Pozițiile calculate ale unui fișier de licență valid structural (3.4).</summary>
public readonly record struct LicenseFileLayout(byte KeyId, int CipherLength)
{
    public Range Cipher => LicenseFileFormat.CipherOffset
        ..(LicenseFileFormat.CipherOffset + CipherLength);

    public int TagOffset => LicenseFileFormat.CipherOffset + CipherLength;

    public Range Tag => TagOffset..(TagOffset + LicenseFileFormat.TagLength);

    public int SignatureOffset => TagOffset + LicenseFileFormat.TagLength;

    /// <summary>Semnătura ECDSA, ultimii 64 de octeți.</summary>
    public Range Signature => SignatureOffset..(SignatureOffset + LicenseFileFormat.SignatureLength);

    /// <summary>Octeții acoperiți de semnătură: tot fișierul în afară de semnătură (3.4).</summary>
    public Range SignedBytes => 0..SignatureOffset;

    /// <summary>Lungimea totală a fișierului.</summary>
    public int FileLength => LicenseFileFormat.FixedLength + CipherLength;
}
