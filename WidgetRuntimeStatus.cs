using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CodexUsageWidget;

public sealed record CountdownCheckpoint(long ResetsAt, DateTimeOffset ConfirmedAt);

public static class WidgetRuntimeStatus
{
    public const string BuildVersion = "2026.10.28.3";

    public static void Write(RateLimitSnapshot? snapshot, TiboSignal? signal, bool running = true,
        string? chineseBrief = null, string? translationProvider = null,
        string? translationSourceUrl = null, string? fiveHourCountdown = null,
        long? clockResetsAt = null, DateTimeOffset? clockConfirmedAtUtc = null)
    {
        try
        {
            var data = new
            {
                buildVersion = BuildVersion,
                assemblyId = typeof(App).Assembly.ManifestModule.ModuleVersionId,
                processId = Environment.ProcessId,
                updatedAtUtc = DateTimeOffset.UtcNow,
                running,
                weeklyUsedPercent = snapshot is null ? (int?)null : UsageWindowSelector.FindWeekly(snapshot)?.UsedPercent,
                weeklyResetsAt = snapshot is null ? (long?)null : UsageWindowSelector.FindWeekly(snapshot)?.ResetsAt,
                fiveHourResetsAt = snapshot is null ? (long?)null : UsageWindowSelector.FindFiveHour(snapshot)?.ResetsAt,
                fiveHourCountdown,
                clockResetsAt,
                clockConfirmedAtUtc,
                chineseBrief,
                translationProvider,
                translationSourceUrl,
                status = signal?.Kind.ToString(),
                headline = signal?.Headline,
                detail = signal?.Detail
            };
            var path = Path.Combine(AppContext.BaseDirectory, "widget-runtime.json");
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(data));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Runtime status unavailable: {ex.Message}");
        }
    }

    public static CountdownCheckpoint? ReadCountdownCheckpoint(string? path = null)
    {
        try
        {
            path ??= Path.Combine(AppContext.BaseDirectory, "widget-runtime.json");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            const int maximumBytes = 2 * 1024 * 1024;
            if (stream.Length is 0 or > maximumBytes) return null;

            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            // A present null clock explicitly clears the checkpoint, even with legacy data.
            if (!root.TryGetProperty("clockResetsAt", out var reset) &&
                !root.TryGetProperty("fiveHourResetsAt", out reset)) return null;
            if (reset.ValueKind != JsonValueKind.Number || !reset.TryGetInt64(out var resetsAt))
                return null;
            _ = DateTimeOffset.FromUnixTimeSeconds(resetsAt);

            if (!root.TryGetProperty("clockConfirmedAtUtc", out var confirmed) ||
                confirmed.ValueKind == JsonValueKind.Null)
            {
                if (!root.TryGetProperty("updatedAtUtc", out confirmed)) return null;
            }
            if (confirmed.ValueKind != JsonValueKind.String ||
                !confirmed.TryGetDateTimeOffset(out var confirmedAt)) return null;

            return new CountdownCheckpoint(resetsAt, confirmedAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            JsonException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            Debug.WriteLine($"Countdown checkpoint unavailable: {ex.Message}");
            return null;
        }
    }
}
