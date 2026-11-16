using CommunityToolkit.Mvvm.ComponentModel;

namespace Erp.LicenseGen.ViewModels;

/// <summary>O casetă de bifat din lista de module (3.2).</summary>
public sealed partial class ModuleItem : ObservableObject
{
    [ObservableProperty]
    private bool isSelected;

    public ModuleItem(string code, string displayName, bool isMandatory)
    {
        Code = code;
        DisplayName = displayName;
        IsMandatory = isMandatory;
        isSelected = isMandatory;
    }

    public string Code { get; }

    public string DisplayName { get; }

    /// <summary>Nucleul e bifat și blocat (3.2).</summary>
    public bool IsMandatory { get; }

    public bool IsEnabled => !IsMandatory;

    public string Label => IsMandatory ? $"{DisplayName} (obligatoriu)" : DisplayName;
}
