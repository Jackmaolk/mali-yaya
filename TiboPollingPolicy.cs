namespace CodexUsageWidget;

public sealed record TiboPollingPlan(string Mode, TimeSpan DynamicInterval, TimeSpan QuotaInterval);

public static class TiboPollingPolicy
{
    public static TiboPollingPlan For(
        TiboSignal? signal, DateTimeOffset now, int consecutiveFailures = 0)
    {
        if (consecutiveFailures > 0 || signal?.Kind == TiboSignalKind.Unavailable)
        {
            // Clamp before exponentiation so even a saturated failure counter is safe.
            var exponent = Math.Clamp(consecutiveFailures, 1, 5) - 1;
            return Plan("retry", Math.Min(300, 30 * (1 << exponent)), 15);
        }

        if (signal is null)
            return Plan("normal", 300, 15);

        return signal.Kind switch
        {
            TiboSignalKind.Planned => Planned(signal, now),
            TiboSignalKind.ObservedReset => Plan("awaiting-confirmation", 20, 5),
            TiboSignalKind.ExpiredNotice => Expired(signal, now),
            TiboSignalKind.Confirmed => Plan("confirmed", 180, 15),
            _ => Plan("normal", 300, 15)
        };
    }

    public static TimeSpan WithJitter(TimeSpan interval, double sample)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero);
        // A non-number has no direction; use the neutral midpoint deterministically.
        var boundedSample = double.IsNaN(sample) ? 0.5 : Math.Clamp(sample, 0, 1);
        var ticks = decimal.Round(interval.Ticks * (0.85m + 0.30m * (decimal)boundedSample));
        return TimeSpan.FromTicks((long)Math.Min(ticks, TimeSpan.MaxValue.Ticks));
    }

    private static TiboPollingPlan Planned(TiboSignal signal, DateTimeOffset now)
    {
        // Once the announced start is reached, stay alert throughout the window.
        if (signal.ExpectedAt is { } start && start <= now)
            return Plan("planned-imminent", 20, 5);

        DateTimeOffset? next = null;
        if (signal.ExpectedAt is { } expected && expected > now)
            next = expected;
        if (signal.ExpectedEndAt is { } end && end > now &&
            (next is null || end < next.Value))
            next = end;

        if (next is null || next.Value - now <= TimeSpan.FromMinutes(15))
            return Plan("planned-imminent", 20, 5);
        if (next.Value - now <= TimeSpan.FromHours(2))
            return Plan("planned-soon", 60, 5);
        return Plan("planned-far", 180, 15);
    }

    private static TiboPollingPlan Expired(TiboSignal signal, DateTimeOffset now)
    {
        var expiredAt = signal.ExpectedEndAt ?? signal.ExpectedAt ?? signal.PostTime;
        return expiredAt is { } time && now - time > TimeSpan.FromHours(2)
            ? Plan("expired-stale", 180, 15)
            : Plan("awaiting-confirmation", 20, 5);
    }

    private static TiboPollingPlan Plan(string mode, int dynamicSeconds, int quotaSeconds) =>
        new(mode, TimeSpan.FromSeconds(dynamicSeconds), TimeSpan.FromSeconds(quotaSeconds));
}
