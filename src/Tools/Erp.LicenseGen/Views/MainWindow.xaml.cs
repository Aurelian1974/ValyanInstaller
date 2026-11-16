using System.Runtime.Versioning;
using System.Windows;
using Erp.LicenseGen.Services;
using Erp.LicenseGen.ViewModels;
using Erp.Licensing.Generator;

namespace Erp.LicenseGen.Views;

/// <summary>Ecranul generatorului (3.2). Code-behind-ul doar deschide fereastra „Chei”.</summary>
[SupportedOSPlatform("windows")]
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnKeysClick(object sender, RoutedEventArgs e)
    {
        var keyStore = new LicenseKeyStore(LicenseKeyStore.DefaultFolder, new DpapiSecretProtector());
        var window = new KeysWindow
        {
            Owner = this,
            DataContext = new KeysViewModel(new DialogService(), keyStore),
        };

        window.ShowDialog();

        // Starea cheilor se poate fi schimbat: ecranul principal o reia.
        if (DataContext is MainViewModel main)
        {
            main.RefreshAfterKeyChange();
        }
    }
}
