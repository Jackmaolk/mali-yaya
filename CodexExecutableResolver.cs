using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexUsageWidget;

public sealed record CodexCandidate(string Path, string Source);
public sealed record CodexInstallation(string ExecutablePath, string Source, string Version);

public sealed record CodexDiscoveryEnvironment(string PathValue, string AppData, string LocalAppData,
    string UserProfile, string ProgramFiles, Architecture Architecture, string? EnvironmentOverride = null,
    string? DesktopHint = null)
{
    public static CodexDiscoveryEnvironment Capture()
    {
        var paths = new[] { Environment.GetEnvironmentVariable("PATH"), ReadPath(EnvironmentVariableTarget.User),
            ReadPath(EnvironmentVariableTarget.Machine) };
        return new(string.Join(';', paths.Where(p => !string.IsNullOrWhiteSpace(p))),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), RuntimeInformation.OSArchitecture,
            Environment.GetEnvironmentVariable("CODEX_WIDGET_CODEX_PATH"),
            Environment.GetEnvironmentVariable("CODEX_CLI_PATH"));
    }

    private static string? ReadPath(EnvironmentVariableTarget target)
    {
        try { return Environment.GetEnvironmentVariable("PATH", target); }
        catch (System.Security.SecurityException) { return null; }
    }
}

public sealed class CodexDiscoveryException(string message) : Exception(message);

public sealed class CodexExecutableResolver
{
    private readonly CodexDiscoveryEnvironment _environment;
    private readonly Func<string, CancellationToken, Task<string?>> _probe;
    private readonly Func<string?, CancellationToken, IReadOnlyList<CodexCandidate>> _scan;
    private readonly TimeSpan _discoveryTimeout;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    public CodexExecutableResolver(CodexDiscoveryEnvironment? environment = null,
        Func<string, CancellationToken, Task<string?>>? probe = null)
    {
        _environment = environment ?? CodexDiscoveryEnvironment.Capture();
        _probe = probe ?? ProbeVersionAsync;
        _scan = FindCandidates;
        _discoveryTimeout = TimeSpan.FromSeconds(12);
    }

    internal CodexExecutableResolver(CodexDiscoveryEnvironment environment,
        Func<string, CancellationToken, Task<string?>> probe,
        Func<string?, CancellationToken, IReadOnlyList<CodexCandidate>> scan, TimeSpan discoveryTimeout,
        TimeProvider? timeProvider = null)
        : this(environment, probe)
    {
        _scan = scan;
        _discoveryTimeout = discoveryTimeout;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal bool IsExplicitSelection(string? configuredPath) =>
        !string.IsNullOrWhiteSpace(configuredPath ?? _environment.EnvironmentOverride);

    public IReadOnlyList<CodexCandidate> FindCandidates(string? configuredPath = null) =>
        FindCandidates(configuredPath, CancellationToken.None);

    private IReadOnlyList<CodexCandidate> FindCandidates(string? configuredPath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new List<CodexCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, string source)
        {
            token.ThrowIfCancellationRequested();
            if (TryNormalizeLocalExecutable(path) is not { } full || !seen.Add(full)) return;
            if (IsNativeExecutable(full, _environment.Architecture)) result.Add(new(full, source));
        }
        var explicitPath = configuredPath ?? _environment.EnvironmentOverride;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            Add(explicitPath, configuredPath is null ? "环境变量指定" : "手动指定");
            return result;
        }
        if (!string.IsNullOrWhiteSpace(_environment.DesktopHint))
            Add(_environment.DesktopHint, "Desktop 提供的 CLI");

