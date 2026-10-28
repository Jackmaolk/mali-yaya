using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class CodexDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CodexDiscoveryTests", Guid.NewGuid().ToString("N"));

    private CodexDiscoveryEnvironment EnvironmentFor(string path = "", string? environmentOverride = null,
        Architecture architecture = Architecture.X64) => new(path, Path.Combine(_root, "roaming"),
        Path.Combine(_root, "local"), Path.Combine(_root, "home"), Path.Combine(_root, "programs"),
        architecture, environmentOverride);

    private string Native(string relative, ushort machine = 0x8664)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[256];
        BitConverter.GetBytes((ushort)0x5A4D).CopyTo(bytes, 0);
        BitConverter.GetBytes(128).CopyTo(bytes, 0x3C);
        BitConverter.GetBytes(0x00004550).CopyTo(bytes, 128);
        BitConverter.GetBytes(machine).CopyTo(bytes, 132);
        BitConverter.GetBytes((ushort)0x20B).CopyTo(bytes, 152);
        BitConverter.GetBytes((ushort)3).CopyTo(bytes, 220);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Theory]
    [InlineData("node_modules/@openai/codex-win32-x64/vendor/x86_64-pc-windows-msvc/bin/codex.exe")]
    [InlineData("node_modules/@openai/codex-win32-x64/vendor/x86_64-pc-windows-msvc/codex/codex.exe")]
    [InlineData("vendor/x86_64-pc-windows-msvc/bin/codex.exe")]
    public void DiscoversCurrentAndLegacyNpmPayloadsWithoutRunningShim(string suffix)
    {
        var path = Native("roaming/npm/node_modules/@openai/codex/" + suffix);
        Assert.Contains(new CodexExecutableResolver(EnvironmentFor()).FindCandidates(), c => c.Path == path);
    }

    [Fact]
    public void DiscoversHoistedPlatformPackageFromCustomPathPrefix()
    {
        var path = Native("custom prefix/node_modules/@openai/codex-win32-x64/vendor/x86_64-pc-windows-msvc/bin/codex.exe");
        var directory = Path.Combine(_root, "custom prefix");
        var candidate = Assert.Single(new CodexExecutableResolver(EnvironmentFor(directory)).FindCandidates());
        Assert.Equal(path, candidate.Path);
        Assert.Contains("npm", candidate.Source);
    }

    [Theory]
    [InlineData("local/Programs/OpenAI/Codex/bin/codex.exe")]
    [InlineData("local/OpenAI/Codex/bin/hash123/codex.exe")]
    [InlineData("local/OpenAI/Codex/bin/codex.exe")]
    [InlineData("local/Programs/Codex/resources/codex.exe")]
    [InlineData("programs/OpenAI/Codex/resources/codex.exe")]
    [InlineData("programs/WindowsApps/OpenAI.Codex_1.0_x64/app/resources/codex.exe")]
    [InlineData("home/.vscode/extensions/openai.chatgpt-1.2.3/bin/windows-x86_64/codex.exe")]
    [InlineData("home/.vscode-insiders/extensions/openai.chatgpt-1.2.3/bin/windows-x86_64/codex.exe")]
    [InlineData("home/.cursor/extensions/openai.chatgpt-1.2.3/bin/windows-x86_64/codex.exe")]
    public void DiscoversKnownNativeInstallations(string relative)
    {
        var path = Native(relative);
        Assert.Contains(new CodexExecutableResolver(EnvironmentFor()).FindCandidates(), c => c.Path == path);
    }

    [Fact]
    public void PathHasPriorityOverFallbackAndDuplicateDirectoriesAreRemoved()
    {
        var first = Native("custom/codex.exe");
        Native("local/Programs/OpenAI/Codex/bin/codex.exe");
        var path = Path.GetDirectoryName(first)!;
        var candidates = new CodexExecutableResolver(EnvironmentFor($";{path};\"{path}\";.;relative")).FindCandidates();
        Assert.Equal(first, candidates[0].Path);
        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void MostRecentlyInstalledDesktopPayloadIsTriedFirst()
    {
        var old = Native("local/OpenAI/Codex/bin/old/codex.exe");
        var latest = Native("local/OpenAI/Codex/bin/new/codex.exe");
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(old)!, DateTime.UtcNow.AddDays(-5));
        var candidates = new CodexExecutableResolver(EnvironmentFor()).FindCandidates();
        Assert.Equal(latest, candidates[0].Path);
    }

    [Fact]
    public void ManualSelectionWinsOverEnvironmentAndPath()
    {
        var manual = Native("manual/codex.exe");
        var environment = Native("environment/codex.exe");
        var path = Native("path/codex.exe");
        var resolver = new CodexExecutableResolver(EnvironmentFor(Path.GetDirectoryName(path)!, environment));
        Assert.Equal(manual, Assert.Single(resolver.FindCandidates(manual)).Path);
        Assert.Equal(environment, Assert.Single(resolver.FindCandidates()).Path);
    }

    [Fact]
    public async Task BrokenExplicitPathDoesNotSilentlySwitchToAnotherInstallation()
    {
        Native("local/Programs/OpenAI/Codex/bin/codex.exe");
        var resolver = new CodexExecutableResolver(EnvironmentFor(), (_, _) => throw new Exception("Must not execute"));
        var error = await Assert.ThrowsAsync<CodexDiscoveryException>(() => resolver.ResolveAsync(Path.Combine(_root, "missing.exe")));
        Assert.Contains("指定", error.Message);
    }

    [Fact]
    public void StaleDesktopHintDoesNotDisableNormalDiscovery()
    {
        var good = Native("local/Programs/OpenAI/Codex/bin/codex.exe");
        var context = EnvironmentFor() with { DesktopHint = Path.Combine(_root, "removed-version", "codex.exe") };
        Assert.Equal(good, Assert.Single(new CodexExecutableResolver(context).FindCandidates()).Path);
    }

    [Fact]
    public async Task UnusableDesktopHintFallsBackWithoutReplacingUserConfiguration()
    {
        var bad = Native("old-desktop/codex.exe");
        var good = Native("local/Programs/OpenAI/Codex/bin/codex.exe");
        var context = EnvironmentFor() with { DesktopHint = bad };
        var resolver = new CodexExecutableResolver(context,
            (path, _) => Task.FromResult<string?>(path == good ? "codex-cli 0.162.0" : null));
        Assert.Equal(good, (await resolver.ResolveAsync()).ExecutablePath);
    }

    [Theory]
    [InlineData("codex.exe")]
    [InlineData(".\\codex.exe")]
    [InlineData("C:\\tools\\codex.cmd")]
    [InlineData("C:\\tools\\codex.ps1")]
    [InlineData("C:\\tools\\codex.exe --anything")]
    [InlineData("\\\\server\\share\\codex.exe")]
    [InlineData("//server/share/codex.exe")]
    public void RejectsScriptsRelativePathsNetworkPathsAndArguments(string value) =>
        Assert.Null(CodexExecutableResolver.TryNormalizeLocalExecutable(value));

    [Fact]
    public void PathsWithSpacesAndNonAsciiAreKeptAsOneExecutable()
    {
        var path = Native("中文 空格/codex.exe");
        Assert.Equal(path, CodexExecutableResolver.TryNormalizeLocalExecutable('"' + path + '"'));
    }

    [Fact]
    public void TextOrExecutionAliasIsNotAcceptedAsNativeCode()
    {
        var path = Native("bad/codex.exe");
        File.WriteAllText(path, "not an executable");
        Assert.False(CodexExecutableResolver.IsNativeExecutable(path, Architecture.X64));
    }

    [Fact]
    public void DesktopGuiExecutableIsNotProbedAsACommandLineClient()
    {
        var path = Native("gui/codex.exe");
        var bytes = File.ReadAllBytes(path);
        BitConverter.GetBytes((ushort)2).CopyTo(bytes, 220);
        File.WriteAllBytes(path, bytes);
        Assert.False(CodexExecutableResolver.IsNativeExecutable(path, Architecture.X64));
    }

    [Fact]
    public void Arm64AndX64AreFilteredAccordingToOperatingSystemArchitecture()
    {
        var path = Native("arm/codex.exe", 0xAA64);
        Assert.False(CodexExecutableResolver.IsNativeExecutable(path, Architecture.X64));
        Assert.True(CodexExecutableResolver.IsNativeExecutable(path, Architecture.Arm64));
        Assert.True(CodexExecutableResolver.IsNativeExecutable(Native("x64/codex.exe"), Architecture.Arm64));
    }

    [Fact]
    public async Task InvalidVersionOrBrokenCandidateFallsBackToNextKnownInstallation()
    {
        var bad = Native("old/codex.exe");
        var good = Native("local/Programs/OpenAI/Codex/bin/codex.exe");
        var calls = new List<string>();
        var resolver = new CodexExecutableResolver(EnvironmentFor(Path.GetDirectoryName(bad)!), (file, _) =>
        {
            calls.Add(file);
            return Task.FromResult<string?>(file == good ? "codex-cli 0.162.0-alpha.17.2\r\n" : "some other tool");
        });
        var found = await resolver.ResolveAsync();
        Assert.Equal(good, found.ExecutablePath);
        Assert.Equal("0.162.0-alpha.17.2", found.Version);
        Assert.Equal(new[] { bad, good }, calls);
    }

    [Fact]
    public async Task ProbeFailuresAreContainedAndDoNotLeakRawOutput()
    {
        var path = Native("old/codex.exe");
        var resolver = new CodexExecutableResolver(EnvironmentFor(Path.GetDirectoryName(path)!),
            (_, _) => throw new IOException("private diagnostic text"));
        var error = await Assert.ThrowsAsync<CodexDiscoveryException>(() => resolver.ResolveAsync());
        Assert.DoesNotContain("private diagnostic", error.Message);
    }

    [Fact]
    public async Task LimitsTheNumberOfExecutableProbes()
    {
        var paths = Enumerable.Range(0, 12).Select(i => Path.GetDirectoryName(Native($"p{i}/codex.exe"))!);
        var calls = 0;
        var resolver = new CodexExecutableResolver(EnvironmentFor(string.Join(';', paths)), (_, _) =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });
        await Assert.ThrowsAsync<CodexDiscoveryException>(() => resolver.ResolveAsync());
        Assert.Equal(8, calls);
    }

    [Fact]
    public async Task UserCancellationIsNotReportedAsMissingInstallation()
    {
        var path = Native("old/codex.exe");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var resolver = new CodexExecutableResolver(EnvironmentFor(Path.GetDirectoryName(path)!));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(cancellationToken: cancellation.Token));
    }

    [Fact]
    public void SavedPathIsPerUserMetadataAndCanReturnToAutomaticMode()
    {
        var file = Path.Combine(_root, "settings", "connection-settings.json");
        var exe = Native("中文 空格/codex.exe");
        CodexConnectionSettings.SavePath(exe, file);
        Assert.Equal(exe, CodexConnectionSettings.LoadPath(file));
        using (var data = JsonDocument.Parse(File.ReadAllText(file)))
            Assert.Equal(new[] { "schemaVersion", "executablePath" }, data.RootElement.EnumerateObject().Select(p => p.Name));
        CodexConnectionSettings.SavePath(null, file);
        Assert.Null(CodexConnectionSettings.LoadPath(file));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"executablePath\": 123}")]
    public void CorruptSettingsFallBackToAutomaticDiscovery(string json)
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "settings.json");
        File.WriteAllText(file, json);
        Assert.Null(CodexConnectionSettings.LoadPath(file));
    }

    [Fact]
    public void OversizedSettingsAreNotLoaded()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "settings.json");
        File.WriteAllText(file, new string('x', 16 * 1024 + 1));
        Assert.Null(CodexConnectionSettings.LoadPath(file));
    }

    [Theory]
    [InlineData("{\"account\":null}", "登录")]
    [InlineData("{}", "登录")]
    [InlineData("{\"account\":{\"type\":\"apiKey\"}}", "API Key")]
    [InlineData("{\"account\":[]}", "协议")]
    public void MissingOrUnsupportedAccountHasActionableMessage(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);
        var error = Assert.Throws<CodexDiscoveryException>(() => CodexRateLimitClient.ValidateAccount(document.RootElement));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void ChatGptAccountIsAcceptedWithoutExposingAccountDetails()
    {
        using var document = JsonDocument.Parse("{\"account\":{\"type\":\"chatgpt\",\"planType\":\"plus\"}}");
        CodexRateLimitClient.ValidateAccount(document.RootElement);
        Assert.DoesNotContain("private details", CodexRateLimitClient.DescribeConnectionError(new IOException("private details")));
    }

    [Theory]
    [InlineData("Error: failed to initialize sqlite state runtime under private-directory", "CODEX_HOME")]
    [InlineData("Error: unexpected argument --listen", "协议")]
    public void StartupErrorsHaveActionableSanitizedMessages(string line, string expected)
    {
        var description = CodexRateLimitClient.DescribeStartupDiagnostic(line);
        Assert.NotNull(description);
        Assert.Contains(expected, description);
        Assert.DoesNotContain("private-directory", description);
    }

    public void Dispose()
    {
        var safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodexDiscoveryTests")) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(_root);
        if (!full.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture path.");
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }
}
