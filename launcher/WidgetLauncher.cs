using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Globalization;
using System.Reflection;

[assembly: AssemblyTitle("Mali Yaya / 码力芽芽")]
[assembly: AssemblyProduct("Mali Yaya / 码力芽芽")]
[assembly: AssemblyDescription("Codex usage desktop companion")]

internal static class WidgetLauncher
{
    private const string WidgetFileName = "CodexUsageWidget.dll";
    private const int StartupCheckMilliseconds = 1500;
    private static string Text(string chinese, string english)
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? chinese : english;
    }

    private sealed class SearchOptions
    {
        internal string DocumentsRoot = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        internal string DotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        internal string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        internal string SearchPath = Environment.GetEnvironmentVariable("PATH");
    }

    private sealed class Resolution
    {
        internal string AdjacentDll;
        internal string LegacyDll;
        internal string WidgetDll;
        internal string WidgetSource = "Missing";
        internal string DotnetHost;
        internal string DotnetSource = "Missing";

        internal int ExitCode
        {
            get { return WidgetDll == null ? 2 : (DotnetHost == null ? 3 : 0); }
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        var diagnose = Array.Exists(args, delegate(string arg)
        {
            return string.Equals(arg, "--diagnose", StringComparison.OrdinalIgnoreCase);
        });
        try
        {
            var options = new SearchOptions();
            if (!ReadArguments(args, diagnose, options))
            {
                if (diagnose)
                    WriteDiagnosticError("InvalidArguments");
                else
                    ShowError(Text("启动参数无效。路径测试参数只能与 --diagnose 一起使用。", "Invalid arguments. Path test options require --diagnose."));
                return 64;
            }

            var result = Resolve(options);
            if (diagnose)
            {
                WriteDiagnostics(result);
                return result.ExitCode;
            }
            if (result.WidgetDll == null)
            {
                ShowError(Text("找不到小组件主程序 ", "Cannot find ") + WidgetFileName +
                    Text("。\n请完整解压发布包，并将启动器与 DLL 放在同一目录。\n已检查：\n", ".\nExtract the complete release and keep the launcher beside its DLL.\nChecked:\n")
                    + result.AdjacentDll + Text("\n兼容旧桌面启动器的路径：\n", "\nLegacy desktop location:\n") + result.LegacyDll);
                return result.ExitCode;
            }
            if (result.DotnetHost == null)
            {
                ShowError(Text("找不到可用的 dotnet.exe。\n已依次检查 DOTNET_ROOT、Program Files\\dotnet 和 PATH 中的绝对路径，未使用脚本或当前目录。\n请确认所需的 .NET Desktop Runtime 已安装，并检查上述路径配置。",
                    "Cannot find dotnet.exe in DOTNET_ROOT, Program Files\\dotnet or absolute PATH entries.\nInstall the required .NET Desktop Runtime and check these paths."));
                return result.ExitCode;
            }
            return StartWidget(result);
        }
        catch (Exception ex)
        {
            if (diagnose)
                WriteDiagnosticError("ResolutionFailed");
            else
                ShowError(Text("小组件启动失败。\n", "Unable to start Mali Yaya.\n") + ex.Message);
            return 1;
        }
    }

    private static bool ReadArguments(string[] args, bool diagnose, SearchOptions options)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--diagnose", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!diagnose)
                return false;
            var separator = arg.IndexOf('=');
            if (separator < 0)
                return false;
            var name = arg.Substring(0, separator);
            var value = arg.Substring(separator + 1);
            switch (name)
            {
                case "--documents-root": options.DocumentsRoot = value; break;
                case "--dotnet-root": options.DotnetRoot = value; break;
                case "--program-files": options.ProgramFiles = value; break;
                case "--path": options.SearchPath = value; break;
                default: return false;
            }
        }
        return true;
    }

    private static Resolution Resolve(SearchOptions options)
    {
        var result = new Resolution();
        result.AdjacentDll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, WidgetFileName);
        var documents = AbsoluteDirectory(options.DocumentsRoot);
        if (documents != null)
            result.LegacyDll = Path.Combine(documents, "Codex", "tools", "codex-usage-widget", "publish", WidgetFileName);
        if (File.Exists(result.AdjacentDll))
        {
            result.WidgetDll = result.AdjacentDll;
            result.WidgetSource = "Adjacent";
        }
        else if (File.Exists(result.LegacyDll))
        {
            result.WidgetDll = result.LegacyDll;
            result.WidgetSource = "Legacy";
        }

        var currentDirectory = Directory.GetCurrentDirectory();
        result.DotnetHost = HostInDirectory(options.DotnetRoot, currentDirectory);
        if (result.DotnetHost != null)
            result.DotnetSource = "DOTNET_ROOT";
        else
        {
            var programFiles = AbsoluteDirectory(options.ProgramFiles);
            if (programFiles != null)
                result.DotnetHost = HostInDirectory(Path.Combine(programFiles, "dotnet"), currentDirectory);
            if (result.DotnetHost != null)
                result.DotnetSource = "ProgramFiles";
            else
            {
                foreach (var entry in (options.SearchPath ?? string.Empty).Split(';'))
                {
                    result.DotnetHost = HostInDirectory(entry, currentDirectory);
                    if (result.DotnetHost != null)
                    {
                        result.DotnetSource = "PATH";
                        break;
                    }
                }
            }
        }
        return result;
    }

    private static string AbsoluteDirectory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (value.Length > 1 && value[0] == '"' && value[value.Length - 1] == '"')
            value = value.Substring(1, value.Length - 2);
        // Path.IsPathRooted also accepts drive-relative paths (C:foo and \foo).
        var drivePath = value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':'
            && (value[2] == '\\' || value[2] == '/');
        if (!drivePath)
            return null;
        try
        {
            var full = Path.GetFullPath(value);
            if (full.StartsWith("\\\\", StringComparison.Ordinal) || full.StartsWith("//", StringComparison.Ordinal)
                || new DriveInfo(Path.GetPathRoot(full)).DriveType == DriveType.Network)
                return null;
            return full;
        }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (System.Security.SecurityException) { return null; }
    }

    private static string HostInDirectory(string directory, string currentDirectory)
    {
        directory = AbsoluteDirectory(directory);
        if (directory == null || string.Equals(directory.TrimEnd('\\', '/'), currentDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return null;
        var candidate = Path.Combine(directory, "dotnet.exe");
        if (!File.Exists(candidate))
            return null;
        // Inspect only the PE header; never execute candidates during discovery.
        try
        {
            using (var reader = new BinaryReader(File.Open(candidate, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
            {
                if (reader.BaseStream.Length < 64 || reader.ReadUInt16() != 0x5a4d)
                    return null;
                reader.BaseStream.Position = 0x3c;
                var peOffset = reader.ReadInt32();
                if (peOffset < 64 || peOffset > reader.BaseStream.Length - 24)
                    return null;
                reader.BaseStream.Position = peOffset;
                if (reader.ReadUInt32() != 0x00004550)
                    return null;
                reader.BaseStream.Position = peOffset + 22L;
                var characteristics = reader.ReadUInt16();
                return (characteristics & 0x0002) != 0 && (characteristics & 0x2000) == 0 ? candidate : null;
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (System.Security.SecurityException) { return null; }
    }

    private static int StartWidget(Resolution result)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = result.DotnetHost,
            Arguments = "\"" + result.WidgetDll + "\"",
            WorkingDirectory = Path.GetDirectoryName(result.WidgetDll),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (var process = Process.Start(startInfo))
        {
            if (process == null)
                throw new InvalidOperationException(Text("系统未创建小组件进程。", "The widget process was not created."));
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (process.WaitForExit(StartupCheckMilliseconds) && process.ExitCode != 0)
            {
                ShowError(Text("小组件在启动阶段退出，退出码：", "Mali Yaya exited during startup. Exit code: ") + process.ExitCode +
                    Text("。\n请确认发布包完整，且所需的 .NET Desktop Runtime 可用。\n主程序：", ".\nCheck that the release is complete and the required .NET Desktop Runtime is installed.\nApplication: ") + result.WidgetDll +
                    Text("\n宿主：", "\nHost: ") + result.DotnetHost);
                return 1;
            }
            // Keep both discard readers alive for the entire hidden child lifetime.
            process.WaitForExit();
        }
        return 0;
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, Text("码力芽芽启动失败", "Mali Yaya startup failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static void WriteDiagnostics(Resolution result)
    {
        // WinExe has no console code page; write UTF-8 directly to an inherited pipe.
        using (var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
        {
            output.WriteLine("AdjacentDll=" + result.AdjacentDll);
            output.WriteLine("LegacyDll=" + result.LegacyDll);
            output.WriteLine("WidgetDll=" + result.WidgetDll);
            output.WriteLine("WidgetSource=" + result.WidgetSource);
            output.WriteLine("DotnetHost=" + result.DotnetHost);
            output.WriteLine("DotnetSource=" + result.DotnetSource);
        }
    }

    private static void WriteDiagnosticError(string error)
    {
        using (var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
            output.WriteLine("Error=" + error);
    }
}
