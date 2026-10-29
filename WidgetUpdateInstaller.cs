using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace CodexUsageWidget;

internal sealed record WidgetInstallPlan(string TargetDirectory, string PayloadDirectory, string Version,
    int ParentProcessId, DateTime ParentStartedUtc, Dictionary<string, string> PreviousHashes,
    Dictionary<string, string> NewHashes, string DotnetHost);
internal sealed record WidgetRecoveryJournal(string Product, string BackupDirectory, Dictionary<string, string> PreviousHashes);
internal enum WidgetRecoveryResult { None, Busy, Recovered }

internal sealed class WidgetUpdateHandoff : IDisposable
{
    private readonly string _job;
    private readonly Process? _helper;
    private readonly CancellationToken _token;
    private readonly CancellationTokenRegistration _registration;
    private readonly object _sync = new();
    private bool _committed, _canceled, _disposed;

    internal WidgetUpdateHandoff(string job, CancellationToken token, Process? helper = null)
    {
        _job = job; _token = token; _helper = helper;
        _registration = token.Register(Revoke);
    }

    internal bool TryCommit()
    {
        lock (_sync)
        {
            if (_disposed || _canceled || _committed || _token.IsCancellationRequested) return false;
            WidgetDataPaths.WriteJsonAtomic(Path.Combine(_job, "approved"), new { approved = true });
            _committed = true;
        }
        _registration.Dispose();
        return true;
    }

    private void Revoke()
    {
        lock (_sync)
        {
            if (_committed || _canceled) return;
            _canceled = true;
            try { WidgetDataPaths.WriteJsonAtomic(Path.Combine(_job, "revoked"), new { revoked = true }); }
            catch (Exception error) when (WidgetDataPaths.IsStorageError(error))
            { Debug.WriteLine("Update handoff canceled without an approval permit."); }
        }
    }

    public void Dispose()
    {
        Revoke();
        lock (_sync) _disposed = true;
        _registration.Dispose();
        _helper?.Dispose();
    }
}

public static class WidgetUpdateInstaller
{
    internal static readonly string[] RuntimeFiles = ["CodexUsageWidget.dll", "CodexUsageWidget.exe",
        "CodexUsageWidget.deps.json", "CodexUsageWidget.runtimeconfig.json"];
    private const string JournalName = "widget-update-recovery.json";

    internal static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();

