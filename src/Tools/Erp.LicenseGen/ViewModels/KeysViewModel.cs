using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erp.LicenseGen.Services;
using Erp.Licensing;
using Erp.Licensing.Generator;

namespace Erp.LicenseGen.ViewModels;

/// <summary>Fereastra „Chei” (3.5): prima configurare și rotirea cheilor.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class KeysViewModel : ObservableObject
{
    private readonly IDialogService dialogs;
    private readonly LicenseKeyStore keyStore;

    [ObservableProperty]
    private string stateText = string.Empty;

    [ObservableProperty]
    private string matchText = string.Empty;

    [ObservableProperty]
    private string? snippet;

    [ObservableProperty]
    private string? warningText;

    public KeysViewModel(IDialogService dialogs, LicenseKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(keyStore);
        this.dialogs = dialogs;
        this.keyStore = keyStore;
        Refresh();
    }

    /// <summary>Generează perechea de chei pentru următorul <c>KeyId</c> (3.5).</summary>
    [RelayCommand]
    private void GenerateKey()
    {
        var nextKeyId = (byte)((keyStore.CurrentKeyId ?? 0) + 1);
        if (nextKeyId == 0)
        {
            dialogs.ShowError("Chei", "S-au folosit toate cele 255 de numere de cheie.");
            return;
        }

        if (keyStore.CurrentKeyId is { } existing && !dialogs.Confirm(
            "Cheie nouă",
            $"Pe acest calculator există deja cheia {existing.ToString(CultureInfo.InvariantCulture)}. "
            + $"Se generează cheia {nextKeyId.ToString(CultureInfo.InvariantCulture)}; cea veche se păstrează, "
            + "ca licențele emise cu ea să rămână valide. Continuați?"))
        {
            return;
        }

        var keys = LicenseKeyFactory.Create(nextKeyId);
        try
        {
            keyStore.Save(keys);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            dialogs.ShowError("Cheia nu s-a putut salva", ex.Message);
            return;
        }

        Snippet = keys.ToLicenseKeysSnippet();
        Refresh();
    }

    [RelayCommand]
    private void CopySnippet()
    {
        if (Snippet is { Length: > 0 } text)
        {
            dialogs.CopyToClipboard(text);
        }
    }

    /// <summary>Salvează copia de siguranță protejată cu parolă (3.5).</summary>
    public void ExportBackup(string password)
    {
        if (keyStore.CurrentKeyId is not { } keyId)
        {
            dialogs.ShowError("Copie de siguranță", "Nu există nicio cheie de salvat.");
            return;
        }

        var path = dialogs.SaveFile(
            "Salvați copia de siguranță a cheii",
            $"erp-cheie-semnare-{keyId.ToString(CultureInfo.InvariantCulture)}.p8",
            "Cheie criptată (*.p8)|*.p8");

        if (path is null)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(path, keyStore.ExportBackup(keyId, password));
            dialogs.ShowInformation(
                "Copie de siguranță",
                "Copia a fost salvată. Mutați-o pe un suport păstrat offline și ștergeți-o de pe acest calculator.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            dialogs.ShowError("Copia nu s-a putut salva", ex.Message);
        }
    }

    private void Refresh()
    {
        var keyIds = keyStore.KeyIds;

        StateText = keyIds.Count switch
        {
            0 => "Pe acest calculator nu există nicio cheie de semnare.",
            1 => $"Cheia de semnare {keyIds[0].ToString(CultureInfo.InvariantCulture)} există pe acest calculator.",
            _ => $"Chei de semnare pe acest calculator: {string.Join(", ", keyIds)}. "
                 + $"Licențele noi se semnează cu {keyIds[^1].ToString(CultureInfo.InvariantCulture)}.",
        };

        if (keyStore.CurrentKeyId is not { } current)
        {
            MatchText = "Apăsați „Generează o cheie nouă” pentru prima configurare.";
            WarningText = null;
            return;
        }

        if (!LicenseKeys.Ring.TryGet(current, out var material))
        {
            MatchText = $"Cheia {current.ToString(CultureInfo.InvariantCulture)} nu e înscrisă în LicenseKeys.cs.";
            WarningText = "Până la lipirea textului în LicenseKeys.cs și recompilare, licențele emise ar fi "
                + "refuzate de ERP (verificarea 2 din 4.2).";
            return;
        }

        if (keyStore.MatchesPublicKey(current, material))
        {
            MatchText = "Cheia privată de pe acest calculator corespunde cheii publice din program.";
            WarningText = null;
            return;
        }

        MatchText = "Cheia privată NU corespunde cheii publice din program.";
        WarningText = "Licențele emise acum ar fi refuzate de ERP. Verificați textul lipit în LicenseKeys.cs (3.5).";
    }
}
