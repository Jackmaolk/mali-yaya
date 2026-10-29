namespace CodexUsageWidget;

public static class UsageWindowSelector
{
    private const long FiveHourWindowMinutes = 5 * 60;
    private const long WeeklyWindowMinimumMinutes = 6 * 24 * 60;

    public static RateLimitWindow? FindFiveHour(RateLimitSnapshot snapshot) => GetWindows(snapshot)
        .Where(window => window.WindowDurationMins is >= FiveHourWindowMinutes - 5 and <= FiveHourWindowMinutes + 5)
        .OrderBy(window => Math.Abs(window.WindowDurationMins!.Value - FiveHourWindowMinutes))
        .FirstOrDefault();

    public static RateLimitWindow? FindWeekly(RateLimitSnapshot snapshot)
    {
        var windows = GetWindows(snapshot);

        return windows
            .Where(window => window.WindowDurationMins is >= WeeklyWindowMinimumMinutes)
            .OrderByDescending(window => window.WindowDurationMins)
            .FirstOrDefault();
    }

    public static string DescribeCapabilities(RateLimitSnapshot snapshot)
    {
        var capabilities = new List<string>();
        if (FindFiveHour(snapshot) is not null) capabilities.Add("5小时限额");
        if (FindWeekly(snapshot) is not null) capabilities.Add("每周限额");
        if (capabilities.Count == 0) capabilities.Add("暂无限额窗口");
        capabilities.Add(snapshot.ResetOpportunityReported
            ? $"重置{snapshot.ResetOpportunityCount ?? 0}次"
            : "重置未上报");
        return string.Join(" + ", capabilities);
    }

    private static RateLimitWindow[] GetWindows(RateLimitSnapshot snapshot) =>
        new[] { snapshot.Primary, snapshot.Secondary }
            .Where(window => window is not null)
            .Cast<RateLimitWindow>()
            .ToArray();
}
