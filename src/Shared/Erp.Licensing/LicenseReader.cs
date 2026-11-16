using System.Security.Cryptography;
using System.Text.Json;

namespace Erp.Licensing;

/// <summary>
/// Verificarea fișierului de licență, în ordinea din 4.2: structura, semnătura ECDSA,
/// decriptarea AES-GCM, conținutul JSON, expirarea. Primul eșec oprește verificarea.
/// <para>
/// Conținutul decriptat există doar în memorie și tamponul lui se șterge imediat după
/// deserializare; nimic din licență nu ajunge în jurnale sau în mesaje de eroare (R8).
/// </para>
/// </summary>
public sealed class LicenseReader : ILicenseReader
{
    private static readonly JsonSerializerOptions ContentJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    private readonly ILicenseKeyRing keyRing;
    private readonly TimeProvider timeProvider;

    /// <summary>Cititorul folosit în produs, cu cheile din <see cref="LicenseKeys"/>.</summary>
    public LicenseReader()
        : this(LicenseKeys.Ring, TimeProvider.System)
    {
    }

    public LicenseReader(ILicenseKeyRing keyRing, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(keyRing);
        this.keyRing = keyRing;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public LicenseReadResult Read(byte[] file) => Read(file, LicenseReadOptions.Default);

    public LicenseReadResult Read(byte[] file, LicenseReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(options);

        // Verificarea 1: marcajul, versiunea, lungimile (4.2).
        if (!LicenseFileFormat.TryReadLayout(file, out var layout))
        {
            return LicenseReadResult.Failed(LicenseFailureCode.NotAnErpLicense, LicenseMessages.NotAnErpLicense);
        }

        // Verificarea 2: KeyId cunoscut și semnătura ECDSA validă peste tot fișierul
        // în afară de semnătură (3.4).
        if (!keyRing.TryGet(layout.KeyId, out var material))
        {
            return LicenseReadResult.Failed(
                LicenseFailureCode.NotIssuedByProducer,
                LicenseMessages.NotIssuedByProducer);
        }

        if (!VerifySignature(file, layout, material))
        {
            return LicenseReadResult.Failed(
                LicenseFailureCode.NotIssuedByProducer,
                LicenseMessages.NotIssuedByProducer);
        }

        // Verificarea 3: decriptarea AES-GCM, cu antetul ca date autentificate (3.4).
        var plaintext = new byte[layout.CipherLength];
        try
        {
            if (!TryDecrypt(file, layout, material, plaintext))
            {
                return LicenseReadResult.Failed(LicenseFailureCode.Corrupted, LicenseMessages.Corrupted);
            }

            // Verificarea 4: JSON valid, product = „ERP”, fiscalCodes conform 3.3.
            if (!TryDeserialize(plaintext, out var content))
            {
                return LicenseReadResult.Failed(LicenseFailureCode.Corrupted, LicenseMessages.Corrupted);
            }

            // Verificarea 5: validUntil este null sau ≥ azi (4.2).
            if (options.CheckExpiry && content.ValidUntil is { } validUntil)
            {
                var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
                if (validUntil < today)
                {
                    return LicenseReadResult.ExpiredLicense(content, validUntil);
                }
            }

            return LicenseReadResult.Valid(content);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static bool VerifySignature(
        ReadOnlySpan<byte> file,
        LicenseFileLayout layout,
        LicenseKeyMaterial material)
    {
        using var ecdsa = material.CreateEcdsa();
        try
        {
            return ecdsa.VerifyData(
                file[layout.SignedBytes],
                file[layout.Signature],
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool TryDecrypt(
        ReadOnlySpan<byte> file,
        LicenseFileLayout layout,
        LicenseKeyMaterial material,
        Span<byte> plaintext)
    {
        using var aes = material.CreateAesGcm();
        try
        {
            aes.Decrypt(
                nonce: file[LicenseFileFormat.NonceRange],
                ciphertext: file[layout.Cipher],
                tag: file[layout.Tag],
                plaintext: plaintext,
                associatedData: file[LicenseFileFormat.HeaderRange]);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool TryDeserialize(ReadOnlySpan<byte> plaintext, out LicenseContent content)
    {
        content = null!;

        LicenseContent? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LicenseContent>(plaintext, ContentJsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (parsed is null)
        {
            return false;
        }

        if (!string.Equals(parsed.Product, "ERP", StringComparison.Ordinal))
        {
            return false;
        }

        if (!FiscalCode.IsValidList(parsed.FiscalCodes))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.LicenseId) || string.IsNullOrWhiteSpace(parsed.CustomerName))
        {
            return false;
        }

        if (parsed.Modules.Count == 0 || !parsed.HasModule(LicenseModules.Mandatory))
        {
            return false;
        }

        if (parsed.Limits.Users < 0 || parsed.Limits.PayrollEmployees < 0 || parsed.Limits.PosTerminals < 0)
        {
            return false;
        }

        content = parsed;
        return true;
    }
}
