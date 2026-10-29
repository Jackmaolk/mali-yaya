using System.IO;
using System.Text.Json;

namespace CodexUsageWidget;

public static class CodexConnectionSettings
{
    private static string DefaultPath => WidgetDataPaths.ConnectionSettingsPath;

    public static string? LoadPath(string? settingsFile = null)
    {
        try
        {
            using var document = JsonDocument.Parse(WidgetDataPaths.ReadBytes(settingsFile ?? DefaultPath, 16 * 1024));
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("executablePath", out var path) &&
                path.ValueKind == JsonValueKind.String ? path.GetString() : null;
        }
        catch (Exception ex) when (WidgetDataPaths.IsStorageError(ex) || ex is JsonException or InvalidOperationException)
        { return null; }
    }

    public static void SavePath(string? executable, string? settingsFile = null)
    {
        var full = executable is null ? null : CodexExecutableResolver.TryNormalizeLocalExecutable(executable)
            ?? throw new ArgumentException("请选择本机 Codex 的原生 .exe 文件，不能填写脚本、网络路径或命令参数。", nameof(executable));
        WidgetDataPaths.WriteJsonAtomic(settingsFile ?? DefaultPath, new { schemaVersion = 1, executablePath = full });
    }
}
