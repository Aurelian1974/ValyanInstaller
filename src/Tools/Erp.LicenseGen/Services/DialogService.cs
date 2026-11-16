using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using Microsoft.Win32;

namespace Erp.LicenseGen.Services;

/// <summary>Implementarea WPF a dialogurilor.</summary>
[SupportedOSPlatform("windows")]
public sealed class DialogService : IDialogService
{
    public string? SaveFile(string title, string suggestedFileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = suggestedFileName,
            Filter = filter,
            AddExtension = true,
            OverwritePrompt = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowInformation(string title, string message)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowError(string title, string message)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string title, string question)
        => MessageBox.Show(question, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    public void CopyToClipboard(string text) => Clipboard.SetText(text);

    public void OpenInShell(string path)
    {
        if (!File.Exists(path))
        {
            ShowInformation("Registrul licențelor", "Nu s-a emis încă nicio licență pe acest calculator.");
            return;
        }

        // Fără shell și fără concatenare de text: calea merge ca argument (R1).
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(path) { UseShellExecute = true },
        };
        process.Start();
    }
}
