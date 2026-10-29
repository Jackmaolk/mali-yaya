using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CodexUsageWidget;

public sealed record CountdownCheckpoint(long ResetsAt, DateTimeOffset ConfirmedAt);

public static class WidgetRuntimeStatus
{
    public const string BuildVersion = "2026.10.28.5";

    public static void Write(RateLimitSnapshot? snapshot, TiboSignal? signal, bool running = true,
        string? chineseBrief = null, string? translationProvider = null,
        string? translationSourceUrl = null, string? fiveHourCountdown = null,
        long? clockResetsAt = null, DateTimeOffset? clockConfirmedAtUtc = null)
    {
        // Readiness must not depend on an account, a quota response, or public-post translation.
        try
        {
            WidgetDataPaths.WriteJsonAtomic(WidgetDataPaths.RuntimeStatusPath(), new
            {
                buildVersion = BuildVersion,
                moduleId = typeof(App).Assembly.ManifestModule.ModuleVersionId,
                processId = Environment.ProcessId,
                running,
                updatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex) when (WidgetDataPaths.IsStorageError(ex))
        {
            Debug.WriteLine("Runtime health status unavailable.");
        }
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
            WidgetDataPaths.WriteJsonAtomic(WidgetDataPaths.AccountRuntimePath(), data);
        }
        catch (Exception ex) when (WidgetDataPaths.IsStorageError(ex))
        {
            Debug.WriteLine("Account runtime status unavailable.");
        }
    }

    public static CountdownCheckpoint? ReadCountdownCheckpoint(string? path = null)
    {
        // No persisted account binding exists yet. Only explicit synthetic/fixture paths may restore a clock.
        if (path is null) return null;
        try
        {
            const int maximumBytes = 2 * 1024 * 1024;
            using var document = JsonDocument.Parse(WidgetDataPaths.ReadBytes(path, maximumBytes));
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
        catch (Exception ex) when (WidgetDataPaths.IsStorageError(ex) || ex is JsonException)
        {
            Debug.WriteLine($"Countdown checkpoint unavailable: {ex.Message}");
            return null;
        }
    }
}
