namespace CodexUsageWidget;

public static class FiveHourCountdownPolicy
{
    public static string Format(RateLimitWindow? window, DateTimeOffset now)
    {
        if (window is null) return "暂无限额";
        if (window.ResetsAt is not long reset || !IsValidReset(reset, now)) return "等待同步";
        var remaining = DateTimeOffset.FromUnixTimeSeconds(reset) - now;
        var seconds = (long)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
        return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }

    public static bool IsValidReset(long? reset, DateTimeOffset now)
    {
        if (reset is not long seconds) return false;
        try
        {
            return (DateTimeOffset.FromUnixTimeSeconds(seconds) - now).TotalHours < 100;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}
