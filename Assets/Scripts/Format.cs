using System;
using System.Globalization;

public static class Format
{
    private static readonly string[] Suffixes =
    {
        "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc"
    };

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // ----------------------------
    // Public API
    // ----------------------------

    public static string Currency(double value)
    {
        if (value < 0)
            return "-" + Currency(-value);

        // Currency rule: below 1000, always show two decimals (truncated)
        if (value < 1000)
        {
            double truncated = TruncateDecimals(value, 2);
            return "$" + truncated.ToString("0.00", Invariant);
        }

        return "$" + Abbreviated(value);
    }

    public static string Abbreviated(double value, int decimals = 2)
    {
        if (value < 0)
            return "-" + Abbreviated(-value, decimals);

        if (value < 1000)
        {
            // Below 1000: show up to 2 decimals (0.##), truncated (never rounded).
            int d = Math.Min(2, decimals);
            double truncated = TruncateDecimals(value, d);
            return truncated.ToString("0.##", Invariant);
        }

        int magnitude = (int)Math.Floor(Math.Log10(value) / 3);
        magnitude = Math.Min(magnitude, Suffixes.Length - 1);

        double scaled = value / Math.Pow(1000, magnitude);

        int decimalCount = GetDecimalCount(scaled, decimals);
        scaled = TruncateDecimals(scaled, decimalCount);

        return scaled.ToString(x(scaled), Invariant) + Suffixes[magnitude];
    }

    public static string Plain(double value)
        => Math.Truncate(value).ToString("#,0", Invariant);

    public static string Rate(double valuePerSecond)
        => Currency(valuePerSecond) + "/s";

    // ----------------------------
    // Helpers
    // ----------------------------

    private static double TruncateDecimals(double value, int decimals)
    {
        if (decimals <= 0)
            return Math.Truncate(value);

        double factor = Math.Pow(10, decimals);
        return Math.Truncate(value * factor) / factor;
    }

    private static int GetDecimalCount(double value, int maxDecimals)
    {
        if (value >= 100) return 0;
        if (value >= 10) return Math.Min(1, maxDecimals);
        return Math.Min(2, maxDecimals);
    }

    private static string x(double value)
    {
        if (value >= 100) return "0";
        if (value >= 10) return "0.0";
        return "0.00";
    }
}