using System;
using System.Linq;
using System.Windows.Markup;

namespace DeliverySimulator;

public class EnumValuesExtension : MarkupExtension
{
    public Type? EnumType { get; set; }

    public override object? ProvideValue(IServiceProvider _)
    {
        if (EnumType != null && EnumType.IsEnum)
        {
            return Enum.GetNames(EnumType)
                .Select(name => Enum.Parse(EnumType, name))
                .ToList();
        }
        return default;
    }
}