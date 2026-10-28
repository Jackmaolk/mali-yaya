namespace CodexUsageWidget;

// Reconciles a verified public notice with this account's quota snapshots.
public sealed class TiboStatusCoordinator
{
    private TiboSignal? _source;
    private TiboSignal? _confirmed;
    private DateTimeOffset? _confirmedAt;
    private DateTimeOffset? _confirmedCycleStart;
    private long? _confirmedWindowEnd;
    private RateLimitWindow? _previousWeekly;
    private RateLimitSnapshot? _snapshot;
    private DateTimeOffset? _snapshotReceivedAt;
    private bool _celebrationPending;

    public TiboSignal? Display { get; private set; }

    public void OnSignal(TiboSignal signal, DateTimeOffset now)
    {
        if (signal.Kind != TiboSignalKind.Unavailable || _source is null)
            _source = signal;
        Reconcile(now);
    }

    public void OnSnapshot(RateLimitSnapshot snapshot, DateTimeOffset now)
    {
        _snapshot = snapshot;
        _snapshotReceivedAt = now;
        Reconcile(now);
        var weekly = UsageWindowSelector.FindWeekly(snapshot);
        _previousWeekly = weekly is null ? null : new RateLimitWindow
        {
            UsedPercent = weekly.UsedPercent,
            ResetsAt = weekly.ResetsAt,
            WindowDurationMins = weekly.WindowDurationMins
        };
    }

    public bool TakeCelebration()
    {
        if (!_celebrationPending) return false;
        _celebrationPending = false;
        return true;
    }

    private void Reconcile(DateTimeOffset now)
    {
        var weekly = _snapshot is null ? null : UsageWindowSelector.FindWeekly(_snapshot);
        if (_snapshot is not null && _snapshotReceivedAt is { } receivedAt &&
            now - receivedAt <= TimeSpan.FromMinutes(2) &&
            IsNewConfirmationCycle(weekly, now) &&
            TiboResetConfirmationPolicy.IsAccountRestored(_source, _previousWeekly, _snapshot, now))
        {
            var basis = _source ?? new(TiboSignalKind.None, "", "", null, null, now);
            _confirmed = basis with
            {
                Kind = TiboSignalKind.Confirmed,
                Headline = "本账户重置已完成 · 庆祝！",
                Detail = "已确认周额度回满，叮当继续监测 Tibo 新动态"
            };
            _confirmedAt = now;
            var hasStart = TiboResetConfirmationPolicy.TryWindowStart(weekly, out var start);
            _confirmedWindowEnd = hasStart ? weekly!.ResetsAt : null;
            _confirmedCycleStart = hasStart &&
                                   now - start <= TimeSpan.FromHours(6) ? start : now;
            if (hasStart)
                _confirmed = _confirmed with
                {
                    Detail = $"本轮周周期：{start.ToOffset(TimeSpan.FromHours(8)):M月d日 HH:mm} 开始 · 重置已完成"
                };
            _celebrationPending = true;
        }

        if (_confirmed is not null)
        {
            if (_confirmed.SourceUrl is null && _source?.SourceUrl is not null &&
                _source.Kind is TiboSignalKind.Planned or TiboSignalKind.ObservedReset or TiboSignalKind.ExpiredNotice &&
                _source.PostTime <= _confirmedCycleStart)
                _confirmed = _confirmed with
                {
                    SourceUrl = _source.SourceUrl, PostTime = _source.PostTime,
                    LatestPost = _source.LatestPost, CheckedAt = _source.CheckedAt
                };
            // Compare new posts to the account recovery, not the earlier announcement.
            if (weekly?.UsedPercent == 0 && _snapshotReceivedAt is { } freshAt &&
                now - freshAt <= TimeSpan.FromMinutes(2))
                Display = _confirmed;
            else if (_source?.LatestPost is { } latest && latest.SourceUrl != _confirmed.SourceUrl &&
                latest.PostTime > _confirmedCycleStart)
                Display = RecentPost(latest, _source.CheckedAt);
            else if (_source?.Kind is TiboSignalKind.Planned or TiboSignalKind.Bonus &&
                     _source.PostTime > _confirmedCycleStart)
                Display = _source;
            else if (_confirmedAt is { } confirmedAt && now - confirmedAt <= TimeSpan.FromHours(6))
                Display = _confirmed;
            else
                Display = _source?.LatestPost is { } post
                    ? RecentPost(post, _source.CheckedAt) : _source;
            return;
        }

        Display = _source;
    }

