using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ModbusLogger.App;

/// <summary>Prazen/null niz -> Collapsed, sicer Visible. Uporabljeno za prikaz sporočil o napaki.</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public static readonly EmptyToCollapsedConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true -> Visible, false -> Collapsed.</summary>
public sealed class BoolToVisibleConverter : IValueConverter
{
    public static readonly BoolToVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true -> Collapsed, false -> Visible (nasprotje BoolToVisibleConverter).</summary>
public sealed class InverseBoolToVisibleConverter : IValueConverter
{
    public static readonly InverseBoolToVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
