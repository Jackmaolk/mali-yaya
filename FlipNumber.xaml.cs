using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CodexUsageWidget;

public partial class FlipNumber : UserControl
{
    private static readonly TimeSpan HalfFlipTime = TimeSpan.FromMilliseconds(140);
    private ClockGroup? _flipClock;
    private EventHandler? _flipCompleted;
    private long _animationGeneration;
    private bool _hasDisplayedNumber;

    public string Value { get; private set; } = "--";

    public FlipNumber()
    {
        InitializeComponent();
        Unloaded += (_, _) => StopAnimation();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                StopAnimation();
        };
    }

    public void SetNumber(string value, bool animate = true)
    {
        VerifyAccess();
        var next = Normalize(value);
        var canAnimate = animate && IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation;
        if (next == Value)
        {
            if (!canAnimate)
                StopAnimation();
            return;
        }

        var previous = Value;
        var flip = _hasDisplayedNumber && canAnimate;
        StopAnimation();
        Value = next;
        _hasDisplayedNumber |= next != "--";
        AutomationProperties.SetName(this, Value);
        if (flip)
            StartFlip(previous);
        else
            ShowSnapshot();
    }

    public void StopAnimation()
    {
        VerifyAccess();
        // Invalidate callbacks before removing clocks: an old completion cannot settle a new flip.
        ++_animationGeneration;
        if (_flipClock is not null)
        {
            if (_flipCompleted is not null)
                _flipClock.Completed -= _flipCompleted;
            _flipClock.Controller?.Remove();
        }
        _flipClock = null;
        _flipCompleted = null;
        TopFoldScale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, null);
        BottomFoldScale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, null);
        ShowSnapshot();
    }

    private void StartFlip(string previous)
    {
        StaticTopText.Text = Value;
        StaticBottomText.Text = previous;
        OldTopText.Text = previous;
        NewBottomText.Text = Value;
        OldTopHalf.Visibility = Visibility.Visible;
        NewBottomHalf.Visibility = Visibility.Visible;

        var timeline = new ParallelTimeline { Duration = new Duration(HalfFlipTime + HalfFlipTime) };
        timeline.Children.Add(new DoubleAnimation(1, 0, new Duration(HalfFlipTime))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        });
        timeline.Children.Add(new DoubleAnimation(0, 1, new Duration(HalfFlipTime))
        {
            BeginTime = HalfFlipTime,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });

        var generation = _animationGeneration;
        var clock = (ClockGroup)timeline.CreateClock(true);
        _flipClock = clock;
        _flipCompleted = (_, _) =>
        {
            if (generation == _animationGeneration && ReferenceEquals(_flipClock, clock))
                StopAnimation();
        };
        clock.Completed += _flipCompleted;
        TopFoldScale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, (AnimationClock)clock.Children[0]);
        BottomFoldScale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, (AnimationClock)clock.Children[1]);
        clock.Controller?.Begin();
    }

    private void ShowSnapshot()
    {
        StaticTopText.Text = Value;
        StaticBottomText.Text = Value;
        OldTopText.Text = Value;
        NewBottomText.Text = Value;
        OldTopHalf.Visibility = Visibility.Collapsed;
        NewBottomHalf.Visibility = Visibility.Collapsed;
        TopFoldScale.ScaleY = 1;
        BottomFoldScale.ScaleY = 0;
    }

    private static string Normalize(string? value) =>
        value is { Length: 2 } && value[0] is >= '0' and <= '9' && value[1] is >= '0' and <= '9'
            ? value
            : "--";
}
