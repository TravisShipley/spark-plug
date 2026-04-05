using System;

public static class ResourceTextFormatter
{
    public static string FormatResource(ResourceDefinition definition, double value)
    {
        var style = NormalizeId(definition?.format?.style);
        var symbol = definition?.format?.symbol ?? string.Empty;

        switch (style)
        {
            case "currency":
                return FormatCurrency(symbol, value);
            case "suffix":
                return Format.Abbreviated(value) + symbol;
            case "plain":
            default:
                return string.IsNullOrEmpty(symbol)
                    ? Format.Plain(value)
                    : $"{Format.Plain(value)} {symbol}";
        }
    }

    private static string FormatCurrency(string symbol, double value)
    {
        if (value < 0d)
            return "-" + FormatCurrency(symbol, -value);

        var normalizedSymbol = string.IsNullOrEmpty(symbol) ? "$" : symbol;
        if (value < 1000d)
        {
            var truncated = Math.Truncate(value * 100d) / 100d;
            return normalizedSymbol
                + truncated.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        return normalizedSymbol + Format.Abbreviated(value);
    }

    private static string NormalizeId(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}
