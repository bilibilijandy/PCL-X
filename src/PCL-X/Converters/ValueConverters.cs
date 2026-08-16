using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PCL_X.Converters;

public class VersionTypeColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var type = value?.ToString()?.ToLower() ?? "";
        return type switch
        {
            "release" => new SolidColorBrush(Color.Parse("#a6e3a1")),
            "snapshot" => new SolidColorBrush(Color.Parse("#f9e2af")),
            "old_beta" => new SolidColorBrush(Color.Parse("#f38ba8")),
            "old_alpha" => new SolidColorBrush(Color.Parse("#eba0ac")),
            _ => new SolidColorBrush(Color.Parse("#89b4fa"))
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class VersionInstalledColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool installed && installed)
            return new SolidColorBrush(Color.Parse("#a6e3a1"));
        return new SolidColorBrush(Color.Parse("#6c7086"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class VersionInstalledTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool installed && installed)
            return "✓ 已安装";
        return "未安装";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
