using Microsoft.Win32;
using System.IO;

namespace CodexUsageWidget;

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexUsageWidget";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value &&
               value.Contains("CodexUsageWidget.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var launcher = Path.Combine(AppContext.BaseDirectory, "CodexUsageWidget.exe");
        if (!File.Exists(launcher))
            throw new FileNotFoundException("找不到桌面组件启动器", launcher);

        key.SetValue(ValueName, $"\"{launcher}\"");
    }
}
