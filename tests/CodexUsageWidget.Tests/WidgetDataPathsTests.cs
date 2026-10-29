using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class WidgetDataPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(TestDataIsolation.DirectoryPath, Guid.NewGuid().ToString("N"));

    public WidgetDataPathsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ModuleInitializerRedirectsAllDefaultStorage()
    {
        Assert.Equal(TestDataIsolation.DirectoryPath, WidgetDataPaths.RootDirectory);
        Assert.Equal(Path.Combine(TestDataIsolation.DirectoryPath, "cache"), WidgetDataPaths.CacheDirectory);
        Assert.Equal(Path.Combine(TestDataIsolation.DirectoryPath, "connection-settings.json"), WidgetDataPaths.ConnectionSettingsPath);
        Assert.Equal(Path.Combine(TestDataIsolation.DirectoryPath, "update-settings.json"), WidgetUpdateSettings.FilePath);
    }

    [Fact]
    public void HealthPathsAreHashedByCanonicalInstallationNotDataOrWorkingDirectory()
    {
        var install = Path.Combine(_root, "install");
        var equivalent = Path.Combine(_root, "unused", "..", "INSTALL") + Path.DirectorySeparatorChar;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(install).TrimEnd('\\', '/').ToUpperInvariant()))).ToLowerInvariant();
        var expected = Path.Combine(TestDataIsolation.DirectoryPath, "runtime", hash, "widget-runtime.json");

        Assert.Equal(expected, WidgetDataPaths.RuntimeStatusPath(install));
        Assert.Equal(expected, WidgetDataPaths.RuntimeStatusPath(equivalent));
        Assert.NotEqual(expected, WidgetDataPaths.RuntimeStatusPath(Path.Combine(_root, "other-install")));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("C:relative")]
    [InlineData("\\root-relative")]
    [InlineData("\\\\server\\share\\data")]
    [InlineData("\\\\?\\C:\\data")]
    [InlineData("\\\\.\\C:\\data")]
    [InlineData("//server/share/data")]
    [InlineData("C:\\data:stream")]
    [InlineData("C:\\NUL\\data")]
    [InlineData("C:\\CON .json")]
    public void NonLocalOrAmbiguousPathsAreRejected(string path)
    {
        Assert.Throws<InvalidDataException>(() => WidgetDataPaths.RuntimeStatusPath(path));
    }

    [Fact]
    public void AtomicWritesUseRandomTempsAndPreserveOldContentWhenDestinationIsLocked()
    {
        var path = Path.Combine(_root, "settings.json");
        WidgetDataPaths.WriteJsonAtomic(path, new { value = "first" });
        File.WriteAllText(path + ".tmp", "unrelated sentinel");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var error = Record.Exception(() => WidgetDataPaths.WriteJsonAtomic(path, new { value = "second" }));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        }

        Assert.Contains("first", File.ReadAllText(path));
        Assert.Equal("unrelated sentinel", File.ReadAllText(path + ".tmp"));
        Assert.Equal(2, Directory.GetFiles(_root).Length);
        WidgetDataPaths.WriteJsonAtomic(path, new { value = "third" });
        Assert.Contains("third", File.ReadAllText(path));
    }

    [Fact]
    public void HealthRecordContainsOnlyUnboundLivenessAndAccountCheckpointIsNotRestored()
    {
        WidgetRuntimeStatus.Write(null, null, chineseBrief: "synthetic text", clockResetsAt: 1800000000,
            clockConfirmedAtUtc: DateTimeOffset.UtcNow);
        using var health = JsonDocument.Parse(File.ReadAllText(WidgetDataPaths.RuntimeStatusPath()));
        Assert.Equal(new[] { "buildVersion", "moduleId", "processId", "running", "updatedAtUtc" },
            health.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(typeof(App).Assembly.ManifestModule.ModuleVersionId, health.RootElement.GetProperty("moduleId").GetGuid());
        Assert.Equal(Environment.ProcessId, health.RootElement.GetProperty("processId").GetInt32());
        Assert.NotEqual(WidgetDataPaths.RuntimeStatusPath(), WidgetDataPaths.AccountRuntimePath());
        Assert.Null(WidgetRuntimeStatus.ReadCountdownCheckpoint());
        Assert.NotNull(WidgetRuntimeStatus.ReadCountdownCheckpoint(WidgetDataPaths.AccountRuntimePath()));
    }

    [Fact]
    public void UpdaterReadinessUsesHealthWithoutAccountQuota()
    {
        var install = Path.Combine(_root, "install");
        var since = DateTimeOffset.UtcNow.AddSeconds(-1);
        var path = WidgetDataPaths.RuntimeStatusPath(install);
        WidgetDataPaths.WriteJsonAtomic(path, new { buildVersion = "2026.10.28.9", moduleId = Guid.NewGuid(),
            processId = Environment.ProcessId, running = true, updatedAtUtc = DateTimeOffset.UtcNow });
        var ready = typeof(WidgetUpdateInstaller).GetMethod("Ready", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True((bool)ready.Invoke(null, [install, "2026.10.28.9", Environment.ProcessId, since])!);
        Assert.False((bool)ready.Invoke(null, [install, "2026.10.28.8", Environment.ProcessId, since])!);
    }

    [Fact]
    public void SettingsExplicitRelativePathsAreRejectedWithoutReadingOrCreatingFiles()
    {
        Assert.Null(CodexConnectionSettings.LoadPath("relative.json"));
        Assert.Equal(new WidgetUpdateOptions(), WidgetUpdateSettings.Load("relative.json"));
        Assert.Throws<InvalidDataException>(() => CodexConnectionSettings.SavePath(null, "relative.json"));
        Assert.Throws<InvalidDataException>(() => WidgetUpdateSettings.Save(new(), "relative.json"));
    }

    [Fact]
    public void JunctionAncestorsAreRejectedByReadersAndWritersWithoutChangingTargets()
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var realFile = Path.Combine(target, "settings.json");
        var sentinel = "{\"executablePath\":\"synthetic.exe\"}";
        File.WriteAllText(realFile, sentinel);
        var link = Path.Combine(_root, "link");
        CreateJunction(link, target);
        try
        {
            Assert.Throws<InvalidDataException>(() => WidgetDataPaths.RuntimeStatusPath(Path.Combine(link, "install")));
            Assert.Throws<InvalidDataException>(() => WidgetDataPaths.WriteJsonAtomic(Path.Combine(link, "settings.json"), new { value = 1 }));
            Assert.Throws<InvalidDataException>(() => WidgetDataPaths.EnsureSafeDirectory(Path.Combine(link, "new-directory")));
            Assert.Throws<InvalidDataException>(() => WidgetDataPaths.RejectReparseAncestors(link));
            Assert.Null(CodexConnectionSettings.LoadPath(Path.Combine(link, "settings.json")));
            Assert.Equal(new WidgetUpdateOptions(), WidgetUpdateSettings.Load(Path.Combine(link, "settings.json")));
            Assert.Null(WidgetRuntimeStatus.ReadCountdownCheckpoint(Path.Combine(link, "settings.json")));
            Assert.Equal(sentinel, File.ReadAllText(realFile));
            Assert.Single(Directory.GetFiles(target));
            Assert.Empty(Directory.GetDirectories(target));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void AppAndUpdaterShareCentralHealthAndNeverFallBackToLegacyOrAccountRuntime()
    {
        var install = Path.Combine(_root, "install");
        Directory.CreateDirectory(install);
        var since = DateTimeOffset.UtcNow.AddSeconds(-1);
        var health = new { buildVersion = WidgetRuntimeStatus.BuildVersion,
            moduleId = typeof(App).Assembly.ManifestModule.ModuleVersionId,
            processId = Environment.ProcessId, running = true, updatedAtUtc = DateTimeOffset.UtcNow };
        var legacy = Path.Combine(install, "widget-runtime.json");
        var sentinel = JsonSerializer.Serialize(health);
        File.WriteAllText(legacy, sentinel);
        WidgetDataPaths.WriteJsonAtomic(WidgetDataPaths.AccountRuntimePath(install), health);
        var ready = typeof(WidgetUpdateInstaller).GetMethod("Ready", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool UpdaterReady() => (bool)ready.Invoke(null, [install, WidgetRuntimeStatus.BuildVersion, Environment.ProcessId, since])!;

        Assert.False(App.IsCurrentBuildRunningForInstallation(install));
        Assert.False(UpdaterReady());
        var path = WidgetDataPaths.RuntimeStatusPath(install);
        WidgetDataPaths.WriteJsonAtomic(path, health);
        Assert.True(App.IsCurrentBuildRunningForInstallation(install));
        Assert.True(UpdaterReady());
        WidgetDataPaths.WriteJsonAtomic(path, health with { running = false });
        Assert.False(App.IsCurrentBuildRunningForInstallation(install));
        Assert.False(UpdaterReady());
        Assert.Equal(sentinel, File.ReadAllText(legacy));
        Assert.Single(Directory.GetFiles(install));
    }

    private static void CreateJunction(string link, string target)
    {
        // Directory junctions do not require the symbolic-link privilege on Windows.
        if ((link + target).IndexOfAny("\"&|<>^%!\r\n".ToCharArray()) >= 0)
            throw new InvalidOperationException("Unsafe junction fixture path.");
        var start = new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var process = Process.Start(start) ?? throw new IOException("Junction fixture process unavailable.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"Junction fixture failed: {output} {error}");
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
    }

    [Theory]
    [InlineData("running")]
    [InlineData("moduleId")]
    [InlineData("processId")]
    [InlineData("updatedAtUtc")]
    [InlineData("malformed")]
    [InlineData("oversized")]
    public void InvalidOrStaleHealthCannotMakeUpdaterReady(string defect)
    {
        var install = Path.Combine(_root, "install");
        var since = DateTimeOffset.UtcNow;
        var path = WidgetDataPaths.RuntimeStatusPath(install);
        var health = JsonSerializer.SerializeToNode(new { buildVersion = "2026.10.28.9", moduleId = Guid.NewGuid(),
            processId = Environment.ProcessId, running = true, updatedAtUtc = since.AddSeconds(1) })!.AsObject();
        switch (defect)
        {
            case "running": health["running"] = false; break;
            case "moduleId": health.Remove("moduleId"); break;
            case "processId": health["processId"] = -1; break;
            case "updatedAtUtc": health["updatedAtUtc"] = JsonValue.Create(since.AddSeconds(-1)); break;
        }
        WidgetDataPaths.EnsureSafeDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, defect switch
        {
            "malformed" => "{broken",
            "oversized" => health.ToJsonString() + new string(' ', 8193),
            _ => health.ToJsonString()
        });
        var ready = typeof(WidgetUpdateInstaller).GetMethod("Ready", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.False((bool)ready.Invoke(null, [install, "2026.10.28.9", Environment.ProcessId, since])!);
    }

    [Fact]
    public void FailedAccountWriteDoesNotBlockHealthAndDefaultCheckpointNeverReadsALockedAccount()
    {
        var account = WidgetDataPaths.AccountRuntimePath();
        WidgetDataPaths.WriteJsonAtomic(account, new { clockResetsAt = 1800000000, updatedAtUtc = DateTimeOffset.UtcNow });
        using var locked = new FileStream(account, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        WidgetRuntimeStatus.Write(null, null);

        using var health = JsonDocument.Parse(File.ReadAllText(WidgetDataPaths.RuntimeStatusPath()));
        Assert.True(health.RootElement.GetProperty("running").GetBoolean());
        Assert.Null(WidgetRuntimeStatus.ReadCountdownCheckpoint());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalCachePathFailureRetainsValidatedNetworkAndMemoryResult(bool relative)
    {
        var blocked = relative ? "not-an-absolute-cache" : Path.Combine(_root, "blocked");
        if (!relative) File.WriteAllText(blocked, "sentinel");
        using var handler = new TranslationHandler();
        using var http = new HttpClient(handler);
        using var client = new TiboChineseBriefClient(http, blocked);
        var post = TranslationHandler.Post;

        Assert.Equal("\u4e2d\u6587", (await client.GetAsync(post))?.Text);
        Assert.Equal("\u4e2d\u6587", (await client.GetAsync(post))?.Text);
        Assert.Equal(1, handler.RequestCount);
        if (!relative) Assert.Equal("sentinel", File.ReadAllText(blocked));
    }

    [Fact]
    public async Task DefaultTranslationCacheUsesOnlySharedPublicCacheUnderIsolatedRoot()
    {
        using var handler = new TranslationHandler();
        using var http = new HttpClient(handler);
        using (var client = new TiboChineseBriefClient(http)) Assert.NotNull(await client.GetAsync(TranslationHandler.Post));
        var path = Path.Combine(WidgetDataPaths.CacheDirectory, "tibo-chinese-cache.json");
        Assert.True(File.Exists(path));
        using var restarted = new TiboChineseBriefClient(http);
        Assert.NotNull(await restarted.GetAsync(TranslationHandler.Post));
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class TranslationHandler : HttpMessageHandler
    {
        internal static TiboPost Post { get; } = new("Synthetic public post",
            "https://x.com/thsottiaux/status/2114800277677092999", DateTimeOffset.UtcNow);
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            RequestCount++;
            var json = JsonSerializer.Serialize(new { code = 200, status = new
            {
                id = "2114800277677092999", text = Post.Text, created_timestamp = Post.PostTime.ToUnixTimeSeconds(),
                author = new { screen_name = "thsottiaux" },
                translation = new { text = "\u4e2d\u6587", target_lang = "zh", provider = "synthetic" }
            } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
