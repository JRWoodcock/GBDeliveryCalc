using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using GBDeliveryCalc.Core.Model;

namespace GBDeliveryCalc.App.Infrastructure;

/// <summary>Negates a bool (used for paired radio buttons).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
}

/// <summary>Shows enum values with their display labels.</summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        FuelType f => f.Label(),
        TripType t => t.Label(),
        Presentation.AppTheme.FollowWindows => "Follow Windows",
        Presentation.AppTheme a => a.ToString(),
        _ => value?.ToString() ?? "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible when a string is non-empty or a collection has items.</summary>
public sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        string s => string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible,
        int i => i == 0 ? Visibility.Collapsed : Visibility.Visible,
        ICollection c => c.Count == 0 ? Visibility.Collapsed : Visibility.Visible,
        null => Visibility.Collapsed,
        _ => Visibility.Visible,
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
