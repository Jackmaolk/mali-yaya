namespace CodexUsageWidget;

public enum UsageAgentLevel
{
    Unknown,
    Healthy,
    Balanced,
    Caution,
    Critical,
    Celebration
}

public sealed record UsageAgentAdvice(
    UsageAgentLevel Level,
    string Title,
    string Message,
    int? RemainingPercent,
    bool IsPaceFast);

public static class UsageAgent
{
    public static UsageAgentAdvice Evaluate(
        RateLimitWindow? weekly,
        int? resetOpportunityCount,
        DateTimeOffset? now = null)
    {
        if (resetOpportunityCount is > 0)
        {
            return new UsageAgentAdvice(
                UsageAgentLevel.Celebration,
                "发现随机重置",
                $"已有 {resetOpportunityCount} 次可用机会，需要时可以放心使用。",
                GetRemaining(weekly),
                false);
        }

        if (weekly is null)
        {
            return new UsageAgentAdvice(
                UsageAgentLevel.Unknown,
                "等待周额度",
                "Codex 暂未返回每周使用限额，保留上次数据。",
                null,
                false);
        }

        var remaining = GetRemaining(weekly) ?? 0;
        var current = now ?? DateTimeOffset.Now;
        var paceFast = IsPaceFast(weekly, current);
        var resetText = FormatRemainingTime(weekly.ResetsAt, current);

        if (remaining <= 10)
            return Advice(UsageAgentLevel.Critical, "额度告急", $"仅保留必要任务，{resetText}。", remaining, true);
        if (remaining <= 30)
            return Advice(UsageAgentLevel.Caution, "优先关键任务", $"本周余量偏低，{resetText}。", remaining, paceFast);
        if (paceFast)
            return Advice(UsageAgentLevel.Caution, "消耗速度偏快", $"建议放慢非必要任务，{resetText}。", remaining, true);
        if (remaining >= 80)
            return Advice(UsageAgentLevel.Healthy, "节奏健康", $"额度充足，可以正常推进，{resetText}。", remaining, false);

        return Advice(UsageAgentLevel.Balanced, "按计划使用", $"当前消耗正常，{resetText}。", remaining, false);
    }

    private static UsageAgentAdvice Advice(
        UsageAgentLevel level,
        string title,
        string message,
        int remaining,
        bool paceFast) => new(level, title, message, remaining, paceFast);

    private static int? GetRemaining(RateLimitWindow? window) =>
        window is null ? null : Math.Clamp(100 - window.UsedPercent, 0, 100);

    private static bool IsPaceFast(RateLimitWindow window, DateTimeOffset now)
    {
        if (window.ResetsAt is not (>= -62135596800 and <= 253402300799) ||
            window.WindowDurationMins is not (> 0 and <= 525600)) return false;

        var end = DateTimeOffset.FromUnixTimeSeconds(window.ResetsAt.Value);
        var duration = TimeSpan.FromMinutes(window.WindowDurationMins.Value);
        if (end.Ticks < duration.Ticks) return false;
        var start = end - duration;
        var elapsedRatio = Math.Clamp((now - start).TotalMinutes / duration.TotalMinutes, 0, 1);
        var expectedUsedPercent = elapsedRatio * 100;
        return window.UsedPercent > expectedUsedPercent + 10;
    }

    private static string FormatRemainingTime(long? resetUnix, DateTimeOffset now)
    {
        if (resetUnix is not (>= -62135596800 and <= 253402300799)) return "重置时间等待上报";
        var remaining = DateTimeOffset.FromUnixTimeSeconds(resetUnix.Value) - now;
        if (remaining <= TimeSpan.Zero) return "即将重置";
        if (remaining.TotalDays >= 1) return $"约 {Math.Floor(remaining.TotalDays)} 天后重置";
        return $"约 {Math.Max(1, Math.Ceiling(remaining.TotalHours))} 小时后重置";
    }
}
