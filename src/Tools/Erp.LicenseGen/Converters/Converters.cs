using System.Globalization;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Data;

namespace Erp.LicenseGen.Converters;

/// <summary>Inversează o valoare logică (câmpul „Valabilă până la” vs. „Nelimitat”).</summary>
[SupportedOSPlatform("windows")]
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag && !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag && !flag;
}

/// <summary>Ascunde elementul când valoarea e <c>null</c>.</summary>
[SupportedOSPlatform("windows")]
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Ascunde elementul când textul e gol.</summary>
[SupportedOSPlatform("windows")]
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
