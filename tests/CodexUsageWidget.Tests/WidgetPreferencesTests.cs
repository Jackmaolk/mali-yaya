using System.IO;
using System.Text.Json;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class WidgetPreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "yaya-prefs-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "preferences.json");

    [Fact]
    public void MissingPreferencesEnableMonitoringWithoutCreatingStorage()
    {
        Assert.Equal(new WidgetPreferences(true, false), WidgetPreferences.Load(FilePath));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void MissingFileInExistingDirectoryEnablesMonitoring()
    {
        Directory.CreateDirectory(_directory);
        Assert.Equal(new WidgetPreferences(true, false), WidgetPreferences.Load(FilePath));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NetworkAndCompactSettingsPersistIndependently(bool network, bool compact)
    {
        var options = new WidgetPreferences(network, compact);
        WidgetPreferences.Save(options, FilePath);
        Assert.Equal(options, WidgetPreferences.Load(FilePath));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"TiboEnabled\":true}")]
    [InlineData("{\"TiboEnabled\":\"true\",\"CompactMode\":false}")]
    [InlineData("{\"TiboEnabled\":true,\"TiboEnabled\":false,\"CompactMode\":false}")]
    [InlineData("{\"TiboEnabled\":true,\"CompactMode\":false,\"unknown\":true}")]
    public void InvalidSettingsNeverEnableNetwork(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, json);
        Assert.False(WidgetPreferences.Load(FilePath).TiboEnabled);
    }

    [Fact]
    public void OversizedSettingsFailClosed()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, new string(' ', 8193));
        Assert.Equal(new WidgetPreferences(false, false), WidgetPreferences.Load(FilePath));
    }

    [Theory]
    [InlineData("relative/preferences.json")]
    [InlineData(@"\\server\share\preferences.json")]
    [InlineData(@"\\?\C:\preferences.json")]
    public void UnsafePreferencePathsFailClosedInsteadOfCrashing(string path)
    {
        Assert.Equal(new WidgetPreferences(false, false), WidgetPreferences.Load(path));
        Assert.Throws<InvalidDataException>(() => WidgetPreferences.Save(new(true, true), path));
    }

    [Fact]
    public void DisablingNetworkCancelsCurrentAndRejectsLateCompletion()
    {
        using var gate = new TiboNetworkGate();
        Assert.False(gate.TryCapture(out _, out _));
        gate.SetEnabled(true);
        Assert.True(gate.TryCapture(out var token, out var generation));
        Assert.True(gate.IsCurrent(generation));
        gate.SetEnabled(false);
        Assert.True(token.IsCancellationRequested);
        Assert.False(gate.IsCurrent(generation));
        gate.SetEnabled(true);
        Assert.True(gate.TryCapture(out var nextToken, out var next));
        Assert.False(nextToken.IsCancellationRequested);
        Assert.NotEqual(generation, next);
        Assert.False(gate.IsCurrent(generation));
    }

    [Fact]
    public void DisposingNetworkCancelsWithoutRestart()
    {
        var gate = new TiboNetworkGate();
        gate.SetEnabled(true);
        gate.TryCapture(out var token, out var generation);
        gate.Dispose();
        gate.SetEnabled(true);
        Assert.True(token.IsCancellationRequested);
        Assert.False(gate.TryCapture(out _, out _));
        Assert.False(gate.IsCurrent(generation));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
