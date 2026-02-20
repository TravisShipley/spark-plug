using System;

public static class TimeFormat
{
    public static string FormatCountdown(double seconds)
    {
        var clamped = Math.Max(0d, seconds);
        var totalSeconds = (int)Math.Ceiling(clamped);
        var span = TimeSpan.FromSeconds(totalSeconds);
        var totalMinutes = (long)Math.Floor(span.TotalMinutes);
        return $"{totalMinutes:00}:{span.Seconds:00}";
    }

    public static string FormatDuration(long seconds)
    {
        var clamped = Math.Max(0L, seconds);
        var span = TimeSpan.FromSeconds(clamped);

        if (span.TotalHours >= 1d)
            return $"{(long)span.TotalHours}h {span.Minutes}m";

        if (span.TotalMinutes >= 1d)
            return $"{span.Minutes}m {span.Seconds}s";

        return $"{span.Seconds}s";
    }
}
