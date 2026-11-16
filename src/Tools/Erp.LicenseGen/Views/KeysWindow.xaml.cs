using System.Runtime.Versioning;
using System.Windows;
using Erp.LicenseGen.ViewModels;

namespace Erp.LicenseGen.Views;

/// <summary>Fereastra „Chei” (3.5).</summary>
[SupportedOSPlatform("windows")]
public partial class KeysWindow : Window
{
    public KeysWindow() => InitializeComponent();

    private void OnBackupClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KeysViewModel model)
        {
            return;
        }

        var dialog = new BackupPasswordWindow { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Password is { Length: > 0 } password)
        {
            model.ExportBackup(password);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
