using System.IO;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class WidgetUpdateInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WidgetInstallerTests", Guid.NewGuid().ToString("N"));

    private Dictionary<string, string> Fixture(string folder, string prefix)
    {
        Directory.CreateDirectory(folder);
        foreach (var name in WidgetUpdateInstaller.RuntimeFiles) File.WriteAllText(Path.Combine(folder, name), prefix + name);
        return WidgetUpdateInstaller.RuntimeFiles.ToDictionary(name => name, name => WidgetUpdateInstaller.Hash(Path.Combine(folder, name)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InterruptedReplacementRestoresAllFilesAndKeepsUserData(int failedAt)
    {
        var target = Path.Combine(_root, "target");
        var payload = Path.Combine(_root, "payload");
        var before = Fixture(target, "old");
        var next = Fixture(payload, "new");
        File.WriteAllText(Path.Combine(target, "widget-runtime.json"), "private-state");
        Assert.Throws<IOException>(() => WidgetUpdateInstaller.ReplaceTransactional(target, payload,
            Path.Combine(_root, "backup"), before, next, count => { if (count == failedAt) throw new IOException("fixture"); }));
        WidgetUpdateInstaller.VerifyFiles(target, before);
        Assert.Equal("private-state", File.ReadAllText(Path.Combine(target, "widget-runtime.json")));
    }

    [Fact]
    public void SuccessfulReplacementTouchesOnlyRuntimeAllowlist()
    {
        var target = Path.Combine(_root, "target");
        var payload = Path.Combine(_root, "payload");
        var before = Fixture(target, "old");
        var next = Fixture(payload, "new");
        File.WriteAllText(Path.Combine(target, "cache.json"), "cache");
        WidgetUpdateInstaller.ReplaceTransactional(target, payload, Path.Combine(_root, "backup"), before, next);
        WidgetUpdateInstaller.VerifyFiles(target, next);
        WidgetUpdateInstaller.VerifyFiles(Path.Combine(_root, "backup"), before);
        Assert.Equal("cache", File.ReadAllText(Path.Combine(target, "cache.json")));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InterruptedProcessCanRecoverOnNextStartup(int stoppedAfter)
    {
        var target = Path.Combine(_root, "target");
        var payload = Path.Combine(_root, "payload");
        var before = Fixture(target, "old");
        var next = Fixture(payload, "new");
        Assert.Throws<IOException>(() => WidgetUpdateInstaller.ReplaceTransactional(target, payload,
            Path.Combine(_root, "backup"), before, next,
            count => { if (count == stoppedAfter) throw new IOException("simulated process exit"); }, rollbackOnError: false));
        Assert.True(File.Exists(Path.Combine(target, "widget-update-recovery.json")));
        Assert.Equal(WidgetRecoveryResult.Recovered, WidgetUpdateInstaller.RecoverInterruptedFiles(target));
        WidgetUpdateInstaller.VerifyFiles(target, before);
        Assert.Equal(WidgetRecoveryResult.None, WidgetUpdateInstaller.RecoverInterruptedFiles(target));
    }

    [Fact]
    public void StartupRecoveryDoesNotCompeteWithActiveInstaller()
    {
        var target = Path.Combine(_root, "target");
        var payload = Path.Combine(_root, "payload");
        var before = Fixture(target, "old");
        var next = Fixture(payload, "new");
        WidgetUpdateInstaller.ReplaceTransactional(target, payload, Path.Combine(_root, "backup"), before, next);
        using (var lease = new FileStream(Path.Combine(target, ".widget-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(WidgetRecoveryResult.Busy, WidgetUpdateInstaller.RecoverInterruptedFiles(target));
            WidgetUpdateInstaller.VerifyFiles(target, next);
        }
        Assert.Equal(WidgetRecoveryResult.Recovered, WidgetUpdateInstaller.RecoverInterruptedFiles(target));
        WidgetUpdateInstaller.VerifyFiles(target, before);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Product\":\"other\",\"BackupDirectory\":null,\"PreviousHashes\":null}")]
    [InlineData("{\"Product\":\"mali-yaya\",\"BackupDirectory\":null,\"PreviousHashes\":null}")]
    public void DamagedRecoveryJournalPreservesExistingRuntime(string json)
    {
        var target = Path.Combine(_root, "target");
        var before = Fixture(target, "old");
        File.WriteAllText(Path.Combine(target, "widget-update-recovery.json"), json);
        Assert.Throws<InvalidDataException>(() => WidgetUpdateInstaller.RecoverInterruptedFiles(target));
        WidgetUpdateInstaller.VerifyFiles(target, before);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("payload")]
    public void ChangedFileBeforeApplyIsRejectedWithoutPartialInstall(string changed)
    {
        var target = Path.Combine(_root, "target");
        var payload = Path.Combine(_root, "payload");
        var before = Fixture(target, "old");
        var next = Fixture(payload, "new");
        File.AppendAllText(Path.Combine(_root, changed, "CodexUsageWidget.dll"), "tampered");
        Assert.Throws<InvalidDataException>(() => WidgetUpdateInstaller.ReplaceTransactional(target, payload,
            Path.Combine(_root, "backup"), before, next));
        Assert.False(Directory.Exists(Path.Combine(_root, "backup")));
        if (changed == "payload") WidgetUpdateInstaller.VerifyFiles(target, before);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData(@"\\server\share\widget")]
    [InlineData("//server/share/widget")]
    [InlineData(@"C:\")]
    public void UnsafeDirectoryIsRejected(string path) => Assert.Throws<InvalidDataException>(() => WidgetUpdateInstaller.LocalDirectory(path));

    [Fact]
    public async Task InstallerRejectsUnexpectedPlanLocationWithoutStartingAnything() =>
        Assert.Equal(2, await WidgetUpdateInstaller.RunAsync(Path.Combine(_root, "apply.json")));

    [Theory]
    [InlineData("Jackmaolk/mali-yaya", true)]
    [InlineData("Jackmaolk/another-repo", true)]
    [InlineData("Jackmaolk", false)]
    [InlineData("https://github.com/Jackmaolk/mali-yaya", false)]
    [InlineData("JiaYang-BUAA/Codex-Desktop-Usage-Monitor-Windows", false)]
    [InlineData("Jackmaolk/../other", false)]
    [InlineData("Jackmaolk/repo.git", false)]
    [InlineData("Jackmaolk/repo?token=anything", false)]
    public void UpdateSourceRequiresExplicitOwnedRepository(string repository, bool accepted) =>
        Assert.Equal(accepted, WidgetUpdateSettings.IsOwnedRepository(repository));

    [Fact]
    public void SettingsRemainOffAndUnconfiguredUntilExplicitlySaved()
    {
        var path = Path.Combine(_root, "settings.json");
        Assert.Equal(new WidgetUpdateOptions(), WidgetUpdateSettings.Load(path));
        WidgetUpdateSettings.Save(new(false, "Jackmaolk/mali-yaya"), path);
        Assert.Equal(new WidgetUpdateOptions(false, "Jackmaolk/mali-yaya"), WidgetUpdateSettings.Load(path));
        Assert.Throws<ArgumentException>(() => WidgetUpdateSettings.Save(new(true, null), path));
        Assert.Throws<ArgumentException>(() => WidgetUpdateSettings.Save(new(true, "other/repo"), path));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Enabled\":true,\"Repository\":\"other/repo\"}")]
    public void CorruptSettingsCannotEnableNetworkChecks(string content)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, content);
        Assert.Equal(new WidgetUpdateOptions(), WidgetUpdateSettings.Load(path));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"LastAttemptUtc\":null,\"Status\":\"unknown\"}")]
    [InlineData("{\"LastAttemptUtc\":\"2026-10-28T00:00:00Z\",\"Status\":\"unknown\",\"Status\":\"unknown\"}")]
    public async Task InvalidCooldownStateIsNonfatalAndNeverStartsNetwork(string content)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "update-state.json"), content);
        using var updater = new WidgetUpdateService(stateDirectory: _root);
        Assert.Null(await updater.CheckAsync(new(true, "Jackmaolk/mali-yaya")));
        Assert.Null(updater.State.LastAttemptUtc);
        Assert.Equal(content, File.ReadAllText(Path.Combine(_root, "update-state.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
