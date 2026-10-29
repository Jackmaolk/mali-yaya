using System.IO;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class WidgetUpdateHandoffTests : IDisposable
{
    private readonly string _job = Path.Combine(Path.GetTempPath(), "YayaHandoffTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReadyOrParentExitAloneIsNeverPermissionToApply()
    {
        Directory.CreateDirectory(_job);
        File.WriteAllText(Path.Combine(_job, "ready"), "ready");
        Assert.False(WidgetUpdateInstaller.IsApplyApproved(_job));
        using (var handoff = new WidgetUpdateHandoff(_job, CancellationToken.None)) { }
        Assert.False(WidgetUpdateInstaller.IsApplyApproved(_job));
        Assert.True(File.Exists(Path.Combine(_job, "revoked")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationBeforeUiCommitLeavesNoUsablePermit(bool alreadyCanceled)
    {
        using var cancellation = new CancellationTokenSource();
        if (alreadyCanceled) cancellation.Cancel();
        using var handoff = new WidgetUpdateHandoff(_job, cancellation.Token);
        if (!alreadyCanceled) cancellation.Cancel();
        Assert.False(handoff.TryCommit());
        Assert.False(WidgetUpdateInstaller.IsApplyApproved(_job));
        Assert.False(File.Exists(Path.Combine(_job, "approved")));
    }

    [Fact]
    public void TimeoutOrAbandonedUiHandoffCannotInstallOnALaterUserClose()
    {
        var handoff = new WidgetUpdateHandoff(_job, CancellationToken.None);
        handoff.Dispose();
        Assert.False(handoff.TryCommit());
        Assert.False(WidgetUpdateInstaller.IsApplyApproved(_job));
    }

    [Fact]
    public void CommitIsOneShotAndSurvivesExpectedParentShutdownCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var handoff = new WidgetUpdateHandoff(_job, cancellation.Token);
        Assert.True(handoff.TryCommit());
        Assert.False(handoff.TryCommit());
        cancellation.Cancel();
        handoff.Dispose();
        Assert.True(WidgetUpdateInstaller.IsApplyApproved(_job));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"approved\":false}")]
    [InlineData("{\"approved\":true,\"unknown\":true}")]
    [InlineData("{\"approved\":true,\"approved\":true}")]
    [InlineData("null")]
    [InlineData("bad")]
    public void DamagedOrAmbiguousPermitFailsClosed(string content)
    {
        Directory.CreateDirectory(_job);
        File.WriteAllText(Path.Combine(_job, "approved"), content);
        Assert.False(WidgetUpdateInstaller.IsApplyApproved(_job));
    }

    public void Dispose() { if (Directory.Exists(_job)) Directory.Delete(_job, true); }
}
