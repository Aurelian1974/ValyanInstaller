namespace Erp.LicenseGen.Services;

/// <summary>
/// Dialogurile sistemului, izolate într-o interfață ca ViewModel-urile să se poată testa
/// fără fereastră (R1).
/// </summary>
public interface IDialogService
{
    /// <summary>Dialogul „Salvează ca”; întoarce <c>null</c> dacă operatorul renunță.</summary>
    string? SaveFile(string title, string suggestedFileName, string filter);

    /// <summary>Dialogul „Deschide”; întoarce <c>null</c> dacă operatorul renunță.</summary>
    string? OpenFile(string title, string filter);

    void ShowInformation(string title, string message);

    void ShowError(string title, string message);

    bool Confirm(string title, string question);

    /// <summary>Copiază text în clipboard (textul C# al cheilor, 3.5).</summary>
    void CopyToClipboard(string text);

    /// <summary>Deschide un fișier cu aplicația implicită (registrul licențelor, 3.6).</summary>
    void OpenInShell(string path);
}