    internal static string LocalDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException("Local path required.");
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network ||
            Path.GetPathRoot(full)!.TrimEnd(Path.DirectorySeparatorChar) == full)
            throw new InvalidDataException("Installation directory rejected.");
        for (var item = new DirectoryInfo(full); item is not null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse directory rejected.");
        return full;
    }

    internal static void VerifyFiles(string directory, IReadOnlyDictionary<string, string> hashes)
    {
        LocalDirectory(directory);
        if (hashes is null || hashes.Count != RuntimeFiles.Length || RuntimeFiles.Any(name => !hashes.ContainsKey(name)))
            throw new InvalidDataException("Runtime inventory mismatch.");
        foreach (var name in RuntimeFiles)
        {
            var file = Path.Combine(directory, name);
            if (!File.Exists(file) || new FileInfo(file).Length is <= 0 or > 20 * 1024 * 1024 ||
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 ||
                !string.Equals(Hash(file), hashes[name], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Runtime integrity check failed.");
        }
    }

    internal static async Task<WidgetUpdateHandoff?> LaunchAsync(PreparedWidgetUpdate update, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var target = LocalDirectory(AppContext.BaseDirectory);
        VerifyFiles(update.PayloadDirectory, update.Hashes);
        using var parent = Process.GetCurrentProcess();
        var old = RuntimeFiles.ToDictionary(name => name, name => Hash(Path.Combine(target, name)));
        var job = Path.Combine(Path.GetDirectoryName(LocalDirectory(update.PayloadDirectory))!, "helper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        foreach (var name in RuntimeFiles.Where(name => !name.EndsWith(".exe", StringComparison.Ordinal)))
            File.Copy(Path.Combine(target, name), Path.Combine(job, name));
        var host = Environment.ProcessPath;
        if (host is null || !string.Equals(Path.GetFileName(host), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            host = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        if (!File.Exists(host)) throw new IOException("dotnet host unavailable.");
        var plan = new WidgetInstallPlan(target, update.PayloadDirectory, update.Version, parent.Id,
            parent.StartTime.ToUniversalTime(), old, update.Hashes.ToDictionary(pair => pair.Key, pair => pair.Value), host);
        var planFile = Path.Combine(job, "apply.json");
        File.WriteAllText(planFile, JsonSerializer.Serialize(plan));
        var info = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = job };
        info.ArgumentList.Add(Path.Combine(job, "CodexUsageWidget.dll"));
        info.ArgumentList.Add("--apply-widget-update");
        info.ArgumentList.Add(planFile);
        var helper = Process.Start(info) ?? throw new IOException("Update helper unavailable.");
        var handoff = new WidgetUpdateHandoff(job, token, helper);
        var transferred = false;
        try
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (helper.HasExited) return null;
                if (File.Exists(Path.Combine(job, "ready"))) { transferred = true; return handoff; }
                await Task.Delay(100, token);
            }
            return null;
        }
        finally { if (!transferred) handoff.Dispose(); }
    }

    internal static bool IsApplyApproved(string job)
    {
        try
        {
            if (IsApplyRevoked(job)) return false;
            using var permit = JsonDocument.Parse(WidgetDataPaths.ReadBytes(Path.Combine(job, "approved"), 128));
            var root = permit.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 1 &&
                root.GetProperty("approved").ValueKind == JsonValueKind.True;
        }
        catch (Exception error) when (WidgetDataPaths.IsStorageError(error) ||
            error is JsonException or KeyNotFoundException or InvalidOperationException) { return false; }
    }

    private static bool IsApplyRevoked(string job)
    {
        try { return File.Exists(WidgetDataPaths.LocalFile(Path.Combine(job, "revoked"))); }
        catch (Exception error) when (WidgetDataPaths.IsStorageError(error)) { return true; }
    }

    internal static void ReplaceTransactional(string target, string payload, string backup,
        IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> next, Action<int>? afterCopy = null,
        bool rollbackOnError = true)
    {
        VerifyFiles(target, previous);
        VerifyFiles(payload, next);
        LocalDirectory(backup);
        Directory.CreateDirectory(backup);
        foreach (var name in RuntimeFiles) AtomicCopy(Path.Combine(target, name), Path.Combine(backup, name));
        VerifyFiles(backup, previous);
        WriteJournal(target, new("mali-yaya", backup, previous.ToDictionary(pair => pair.Key, pair => pair.Value)));
        try
        {
            var count = 0;
            // The DLL is last; metadata and launcher are ready before new code is activated.
            foreach (var name in RuntimeFiles.OrderBy(name => name == "CodexUsageWidget.dll" ? 1 : 0))
            {
                AtomicCopy(Path.Combine(payload, name), Path.Combine(target, name));
                afterCopy?.Invoke(++count);
            }
            VerifyFiles(target, next);
        }
        catch when (rollbackOnError)
        {
            Restore(target, backup, previous);
            File.Delete(Path.Combine(target, JournalName));
            throw;
        }
    }

    private static void Restore(string target, string backup, IReadOnlyDictionary<string, string> hashes)
    {
        VerifyFiles(backup, hashes);
        foreach (var name in RuntimeFiles.OrderBy(name => name == "CodexUsageWidget.dll" ? 1 : 0))
            AtomicCopy(Path.Combine(backup, name), Path.Combine(target, name));
        VerifyFiles(target, hashes);
    }

    private static void AtomicCopy(string source, string target)
    {
        var temporary = target + ".incoming-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var input = File.OpenRead(source))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(flushToDisk: true); }
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void WriteJournal(string target, WidgetRecoveryJournal journal)
    {
        var path = Path.Combine(target, JournalName);
        var temporary = path + ".incoming-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(output, journal); output.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static WidgetRecoveryResult RecoverInterruptedFiles(string target)
    {
        target = LocalDirectory(target);
        var path = Path.Combine(target, JournalName);
        if (!File.Exists(path)) return WidgetRecoveryResult.None;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length is <= 0 or > 64 * 1024)
            throw new InvalidDataException("Invalid recovery journal.");
        var lockPath = Path.Combine(target, ".widget-update.lock");
        if (File.Exists(lockPath) && (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Invalid update lock.");
        FileStream lease;
        try { lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return WidgetRecoveryResult.Busy; }
        using (lease)
        {
            if (!File.Exists(path)) return WidgetRecoveryResult.None;
            var journal = JsonSerializer.Deserialize<WidgetRecoveryJournal>(File.ReadAllText(path)) ?? throw new InvalidDataException();
            if (journal.Product != "mali-yaya") throw new InvalidDataException("Wrong recovery product.");
            Restore(target, journal.BackupDirectory, journal.PreviousHashes);
            File.Delete(path);
            return WidgetRecoveryResult.Recovered;
        }
    }

    public static bool RestartAfterInterruptedUpdate()
    {
        var target = LocalDirectory(AppContext.BaseDirectory);
        if (RecoverInterruptedFiles(target) != WidgetRecoveryResult.Recovered) return false;
        var host = Environment.ProcessPath;
        if (host is null || Path.GetFileName(host) != "dotnet.exe")
            host = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        using var restored = StartWidget(host, target);
        return true;
    }

    public static async Task<int> RunAsync(string planFile)
    {
        string? target = null, backup = null;
        WidgetInstallPlan? plan = null;
        Process? replacement = null;
        FileStream? updateLock = null;
        var installed = false;
        var parentExited = false;
        try
        {
            {
                var job = LocalDirectory(AppContext.BaseDirectory);
                if (!string.Equals(Path.GetFullPath(planFile), Path.Combine(job, "apply.json"), StringComparison.OrdinalIgnoreCase) ||
                    new FileInfo(planFile).Length is <= 0 or > 64 * 1024) return 2;
                plan = JsonSerializer.Deserialize<WidgetInstallPlan>(File.ReadAllText(planFile)) ?? throw new InvalidDataException();
                target = LocalDirectory(plan.TargetDirectory);
                if (string.Equals(target, job, StringComparison.OrdinalIgnoreCase) ||
                    !Version.TryParse(plan.Version, out var version) || version <= Version.Parse(WidgetRuntimeStatus.BuildVersion)) return 2;
                VerifyFiles(target, plan.PreviousHashes);
                VerifyFiles(plan.PayloadDirectory, plan.NewHashes);
                if (!string.Equals(Hash(Assembly.GetExecutingAssembly().Location), plan.PreviousHashes["CodexUsageWidget.dll"], StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetFullPath(plan.DotnetHost), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return 2;
                // A file handle is safe across awaits; a Windows mutex is thread-affine.
                var lockPath = Path.Combine(target, ".widget-update.lock");
                if (File.Exists(lockPath) && (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0) return 2;
                updateLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                File.WriteAllText(Path.Combine(job, "ready"), "ready");
                try
                {
                    using var parent = Process.GetProcessById(plan.ParentProcessId);
                    if (parent.StartTime.ToUniversalTime() != plan.ParentStartedUtc) return 2;
                    for (var attempt = 0; !parent.HasExited; attempt++)
                    {
                        if (attempt >= 450 || IsApplyRevoked(job)) return 1;
                        await Task.Delay(100);
                    }
                }
                catch (ArgumentException) { }
                // A ready helper is not permission to install: UI rechecks opt-in before committing.
                if (!IsApplyApproved(job)) return 1;
                parentExited = true;
                backup = Path.Combine(job, "backup");
                ReplaceTransactional(target, plan.PayloadDirectory, backup, plan.PreviousHashes, plan.NewHashes);
                installed = true;
                var started = DateTimeOffset.UtcNow;
                replacement = StartWidget(plan.DotnetHost, target);
                for (var attempt = 0; attempt < 120; attempt++)
                {
                    if (replacement.HasExited) break;
                    if (Ready(target, plan.Version, replacement.Id, started))
                    { File.Delete(Path.Combine(target, JournalName)); return 0; }
                    await Task.Delay(250);
                }
                throw new IOException("Replacement runtime did not become ready.");
            }
        }
        catch (Exception)
        {
            // Only this helper's own child may be stopped; never restart Codex.
            if (replacement is not null && !replacement.HasExited)
            {
                try { replacement.Kill(entireProcessTree: true); await replacement.WaitForExitAsync(); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return 3; }
            }
            if (parentExited && target is not null && plan is not null)
            {
                try
                {
                    if (installed && backup is not null) Restore(target, backup, plan.PreviousHashes);
                    VerifyFiles(target, plan.PreviousHashes);
                    File.Delete(Path.Combine(target, JournalName));
                    using var old = StartWidget(plan.DotnetHost, target);
                }
                catch { return 3; }
            }
            return 1;
        }
        finally { replacement?.Dispose(); updateLock?.Dispose(); }
    }

    private static Process StartWidget(string host, string target)
    {
        var info = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = target };
        info.ArgumentList.Add(Path.Combine(target, "CodexUsageWidget.dll"));
        return Process.Start(info) ?? throw new IOException("Cannot start widget.");
    }

    private static bool Ready(string target, string version, int pid, DateTimeOffset since)
    {
        try
        {
            using var document = JsonDocument.Parse(WidgetDataPaths.ReadBytes(WidgetDataPaths.RuntimeStatusPath(target), 8192));
            var data = document.RootElement;
            return data.GetProperty("buildVersion").GetString() == version && data.GetProperty("running").GetBoolean() &&
                data.GetProperty("moduleId").GetGuid() != Guid.Empty &&
                data.GetProperty("processId").GetInt32() == pid && data.GetProperty("updatedAtUtc").GetDateTimeOffset() >= since;
        }
        catch (Exception ex) when (WidgetDataPaths.IsStorageError(ex) ||
            ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return false; }
    }
}