        var triples = _environment.Architecture == Architecture.Arm64
            ? new[] { ("arm64", "aarch64-pc-windows-msvc"), ("x64", "x86_64-pc-windows-msvc") }
            : new[] { ("x64", "x86_64-pc-windows-msvc") };
        void Npm(string packageRoot, string source)
        {
            foreach (var (arch, triple) in triples)
            {
                string[] vendors = [
                    Path.Combine(packageRoot, "node_modules", "@openai", "codex-win32-" + arch, "vendor"),
                    Path.GetFullPath(Path.Combine(packageRoot, "..", "codex-win32-" + arch, "vendor")),
                    Path.Combine(packageRoot, "vendor")
                ];
                foreach (var vendor in vendors)
                {
                    Add(Path.Combine(vendor, triple, "bin", "codex.exe"), source);
                    Add(Path.Combine(vendor, triple, "codex", "codex.exe"), source);
                }
            }
        }
        foreach (var raw in _environment.PathValue.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            token.ThrowIfCancellationRequested();
            var directory = NormalizeLocalDirectory(raw.Trim().Trim('"'));
            if (directory is null) continue;
            Add(Path.Combine(directory, "codex.exe"), "PATH 中的 CLI");
            // Resolve npm shims to their native payload; never execute .cmd/.ps1/.js.
            Npm(Path.Combine(directory, "node_modules", "@openai", "codex"), "PATH 中的 npm CLI");
        }
        if (NormalizeLocalDirectory(_environment.LocalAppData) is { } local)
        {
            Add(Path.Combine(local, "Programs", "OpenAI", "Codex", "bin", "codex.exe"), "独立安装版 CLI");
            var desktop = Path.Combine(local, "OpenAI", "Codex");
            Add(Path.Combine(desktop, "bin", "codex.exe"), "Desktop 自带 CLI");
            foreach (var version in Subdirectories(Path.Combine(desktop, "bin"), "*", token))
                Add(Path.Combine(version, "codex.exe"), "Desktop 自带 CLI");
            Add(Path.Combine(local, "Programs", "Codex", "resources", "codex.exe"), "Desktop 自带 CLI");
        }
        if (NormalizeLocalDirectory(_environment.AppData) is { } appData)
            Npm(Path.Combine(appData, "npm", "node_modules", "@openai", "codex"), "npm 全局 CLI");
        if (NormalizeLocalDirectory(_environment.ProgramFiles) is { } programs)
        {
            Npm(Path.Combine(programs, "nodejs", "node_modules", "@openai", "codex"), "系统 npm CLI");
            Add(Path.Combine(programs, "OpenAI", "Codex", "resources", "codex.exe"), "Desktop 自带 CLI");
            foreach (var package in Subdirectories(Path.Combine(programs, "WindowsApps"), "OpenAI.Codex_*", token))
                Add(Path.Combine(package, "app", "resources", "codex.exe"), "Store Desktop 自带 CLI");
        }
        if (NormalizeLocalDirectory(_environment.UserProfile) is { } home)
        {
            foreach (var editor in new[] { ".vscode", ".vscode-insiders", ".cursor", ".windsurf" })
            foreach (var extension in Subdirectories(Path.Combine(home, editor, "extensions"), "openai.chatgpt-*", token))
            {
                if (_environment.Architecture == Architecture.Arm64)
                    Add(Path.Combine(extension, "bin", "windows-aarch64", "codex.exe"), "IDE 扩展自带 CLI");
                Add(Path.Combine(extension, "bin", "windows-x86_64", "codex.exe"), "IDE 扩展自带 CLI");
            }
        }
        return result;
    }

    public async Task<CodexInstallation> ResolveAsync(string? configuredPath = null,
        CancellationToken cancellationToken = default)
    {
        await foreach (var installation in ResolveCandidatesAsync(configuredPath, cancellationToken))
            return installation;
        throw MissingInstallation(configuredPath);
    }

    internal async IAsyncEnumerable<CodexInstallation> ResolveCandidatesAsync(string? configuredPath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(_discoveryTimeout, _timeProvider);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = budget.Token;
        var remaining = _discoveryTimeout;
        var segmentStarted = _timeProvider.GetTimestamp();
        IReadOnlyList<CodexCandidate> candidates;
        try
        {
            // Waiting is bounded even if an OS file operation cannot itself be interrupted.
            candidates = await Task.Run(() => _scan(configuredPath, token), token).WaitAsync(token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw MissingInstallation(configuredPath);
        }
        foreach (var candidate in candidates.Take(IsExplicitSelection(configuredPath) ? 1 : 8))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? output;
            try
            {
                token.ThrowIfCancellationRequested();
                output = await _probe(candidate.Path, token).WaitAsync(token);
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                throw new CodexDiscoveryException("无法访问已发现的 Codex CLI，请检查文件权限或手动选择可访问的 codex.exe。");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { continue; }
            if (output is null) continue;
            var version = Regex.Match(output,
                @"(?m)^codex-cli (?<version>[0-9]+\.[0-9]+\.[0-9A-Za-z.+-]{1,64})\s*$");
            if (version.Success)
            {
                remaining -= _timeProvider.GetElapsedTime(segmentStarted);
                // The caller's bounded handshake must not consume the scan/version budget.
                deadline.CancelAfter(Timeout.InfiniteTimeSpan);
                yield return new(candidate.Path, candidate.Source, version.Groups["version"].Value);
                cancellationToken.ThrowIfCancellationRequested();
                if (remaining <= TimeSpan.Zero) break;
                segmentStarted = _timeProvider.GetTimestamp();
                deadline.CancelAfter(remaining);
            }
            if (IsExplicitSelection(configuredPath)) break;
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal CodexDiscoveryException MissingInstallation(string? configuredPath) =>
        new(IsExplicitSelection(configuredPath)
            ? "指定的 Codex CLI 不可用；请右键刷新按钮，重新选择 codex.exe 或恢复自动识别。"
            : "未找到可用 Codex CLI；请安装 CLI，或右键刷新按钮手动选择 codex.exe。支持 npm、独立版及可访问的 Desktop/IDE 安装。" );

    public static string? TryNormalizeLocalExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var value = path.Trim().Trim('"');
        if (!value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        return NormalizeLocalDirectory(value);
    }

    private static string? NormalizeLocalDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains('\0')) return null;
        try
        {
            var full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal) ||
                new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network) return null;
            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or
            UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }

    public static bool IsNativeExecutable(string path, Architecture architecture)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 128) return false;
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt16() != 0x5A4D) return false;
            stream.Position = 0x3C;
            var offset = reader.ReadInt32();
            if (offset < 64 || offset > 1024 * 1024 || offset > stream.Length - 94) return false;
            stream.Position = offset;
            if (reader.ReadUInt32() != 0x00004550) return false;
            var machine = reader.ReadUInt16();
            if (!(architecture is Architecture.X64 or Architecture.Arm64 && machine == 0x8664 ||
                architecture == Architecture.Arm64 && machine == 0xAA64)) return false;
            stream.Position = offset + 24;
            if (reader.ReadUInt16() != 0x20B) return false;
            stream.Position = offset + 24 + 68;
            return reader.ReadUInt16() == 3; // Console subsystem, not the Desktop GUI executable.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or System.Security.SecurityException) { return false; }
    }

    private static string[] Subdirectories(string root, string pattern, CancellationToken token)
    {
        try
        {
            var directories = new List<(string Path, DateTime Modified)>();
            foreach (var directory in Directory.EnumerateDirectories(root, pattern, SearchOption.TopDirectoryOnly).Take(64))
            {
                token.ThrowIfCancellationRequested();
                directories.Add((directory, Directory.GetLastWriteTimeUtc(directory)));
            }
            return directories.OrderByDescending(p => p.Modified)
                .ThenByDescending(p => p.Path, StringComparer.OrdinalIgnoreCase).Select(p => p.Path).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return []; }
    }

    private static async Task<string?> ProbeVersionAsync(string executable, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        info.ArgumentList.Add("--version");
        using var process = Process.Start(info);
        if (process is null) return null;
        try
        {
            process.StandardInput.Close();
            var stdout = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var stderr = ReadBoundedAsync(process.StandardError, timeout.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
            return process.ExitCode == 0 ? await stdout : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[512];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (text.Length + count > 16 * 1024) throw new InvalidDataException("CLI version output too large.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
