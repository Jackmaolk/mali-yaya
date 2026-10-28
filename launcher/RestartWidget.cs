using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Windows.Forms;

internal static class RestartWidget
{
    private static int Main(string[] args)
    {
        var root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var script = Path.Combine(root, "restart-widget.ps1");
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            int excludeProcessId;
            if (args.Length > 0 && int.TryParse(args[0], out excludeProcessId) && excludeProcessId > 0)
                info.Arguments += " -ExcludeProcessId " + excludeProcessId;
            Guid expectedAssemblyId;
            if (args.Length > 1 && Guid.TryParse(args[1], out expectedAssemblyId))
                info.Arguments += " -ExpectedAssemblyId " + expectedAssemblyId.ToString("D");
            using (var process = Process.Start(info))
            {
                if (process == null) throw new InvalidOperationException("Restart process did not start.");
                process.WaitForExit();
                if (process.ExitCode == 0) return 0;
            }
            throw new InvalidOperationException("See restart-widget.log for details.");
        }
        catch (Exception ex)
        {
            var chinese = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
            MessageBox.Show((chinese ? "小组件重启未完成，请查看程序目录中的 restart-widget.log。\n" :
                "The widget restart did not complete. See restart-widget.log in the application directory.\n") + ex.Message,
                chinese ? "码力芽芽" : "Mali Yaya", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }
}
