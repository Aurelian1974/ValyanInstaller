using System.Globalization;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Markup;
using Erp.LicenseGen.Services;
using Erp.LicenseGen.ViewModels;
using Erp.LicenseGen.Views;
using Erp.Licensing;
using Erp.Licensing.Generator;

namespace Erp.LicenseGen;

/// <summary>Generatorul de licențe, aplicație internă (3.1).</summary>
[SupportedOSPlatform("windows")]
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Interfața e în română (R11): datele și numerele se afișează cu formatul românesc.
        var culture = CultureInfo.GetCultureInfo("ro-RO");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        var dialogs = new DialogService();
        var keyStore = new LicenseKeyStore(LicenseKeyStore.DefaultFolder, new DpapiSecretProtector());
        var registry = new LicenseRegistry(LicenseRegistry.DefaultPath);

        MainWindow = new MainWindow
        {
            DataContext = new MainViewModel(dialogs, registry, keyStore, new LicenseReader()),
        };
        MainWindow.Show();
    }
}