    private bool IsNewConfirmationCycle(RateLimitWindow? weekly, DateTimeOffset now)
    {
        if (_confirmed is null) return true;
        if (weekly?.ResetsAt is not { } currentEnd ||
            !TiboResetConfirmationPolicy.TryWindowStart(weekly, out var start) ||
            start > now.AddMinutes(2)) return false;
        if (_confirmedWindowEnd is { } knownEnd)
        {
            var movement = (decimal)currentEnd - knownEnd;
            return movement > 60 && (movement > 600 ||
                (_previousWeekly is { } previous && previous.UsedPercent > weekly.UsedPercent));
        }
        // A newly supplied ID can enrich an earlier anonymous full reading. A cycle
        // beginning well after that reading is a distinct event rather than enrichment.
        if (_confirmedAt is { } confirmedAt && start > confirmedAt.AddMinutes(10)) return true;
        _confirmedWindowEnd = currentEnd;
        return false;
    }

    private static TiboSignal RecentPost(TiboPost post, DateTimeOffset checkedAt)
    {
        var excerpt = string.Join(" ", post.Text.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        if (excerpt.Length > 58) excerpt = excerpt[..58] + "…";
        return new(TiboSignalKind.RecentPost, "Tibo 最新已核实动态",
            string.IsNullOrWhiteSpace(excerpt) ? "点击查看原帖" : excerpt,
            post.SourceUrl, post.PostTime, checkedAt) { LatestPost = post };
    }
}

public static class TiboResetConfirmationPolicy
{
    public static bool IsAccountRestored(TiboSignal? notice, RateLimitWindow? previousWeekly,
        RateLimitSnapshot current, DateTimeOffset now)
    {
        var weekly = UsageWindowSelector.FindWeekly(current);
        // Product rule: a fresh weekly reading at 100% ends account verification immediately.
        if (weekly?.UsedPercent == 0) return true;
        if (!TryWindowStart(weekly, out var weeklyStart) || weekly is null ||
            weekly.WindowDurationMins is not >= 10000 ||
            weeklyStart > now.AddMinutes(2) || now - weeklyStart > TimeSpan.FromHours(6))
            return false;

        // Local account evidence has priority over approximate community timestamps.
        if (previousWeekly is { ResetsAt: not null } &&
            previousWeekly.WindowDurationMins == weekly.WindowDurationMins &&
            previousWeekly.UsedPercent > weekly.UsedPercent &&
            TryWindowStart(previousWeekly, out _) &&
            (decimal)weekly.ResetsAt!.Value - previousWeekly.ResetsAt.Value > 60)
            return true;

        // Cold starts can miss the 100% sample. Synchronized fresh week/5h origins
        // still identify the new account cycle after some allowance has been spent.
        var fiveHour = UsageWindowSelector.FindFiveHour(current);
        if (TryWindowStart(fiveHour, out var fiveHourStart) &&
            Math.Abs((fiveHourStart - weeklyStart).TotalMinutes) <= 10)
            return true;

        if (notice?.Kind is not (TiboSignalKind.Planned or TiboSignalKind.ExpiredNotice or TiboSignalKind.ObservedReset) ||
            notice.PostTime is not { } postTime || postTime > now || weeklyStart < postTime.AddMinutes(-5))
            return false;
        var observed = notice.Kind == TiboSignalKind.ObservedReset;
        if (observed && (notice.ObservedAt is not { } observation || observation < postTime ||
                         observation > now.AddMinutes(2) || now - observation > TimeSpan.FromHours(24)))
            return false;

        // A weekly-only account can still be confirmed against a recent public observation.
        return observed && notice.ObservedAt is { } observedAt &&
            Math.Abs((weeklyStart - observedAt).TotalMinutes) <= 30;
    }

    internal static bool TryWindowStart(RateLimitWindow? window, out DateTimeOffset start)
    {
        start = default;
        if (window is not { UsedPercent: >= 0 and <= 100, ResetsAt: not null,
                            WindowDurationMins: > 0 and <= 525600 }) return false;
        try
        {
            start = DateTimeOffset.FromUnixTimeSeconds(window.ResetsAt.Value)
                .AddMinutes(-window.WindowDurationMins.Value);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}
