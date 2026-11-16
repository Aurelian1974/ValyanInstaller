using System.Runtime.Versioning;
using System.Windows;

namespace Erp.LicenseGen.Views;

/// <summary>Cererea parolei pentru copia de siguranță a cheii private (3.5).</summary>
[SupportedOSPlatform("windows")]
public partial class BackupPasswordWindow : Window
{
    private const int MinimumLength = 12;

    public BackupPasswordWindow() => InitializeComponent();

    /// <summary>Parola introdusă, disponibilă doar după „Salvează copia”.</summary>
    public string? Password { get; private set; }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var first = FirstBox.Password;
        var second = SecondBox.Password;

        if (first.Length < MinimumLength)
        {
            MessageText.Text = $"Parola are cel puțin {MinimumLength} caractere.";
            OkButton.IsEnabled = false;
            return;
        }

        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            MessageText.Text = "Cele două parole nu se potrivesc.";
            OkButton.IsEnabled = false;
            return;
        }

        MessageText.Text = string.Empty;
        OkButton.IsEnabled = true;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Password = FirstBox.Password;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
