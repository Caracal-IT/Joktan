using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Joktan.Core.Controls;

/// <summary>
/// Shows a boolean as "Yes" or "No".
/// </summary>
public sealed class YesNoConverter : IValueConverter
{
    public static readonly YesNoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Yes" : "No";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
