using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Globalization;
using System.Windows;

namespace CodexUsageWidget;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args is ["--diagnose-build"])
        {
            Console.WriteLine(JsonSerializer.Serialize(new { product = "mali-yaya", version = WidgetRuntimeStatus.BuildVersion,
                platform = "win-x64", runtimeMajor = Environment.Version.Major }));
            Shutdown(0);
            return;
        }
        if (e.Args is ["--apply-widget-update", var planFile])
        {
            var result = Task.Run(() => WidgetUpdateInstaller.RunAsync(planFile)).GetAwaiter().GetResult();
            Shutdown(result);
            return;
        }
        if (e.Args is ["--diagnose-cli"] or ["--diagnose-connection"])
        {
            try
            {
                var report = Task.Run(async () =>
                {
                    if (e.Args[0] == "--diagnose-cli")
                        return (object)await new CodexExecutableResolver().ResolveAsync(CodexConnectionSettings.LoadPath());
                    await using var client = new CodexRateLimitClient(CodexConnectionSettings.LoadPath());
                    var received = false;
                    var status = "未收到限额数据";
                    client.SnapshotUpdated += _ => received = true;
                    client.ConnectionStatusChanged += message => status = message;
                    await client.StartAsync();
                    await client.RefreshAsync();
                    if (!received) throw new CodexDiscoveryException(status);
                    return new { connected = true, installation = client.Installation, quotaReceived = true };
                }).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(report));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { error = CodexRateLimitClient.DescribeConnectionError(ex) }));
                Shutdown(1);
            }
            return;
        }
        try
        {
            if (WidgetUpdateInstaller.RestartAfterInterruptedUpdate()) { Shutdown(0); return; }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(ChineseUi ? "组件更新中断，自动恢复未能完成。请保留更新备份并联系维护者；不会重启 Codex。" :
                "An interrupted widget update could not be recovered. Keep the update backups and contact the maintainer. Codex will not be restarted.",
                ChineseUi ? "码力芽芽更新恢复" : "Mali Yaya update recovery");
            Shutdown(2);
            return;
        }
        _singleInstanceMutex = new Mutex(true, "Local\\CodexUsageWidget", out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            if (!IsCurrentBuildRunning()) RequestUpdatedRuntime();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        // Only the normal, mutex-owning launch creates a window. Diagnostics and
        // duplicate instances must not start timers or overwrite runtime state.
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private static bool IsCurrentBuildRunning()
    {
        try
        {
            using var state = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "widget-runtime.json")));
            var root = state.RootElement;
            if (!root.TryGetProperty("assemblyId", out var assemblyId) ||
                assemblyId.GetGuid() != typeof(App).Assembly.ManifestModule.ModuleVersionId ||
                !root.TryGetProperty("processId", out var pid) ||
                !root.TryGetProperty("running", out var running) || !running.GetBoolean()) return false;
            using var process = Process.GetProcessById(pid.GetInt32());
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                                      ArgumentException or InvalidOperationException or FormatException or
                                      System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void RequestUpdatedRuntime()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        var helper = Path.Combine(root, "restart-widget.exe");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(helper)
            {
                Arguments = $"{Environment.ProcessId} {typeof(App).Assembly.ManifestModule.ModuleVersionId:D}",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(ChineseUi ? "新版已准备，请关闭当前小组件后再打开。" : "The new version is ready. Close the current widget, then open it again.",
                ChineseUi ? "码力芽芽" : "Mali Yaya");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
    private static bool ChineseUi => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
}
