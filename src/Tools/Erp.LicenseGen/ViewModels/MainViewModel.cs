using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erp.LicenseGen.Services;
using Erp.Licensing;
using Erp.Licensing.Generator;

namespace Erp.LicenseGen.ViewModels;

/// <summary>
/// Ecranul „Licență nouă” (3.2). Toată logica stă în <see cref="LicenseDraft"/>,
/// <see cref="LicenseRegistry"/> și <see cref="LicenseKeyStore"/>, care se testează unitar;
/// aici rămân doar legarea la câmpuri și comenzile.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDialogService dialogs;
    private readonly LicenseRegistry registry;
    private readonly LicenseKeyStore keyStore;
    private readonly ILicenseReader reader;
    private readonly TimeProvider timeProvider;

    [ObservableProperty]
    private string customerName = string.Empty;

    [ObservableProperty]
    private string fiscalCodesText = string.Empty;

    [ObservableProperty]
    private string licenseNumberText = string.Empty;

    [ObservableProperty]
    private DateTime? validUntil;

    [ObservableProperty]
    private bool isUnlimited;

    [ObservableProperty]
    private int users = 100;

    [ObservableProperty]
    private int payrollEmployees = 300;

    [ObservableProperty]
    private int posTerminals = 10;

    [ObservableProperty]
    private string normalizedFiscalCodes = string.Empty;

    [ObservableProperty]
    private string fiscalCodesSummary = "Niciun CUI valid";

    [ObservableProperty]
    private string kindLabel = "—";

    [ObservableProperty]
    private string? errorText;

    [ObservableProperty]
    private string? warningText;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private bool canGenerate;

    public MainViewModel(
        IDialogService dialogs,
        LicenseRegistry registry,
        LicenseKeyStore keyStore,
        ILicenseReader reader,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(reader);

        this.dialogs = dialogs;
        this.registry = registry;
        this.keyStore = keyStore;
        this.reader = reader;
        this.timeProvider = timeProvider ?? TimeProvider.System;

        Modules = [.. LicenseModules.All.Select(m =>
            new ModuleItem(m.Key, m.Value, isMandatory: string.Equals(m.Key, LicenseModules.Mandatory, StringComparison.Ordinal)))];

        foreach (var module in Modules)
        {
            module.PropertyChanged += (_, _) => Revalidate();
        }

        var today = Today;
        validUntil = LicenseDraft.DefaultValidUntil(today).ToDateTime(TimeOnly.MinValue);
        licenseNumberText = registry.NextNumber(today.Year).ToString();
        RefreshKeyStatus();
        Revalidate();
    }

    /// <summary>Cele 14 module din 3.2, cu Nucleul blocat.</summary>
    public ObservableCollection<ModuleItem> Modules { get; }

    /// <summary>Textul din bara de jos: cheia de semnare și numărul de licențe emise (3.2).</summary>
    [ObservableProperty]
    private string keyStatusText = string.Empty;

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    /// <summary>„Generează licența…” (3.2): verifică, cere calea, scrie fișierul și registrul.</summary>
    [RelayCommand]
    private void Generate()
    {
        var validation = BuildDraft().Validate(Today);
        if (validation.Content is not { } content)
        {
            ErrorText = string.Join("\n", validation.Errors.Select(e => e.Text));
            return;
        }

        if (keyStore.CurrentKeyId is not { } keyId)
        {
            dialogs.ShowError(
                "Lipsesc cheile",
                "Pe acest calculator nu există nicio cheie de semnare. Apăsați „Chei” și generați perechea de chei (3.5).");
            return;
        }

        if (!LicenseKeys.Ring.TryGet(keyId, out var material))
        {
            dialogs.ShowError(
                "Cheile nu sunt în program",
                $"Cheia {keyId.ToString(CultureInfo.InvariantCulture)} există pe acest calculator, dar nu e înscrisă în LicenseKeys.cs. "
                + "Apăsați „Chei”, copiați textul C# afișat în LicenseKeys.cs și recompilați generatorul (3.5).");
            return;
        }

        if (!keyStore.MatchesPublicKey(keyId, material))
        {
            dialogs.ShowError(
                "Cheile nu se potrivesc",
                "Cheia privată de pe acest calculator nu corespunde cheii publice din program. "
                + "Licențele emise ar fi refuzate de ERP. Verificați textul lipit în LicenseKeys.cs (3.5).");
            return;
        }

        var suggested = LicenseFileName.Suggest(content.CustomerName, LicenseNumber.TryParse(content.LicenseId, out var n) ? n : default);
        var path = dialogs.SaveFile("Salvați licența", suggested, "Licență ERP (*.lic)|*.lic");
        if (path is null)
        {
            return;
        }

        try
        {
            using var signingKey = keyStore.LoadSigningKey(keyId);
            var file = new LicenseWriter(signingKey, material).Write(content);

            File.WriteAllBytes(path, file);

            // Verificarea imediată: licența scrisă trebuie să se citească (4.2).
            var check = reader.Read(file);
            if (!check.IsValid)
            {
                dialogs.ShowError("Licența nu se verifică", check.Message!);
                return;
            }

            registry.Append(LicenseRegistryEntry.FromLicense(content, Path.GetFileName(path)));
            RefreshKeyStatus();

            StatusText = $"Licența {content.LicenseId} a fost salvată: {path}";
            LicenseNumberText = registry.NextNumber(Today.Year).ToString();
            Revalidate();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialogs.ShowError("Licența nu s-a putut salva", ex.Message);
        }
    }

    /// <summary>„Deschide o licență” (3.2): verifică și afișează conținutul unui fișier existent.</summary>
    [RelayCommand]
    private void OpenLicense()
    {
        var path = dialogs.OpenFile("Deschideți o licență", "Licență ERP (*.lic)|*.lic");
        if (path is null)
        {
            return;
        }

        byte[] file;
        try
        {
            file = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialogs.ShowError("Fișierul nu s-a putut citi", ex.Message);
            return;
        }

        var result = reader.Read(file);
        if (result.License is null)
        {
            dialogs.ShowError("Licență respinsă", result.Message!);
            return;
        }

        var license = result.License;
        var text = string.Join(
            Environment.NewLine,
            $"Număr licență: {license.LicenseId}",
            $"Client: {license.CustomerName}",
            $"Tip: {license.KindLabel}",
            $"CUI-uri: {string.Join(", ", license.FiscalCodeList.Select(FiscalCode.ToDisplay))}",
            $"Module: {string.Join(", ", license.Modules.Select(LicenseModules.DisplayName))}",
            $"Limite: {license.Limits.Users} utilizatori, {license.Limits.PayrollEmployees} angajați, {license.Limits.PosTerminals} case POS",
            $"Emisă la: {license.IssuedAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}",
            $"Valabilă până la: {(license.ValidUntil is { } u ? u.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "Nelimitat")}",
            result.IsValid ? string.Empty : Environment.NewLine + result.Message);

        dialogs.ShowInformation("Licență", text);
    }

    /// <summary>Reia starea cheilor și numărul licenței după închiderea ferestrei „Chei”.</summary>
    public void RefreshAfterKeyChange()
    {
        RefreshKeyStatus();
        LicenseNumberText = registry.NextNumber(Today.Year).ToString();
        Revalidate();
    }

    /// <summary>„Registrul licențelor” (3.6).</summary>
    [RelayCommand]
    private void ShowRegistry() => dialogs.OpenInShell(registry.FilePath);

    private void Revalidate()
    {
        var validation = BuildDraft().Validate(Today);

        NormalizedFiscalCodes = validation.FiscalCodes.NormalizedList;
        FiscalCodesSummary = validation.FiscalCodesSummary;
        KindLabel = validation.FiscalCodes.Accepted.Count switch
        {
            0 => "—",
            1 => "Firmă (limita de firme = 1)",
            var n => $"Grup de {n.ToString(CultureInfo.InvariantCulture)} firme (limita de firme = {n.ToString(CultureInfo.InvariantCulture)})",
        };

        ErrorText = validation.Errors.Count == 0 ? null : string.Join("\n", validation.Errors.Select(e => e.Text));
        WarningText = validation.Warnings.Count == 0 ? null : string.Join("\n", validation.Warnings.Select(w => w.Text));
        CanGenerate = validation.CanGenerate;
        GenerateCommand.NotifyCanExecuteChanged();
    }

    private LicenseDraft BuildDraft()
    {
        var number = LicenseNumber.TryParse(LicenseNumberText, out var parsed) ? parsed : default;

        return new LicenseDraft
        {
            CustomerName = CustomerName,
            FiscalCodesText = FiscalCodesText,
            Modules = [.. Modules.Where(m => m.IsSelected).Select(m => m.Code)],
            Users = Users,
            PayrollEmployees = PayrollEmployees,
            PosTerminals = PosTerminals,
            IsUnlimited = IsUnlimited,
            ValidUntil = ValidUntil is { } date ? DateOnly.FromDateTime(date) : null,
            Number = number,
        };
    }

    private void RefreshKeyStatus()
    {
        var count = registry.Count;
        var licenses = count == 1 ? "1 licență emisă" : $"{count.ToString(CultureInfo.InvariantCulture)} licențe emise";

        KeyStatusText = keyStore.CurrentKeyId is { } keyId
            ? $"Cheia de semnare {keyId.ToString(CultureInfo.InvariantCulture)} · {licenses}"
            : $"Fără cheie de semnare pe acest calculator · {licenses}";
    }

    partial void OnCustomerNameChanged(string value) => Revalidate();

    partial void OnFiscalCodesTextChanged(string value) => Revalidate();

    partial void OnLicenseNumberTextChanged(string value) => Revalidate();

    partial void OnValidUntilChanged(DateTime? value) => Revalidate();

    partial void OnIsUnlimitedChanged(bool value) => Revalidate();

    partial void OnUsersChanged(int value) => Revalidate();

    partial void OnPayrollEmployeesChanged(int value) => Revalidate();

    partial void OnPosTerminalsChanged(int value) => Revalidate();
}
