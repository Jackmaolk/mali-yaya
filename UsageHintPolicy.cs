using System.Windows.Media;

namespace CodexUsageWidget;

public readonly record struct UsageHint(string Text, Color Color);

public static class UsageHintPolicy
{
    private static readonly Color Safe = Color.FromRgb(35, 112, 78);
    private static readonly Color Normal = Color.FromRgb(65, 91, 78);
    private static readonly Color Caution = Color.FromRgb(154, 88, 0);
    private static readonly Color Critical = Color.FromRgb(178, 58, 72);

    public static UsageHint ForFiveHour(int remaining) => Math.Clamp(remaining, 0, 100) switch
    {
        >= 80 => new("放心使用", Safe),
        >= 60 => new("节奏正常", Normal),
        >= 40 => new("留意消耗", Normal),
        >= 20 => new("谨慎使用", Caution),
        >= 1 => new("短时额度紧张", Critical),
        _ => new("等待短时重置", Critical)
    };

    public static UsageHint ForWeekly(int remaining) => Math.Clamp(remaining, 0, 100) switch
    {
        >= 80 => new("周余量充足", Safe),
        >= 60 => new("周节奏正常", Normal),
        >= 40 => new("规划后续用量", Normal),
        >= 20 => new("保留关键任务", Caution),
        >= 1 => new("周额度紧张", Critical),
        _ => new("等待周重置", Critical)
    };

    public static UsageHint ForResetOpportunity(int? count) => count switch
    {
        null => new("等待官方上报", Normal),
        <= 0 => new("暂无可用重置", Normal),
        1 => new("太好了 · 可重置1次", Safe),
        _ => new($"感谢 ChatGPT · 可重置{count}次", Safe)
    };
}
