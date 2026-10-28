using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

[assembly: AssemblyTitle("Mali Yaya desktop entry installer")]
[assembly: AssemblyProduct("Mali Yaya / 码力芽芽")]

internal static class InstallDesktopEntry
{
    private static bool Chinese { get { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"; } }
    private static string Text(string zh, string en) { return Chinese ? zh : en; }

    [STAThread]
    private static int Main()
    {
        try
        {
            var root = AppDomain.CurrentDomain.BaseDirectory;
            var source = Path.Combine(root, "publish", "CodexUsageWidget.exe");
            var dll = Path.Combine(root, "publish", "CodexUsageWidget.dll");
            if (!File.Exists(source) || !File.Exists(dll))
                throw new IOException(Text("找不到组件运行文件，请从完整安装目录打开本程序。", "Run this installer from the complete application directory."));
            var supported = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Codex", "tools", "codex-usage-widget", "publish", "CodexUsageWidget.dll");
            if (!string.Equals(Path.GetFullPath(dll), Path.GetFullPath(supported), StringComparison.OrdinalIgnoreCase))
                throw new IOException(Text("此入口安装器仅适用于当前个人安装目录。自定义安装请给实际 EXE 创建桌面快捷方式，不要单独移动 EXE。",
                    "This desktop entry installer is for the personal installation layout only. For a custom install, create a desktop shortcut to the actual EXE; do not move the EXE alone."));
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                throw new IOException(Text("找不到当前用户桌面。", "The current user's Desktop was not found."));
            var destination = Path.Combine(desktop, Chinese ? "码力芽芽.exe" : "Mali Yaya.exe");
            var backup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexUsageWidget", "desktop-entry-backups", Guid.NewGuid().ToString("N"));
            var changed = !File.Exists(destination) || !EqualFiles(source, destination);
            if (changed)
            {
                if (File.Exists(destination)) Backup(destination, backup);
                File.Copy(source, destination, true);
                if (!EqualFiles(source, destination)) throw new IOException(Text("桌面入口校验失败。", "Desktop entry verification failed."));
            }
            // Preserve the explicitly replaced old entry rather than deleting it.
            var old = Path.Combine(desktop, "码力叮当.exe");
            var retired = false;
            if (File.Exists(old))
            {
                Backup(old, backup);
                try { File.Delete(old); retired = true; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            MessageBox.Show(Text("新版桌面入口已就绪：\n", "Your new desktop entry is ready:\n") + destination +
                (File.Exists(old) && !retired ? Text("\n旧入口正在使用，已备份但暂时保留；未停止任何程序。", "\nThe old entry is in use. It was backed up and left in place; no process was stopped.") : ""),
                Text("码力芽芽", "Mali Yaya"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(Text("无法更新桌面入口，未重启电脑或 Codex。\n", "Cannot update the desktop entry. Neither Windows nor Codex was restarted.\n") + ex.Message,
                Text("码力芽芽", "Mali Yaya"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }

    private static void Backup(string path, string directory)
    {
        Directory.CreateDirectory(directory);
        var saved = Path.Combine(directory, Path.GetFileName(path));
        if (!File.Exists(saved)) File.Copy(path, saved, false);
        if (!EqualFiles(path, saved)) throw new IOException("Desktop entry backup verification failed.");
    }

    private static bool EqualFiles(string left, string right)
    {
        if (new FileInfo(left).Length != new FileInfo(right).Length) return false;
        using (var algorithm = SHA256.Create())
        using (var a = File.OpenRead(left))
        using (var b = File.OpenRead(right))
            return Convert.ToBase64String(algorithm.ComputeHash(a)) == Convert.ToBase64String(algorithm.ComputeHash(b));
    }
}
