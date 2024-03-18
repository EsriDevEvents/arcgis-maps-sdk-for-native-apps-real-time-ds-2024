using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows;
using System;
using System.Windows.Media;
using DeliveryShared;

namespace DeliveryDashboard;

public class CompanyColorToBrushConverter : MarkupExtension, IValueConverter
{
    private static CompanyColorToBrushConverter? _converter;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        _converter ??= new CompanyColorToBrushConverter();
        return _converter;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CompanyColor companyColor)
            return DependencyProperty.UnsetValue;

        var brush = companyColor switch
        {
            CompanyColor.Red => new SolidColorBrush(Colors.Red),
            CompanyColor.Blue => new SolidColorBrush(Colors.Blue),
            CompanyColor.Green => new SolidColorBrush(Colors.Green),
            CompanyColor.Purple => new SolidColorBrush(Colors.Purple),
            _ => new SolidColorBrush(Colors.Black),
        };
        brush.Opacity = 0.5;
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
