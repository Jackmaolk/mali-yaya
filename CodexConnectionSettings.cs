using System.IO;
using System.Text.Json;

namespace CodexUsageWidget;

public static class CodexConnectionSettings
{
    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexUsageWidget", "connection-settings.json");

    public static string? LoadPath(string? settingsFile = null)
    {
        try
        {
            using var stream = File.OpenRead(settingsFile ?? DefaultPath);
            if (stream.Length is 0 or > 16 * 1024) return null;
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("executablePath", out var path) &&
                path.ValueKind == JsonValueKind.String ? path.GetString() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or System.Security.SecurityException) { return null; }
    }

    public static void SavePath(string? executable, string? settingsFile = null)
    {
        var full = executable is null ? null : CodexExecutableResolver.TryNormalizeLocalExecutable(executable)
            ?? throw new ArgumentException("请选择本机 Codex 的原生 .exe 文件，不能填写脚本、网络路径或命令参数。", nameof(executable));
        var path = settingsFile ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { schemaVersion = 1, executablePath = full }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
