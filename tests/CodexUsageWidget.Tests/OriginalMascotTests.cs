using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Xunit;

namespace CodexUsageWidget.Tests;

[Collection("WpfPresentation")]
public sealed class OriginalMascotTests
{
    [Theory]
    [InlineData("plus", "PLUS")]
    [InlineData("pro", "PRO")]
    [InlineData(null, "--")]
    public void PlanBadgeUsesReportedSubscriptionInsteadOfGuessing(string? plan, string expected) => InSta(() =>
    {
        var window = new MainWindow(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        var snapshot = Weekly(60);
        snapshot.PlanType = plan;
        Invoke(window, "ApplySnapshot", snapshot);
        Assert.Equal(expected, Find<TextBlock>(window, "PlanText").Text);
    });

    [Theory]
    [InlineData(99, "精神满满", 7)]
    [InlineData(60, "轻松工作", 7)]
    [InlineData(40, "认真专注", 5)]
    [InlineData(20, "有点担心", 5)]
    [InlineData(19, "额度告急", 5)]
    public void OriginalRobotFollowsQuotaMood(int remaining, string mood, int eyeHeight) => InSta(() =>
    {
        var window = new MainWindow(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        Invoke(window, "UpdatePetMood", Weekly(remaining));
        Assert.Equal(mood, Find<TextBlock>(window, "PetMoodText").Text);
        Assert.Equal(eyeHeight, Find<Ellipse>(window, "LeftPupil").Height);
        Assert.Equal(eyeHeight, Find<Ellipse>(window, "RightPupil").Height);
        var dot = Assert.IsType<SolidColorBrush>(Find<Ellipse>(window, "PetStatusDot").Fill);
        var light = Assert.IsType<SolidColorBrush>(Find<System.Windows.Shapes.Path>(window, "CharacterStatusLight").Fill);
        Assert.Equal(dot.Color, light.Color);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnnouncedAndObservedResetKeepAnExpectantPose(bool observed) => InSta(() =>
    {
        var window = new MainWindow(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        var coordinator = (TiboStatusCoordinator)typeof(MainWindow)
            .GetField("_tiboStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var now = DateTimeOffset.UtcNow;
        coordinator.OnSignal(new TiboSignal(observed ? TiboSignalKind.ObservedReset : TiboSignalKind.Planned,
            "预告", "公开动态，等待账户确认", null, now, now) { ExpectedEndAt = now.AddHours(1) }, now);
        Invoke(window, "UpdatePetMood", Weekly(60));
        Assert.Equal("期待重置", Find<TextBlock>(window, "PetMoodText").Text);
        Assert.Equal(0.3, Find<Ellipse>(window, "CharacterStatusGlow").Opacity);
    });

    [Fact]
    public void GiftedResetAndActualFullWeeklyQuotaHaveDifferentPoses() => InSta(() =>
    {
        var window = new MainWindow(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        var snapshot = Weekly(60);
        snapshot.ResetOpportunityCount = 2;
        snapshot.ResetOpportunityReported = true;
        Invoke(window, "UpdatePetMood", snapshot);
        Assert.Equal("机会在手", Find<TextBlock>(window, "PetMoodText").Text);
        Invoke(window, "ApplySnapshot", snapshot);
        Invoke(window, "ApplySnapshot", Weekly(100));
        Assert.Equal("重置完成！", Find<TextBlock>(window, "PetMoodText").Text);
        Assert.Equal(135.0, Find<RotateTransform>(window, "LeftArmRotate").GetAnimationBaseValue(RotateTransform.AngleProperty));
        Assert.Equal(-135.0, Find<RotateTransform>(window, "RightArmRotate").GetAnimationBaseValue(RotateTransform.AngleProperty));
    });

    [Fact]
    public void InitialFullWeeklyQuotaDoesNotShowResetCompletePose() => InSta(() =>
    {
        var window = new MainWindow(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        Invoke(window, "ApplySnapshot", Weekly(100));
        Assert.NotEqual("重置完成！", Find<TextBlock>(window, "PetMoodText").Text);
    });

    [Fact]
    public void RobotIsNativeVectorWithDistinctiveNewStructure() => InSta(() =>
    {
        var window = new MainWindow(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        Assert.Equal("码力芽芽", window.Title);
        var mascot = Find<Canvas>(window, "OriginalMascot");
        Assert.Equal("原创叶片机器人芽芽", AutomationProperties.GetName(mascot));
        Assert.IsType<Rectangle>(window.FindName("CharacterHead"));
        Assert.IsType<Rectangle>(window.FindName("CharacterScreen"));
        Assert.IsType<System.Windows.Shapes.Path>(window.FindName("LeftLeaf"));
        Assert.IsType<System.Windows.Shapes.Path>(window.FindName("RightLeaf"));
        Assert.IsType<System.Windows.Shapes.Path>(window.FindName("CharacterStatusLight"));
        Assert.Null(window.FindName("CharacterBellGlow"));
        Assert.DoesNotContain(mascot.Children.Cast<UIElement>(), c => c is Image);
        var avatar = Find<Grid>(window, "AvatarContainer");
        avatar.Measure(new Size(72, 108));
        avatar.Arrange(new Rect(0, 0, 72, 108));
        avatar.UpdateLayout();
        var bitmap = new RenderTargetBitmap(288, 432, 384, 384, PixelFormats.Pbgra32);
        bitmap.Render(avatar);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, "original-mascot-preview.png"));
        encoder.Save(output);
    });

    [Fact]
    public void HiddenOrClosingMascotCanStopEveryMotionClock() => InSta(() =>
    {
        var window = new MainWindow(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        Invoke(window, "UpdatePetMood", Weekly(19));
        Invoke(window, "PlayCelebrationAnimation");
        Invoke(window, "StopMascotAnimations");
        Assert.False(Find<ScaleTransform>(window, "CharacterScale").HasAnimatedProperties);
        Assert.False(Find<RotateTransform>(window, "CharacterRotate").HasAnimatedProperties);
        Assert.False(Find<TranslateTransform>(window, "CharacterTranslate").HasAnimatedProperties);
        Assert.False(Find<RotateTransform>(window, "LeftArmRotate").HasAnimatedProperties);
        Assert.False(Find<Ellipse>(window, "CharacterStatusGlow").HasAnimatedProperties);
        Assert.Equal(1, Find<ScaleTransform>(window, "CharacterScale").ScaleX);
    });

    [Theory]
    [InlineData(42, -42)]
    [InlineData(18, -18)]
    [InlineData(7, -7)]
    [InlineData(30, -30)]
    [InlineData(38, -38)]
    [InlineData(65, -65)]
    [InlineData(75, -75)]
    [InlineData(135, -135)]
    public void PosesAndCelebrationFitTheSmallViewport(double leftAngle, double rightAngle) => InSta(() =>
    {
        var window = new MainWindow(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(294, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, 294, root.DesiredSize.Height));
        root.UpdateLayout();
        Find<RotateTransform>(window, "LeftArmRotate").Angle = leftAngle;
        Find<RotateTransform>(window, "RightArmRotate").Angle = rightAngle;
        var viewport = Find<Viewbox>(window, "MascotViewport");
        var mascot = Find<Canvas>(window, "OriginalMascot");
        foreach (var angle in new[] { -4.0, 0.0, 4.0 })
        foreach (var scale in new[] { 1.0, 1.06 })
        foreach (var offset in new[] { 0.0, -9.0 })
        {
            Find<RotateTransform>(window, "CharacterRotate").Angle = angle;
            Find<ScaleTransform>(window, "CharacterScale").ScaleX = scale;
            Find<ScaleTransform>(window, "CharacterScale").ScaleY = scale;
            Find<TranslateTransform>(window, "CharacterTranslate").Y = offset;
            var bounds = mascot.TransformToAncestor(viewport).TransformBounds(VisualTreeHelper.GetDescendantBounds(mascot));
            Assert.True(bounds.Left >= -0.5 && bounds.Right <= viewport.ActualWidth + 0.5 &&
                bounds.Top >= -0.5 && bounds.Bottom <= viewport.ActualHeight + 0.5,
                $"Pose {leftAngle}/{rightAngle}, angle {angle}, scale {scale}, y {offset}: {bounds} outside {viewport.ActualWidth}x{viewport.ActualHeight}");
        }
    });

    private static RateLimitSnapshot Weekly(int remaining) => new()
    {
        Primary = new RateLimitWindow { WindowDurationMins = 10080, UsedPercent = 100 - remaining,
            ResetsAt = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds() }
    };

    private static T Find<T>(MainWindow window, string name) => (T)window.FindName(name);
    private static void Invoke(MainWindow window, string name, params object[] args) => typeof(MainWindow)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    private static void InSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Original mascot check timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
