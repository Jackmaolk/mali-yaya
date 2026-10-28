using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexUsageWidget;

public enum TiboSignalKind { Unavailable, None, RecentPost, Planned, ExpiredNotice, ObservedReset, Bonus, Confirmed }

public sealed record TiboPost(string Text, string SourceUrl, DateTimeOffset PostTime);

public sealed record TiboSignal(
    TiboSignalKind Kind,
    string Headline,
    string Detail,
    string? SourceUrl,
    DateTimeOffset? PostTime,
    DateTimeOffset CheckedAt)
{
    public TiboPost? LatestPost { get; init; }
    public DateTimeOffset? ObservedAt { get; init; }
    public DateTimeOffset? ExpectedAt { get; init; }
    public DateTimeOffset? ExpectedEndAt { get; init; }
    public bool VerificationDegraded { get; init; }
}

public sealed class TiboResetMonitor : IDisposable
{
    private const int MaxPostTextLength = 50_000;
    private static readonly Uri FeedUrl = new("https://codex.gussuriworks.com/api/current");
    private static readonly Regex SourcePattern = new(
        @"^https://x\.com/thsottiaux/status/(\d{12,22})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BonusPattern = new(
        @"\b(banked|bonus|free|complimentary)\s+(?:rate.limit\s+)?resets?\b|\breset\s+credits?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ResetPattern = new(
        @"\breset(?:s|ting)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly TimeZoneInfo ChinaZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
    private readonly HttpClient _client;
    private TiboPost? _lastVerifiedPost;
    private readonly Dictionary<string, (TiboPost Post, DateTimeOffset VerifiedAt)> _verifiedPosts = [];

    public TiboResetMonitor(HttpClient? client = null)
    {
        _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(12),
            MaxResponseContentBufferSize = 1024 * 1024
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexUsageWidget/1.0");
    }

    public async Task<TiboSignal> CheckAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            using var feedResponse = await _client.GetAsync(FeedUrl, cancellationToken);
            feedResponse.EnsureSuccessStatusCode();
            using var feed = JsonDocument.Parse(await feedResponse.Content.ReadAsStreamAsync(cancellationToken));
            var root = feed.RootElement;
            var checkedAt = ReadTime(root, "checkedAt");
            if (checkedAt is null || now - checkedAt > TimeSpan.FromHours(2) ||
                checkedAt - now > TimeSpan.FromMinutes(5))
                return Unavailable(now, "动态来源超过 2 小时未更新");

            var model = root.GetProperty("viewModel");
            var activity = root.TryGetProperty("latestTiboActivity", out var latestActivity)
                ? latestActivity
                : model.TryGetProperty("latestTiboActivity", out latestActivity) ? latestActivity : default;
            var activeWindow = model.TryGetProperty("activeWindow", out var candidate) &&
                               candidate.ValueKind == JsonValueKind.Object &&
                               ReadString(candidate, "kind") == "official" &&
                               candidate.TryGetProperty("active", out var isActive) &&
                               isActive.ValueKind == JsonValueKind.True
                ? candidate : (JsonElement?)null;
            var activityUrl = ReadString(activity, "sourceUrl");
            var posts = new List<(string? Url, DateTimeOffset? PostedAt, bool IsLatest, bool OversizedText)>
            {
                (activityUrl, ReadTime(activity, "createdAt"), true, HasOversizedPostText(activity))
            };
            if (activeWindow is { } window)
                posts.Add((ReadString(window, "source"), ReadTime(window, "openedAt"), false,
                    HasOversizedPostText(window)));
            if (model.TryGetProperty("latestWindow", out var completedWindow) &&
                completedWindow.ValueKind == JsonValueKind.Object &&
                ReadString(completedWindow, "recordKind") == "confirmed_global" &&
                ReadTime(completedWindow, "closedAt") is { } closedAt &&
                now - closedAt <= TimeSpan.FromHours(24))
                posts.Add((ReadString(completedWindow, "source"),
                    ReadTime(completedWindow, "openedAt"), false, HasOversizedPostText(completedWindow)));

            TiboSignal? actionable = null;
            TiboSignal? recent = null;
            TiboPost? latestPost = null;
            var verificationFailed = false;
            foreach (var (url, postedAt, isLatest, oversizedText) in posts.DistinctBy(post => post.Url))
            {
                var maxAge = isLatest ? TimeSpan.FromDays(7) : TimeSpan.FromHours(36);
                if (url is null || postedAt is null || now - postedAt > maxAge ||
                    postedAt - now > TimeSpan.FromMinutes(5))
                    continue;
                var match = SourcePattern.Match(url);
                if (!match.Success) continue;
                if (oversizedText)
                {
                    verificationFailed = true;
                    continue;
                }
                TiboPost? verifiedPost;
                try
                {
                    verifiedPost = await FetchVerifiedPostAsync(match.Groups[1].Value,
                        url, postedAt.Value, cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    verificationFailed = true;
                    continue;
                }
                if (verifiedPost is null)
                {
                    verificationFailed = true;
                    continue;
                }
                if (isLatest)
                {
                    latestPost = verifiedPost;
                    _lastVerifiedPost = verifiedPost;
                }
                var signal = Classify(verifiedPost.Text, model, url,
                    verifiedPost.PostTime, checkedAt.Value, now);
                if (signal.Kind is TiboSignalKind.Bonus or TiboSignalKind.Planned or TiboSignalKind.ExpiredNotice or TiboSignalKind.ObservedReset)
                    actionable ??= signal;
                else
                    recent ??= signal;
            }
            if (_lastVerifiedPost is { } cachedPost && now - cachedPost.PostTime <= TimeSpan.FromDays(7))
                latestPost ??= cachedPost;
            if (verificationFailed && actionable is null && recent is null && latestPost is null)
                return Unavailable(now, "原帖暂时无法核实，自动重试中") with { VerificationDegraded = true };
            return (actionable ?? recent ?? (latestPost is not null
                ? RecentPost(latestPost, checkedAt.Value, cached: true)
                : new(TiboSignalKind.None, "暂无可核实的 Tibo 动态",
                    "继续自动检查公开动态", null, null, checkedAt.Value)))
                with { LatestPost = latestPost, VerificationDegraded = verificationFailed };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return Unavailable(now, "原帖镜像拒绝访问，稍后重试");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Unavailable(now, "动态来源连接失败，稍后重试");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Unavailable(now, "动态来源格式变化，稍后重试");
        }
    }

    private async Task<TiboPost?> FetchVerifiedPostAsync(string id, string url,
        DateTimeOffset postedAt, CancellationToken cancellationToken)
    {
        if (_verifiedPosts.TryGetValue(id, out var cached) &&
            DateTimeOffset.UtcNow - cached.VerifiedAt <= TimeSpan.FromMinutes(5))
            return Math.Abs((cached.Post.PostTime - postedAt).TotalMinutes) <= 10 ? cached.Post : null;
        Exception? lastError = null;
        foreach (var host in new[] { "api.fxtwitter.com", "api.vxtwitter.com" })
        {
            try
            {
                using var response = await _client.GetAsync(
                    $"https://{host}/thsottiaux/status/{id}", cancellationToken);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                var isFx = root.TryGetProperty("tweet", out var tweet);
                var post = isFx ? tweet : root;
                if (post.ValueKind != JsonValueKind.Object) continue;
                var actualId = ReadString(post, isFx ? "id" : "tweetID");
                var author = isFx && post.TryGetProperty("author", out var authorObject)
                    ? ReadString(authorObject, "screen_name")
                    : ReadString(post, "user_screen_name");
                var timestampName = isFx ? "created_timestamp" : "date_epoch";
                if (actualId != id || !string.Equals(author, "thsottiaux", StringComparison.OrdinalIgnoreCase) ||
                    !post.TryGetProperty(timestampName, out var timestamp) ||
                    timestamp.ValueKind != JsonValueKind.Number ||
                    !timestamp.TryGetInt64(out var seconds) ||
                    seconds < DateTimeOffset.MinValue.ToUnixTimeSeconds() ||
                    seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds()) continue;
                var verifiedTime = DateTimeOffset.FromUnixTimeSeconds(seconds);
                if (Math.Abs((verifiedTime - postedAt).TotalMinutes) > 10) continue;
                var text = ReadString(post, "text") ?? "";
                if (text.Length > MaxPostTextLength) continue;
                var verifiedPost = new TiboPost(text, url, verifiedTime);
                _verifiedPosts[id] = (verifiedPost, DateTimeOffset.UtcNow);
                if (_verifiedPosts.Count > 32)
                    _verifiedPosts.Remove(_verifiedPosts.MinBy(entry => entry.Value.VerifiedAt).Key);
                return verifiedPost;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                lastError = ex;
            }
        }
        if (lastError is not null) throw lastError;
        return null;
    }

    public static TiboSignal Classify(
        string postText, JsonElement model, string sourceUrl, DateTimeOffset postTime,
        DateTimeOffset checkedAt, DateTimeOffset now)
    {
        if (BonusPattern.IsMatch(postText))
            return new(TiboSignalKind.Bonus, "Tibo 提到赠送重置机会",
                "是否到账请看下方实际重置次数 · 点此看原帖", sourceUrl, postTime, checkedAt);

        if (model.TryGetProperty("latestWindow", out var observed) &&
            observed.ValueKind == JsonValueKind.Object &&
            ReadString(observed, "source") == sourceUrl &&
            ReadString(observed, "recordKind") == "confirmed_global" &&
            ReadTime(observed, "closedAt") is { } observedAt &&
            observedAt >= postTime && observedAt <= now.AddMinutes(5) &&
            now - observedAt <= TimeSpan.FromHours(6) &&
            ResetPattern.IsMatch(postText))
            return new TiboSignal(TiboSignalKind.ObservedReset, "社区观察到重置 · 核对账户中",
                "公开动态已更新，正在核对本账户实际额度", sourceUrl, postTime, checkedAt)
            { ObservedAt = observedAt };

        if (model.TryGetProperty("activeWindow", out var window) &&
            window.ValueKind == JsonValueKind.Object &&
            ReadString(window, "source") == sourceUrl &&
            ReadString(window, "kind") == "official" &&
            window.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True &&
            ResetPattern.IsMatch(postText))
        {
            var expected = ReadTime(window, "expectedAt");
            var end = ReadTime(window, "expectedEndAt");
            if (end <= now)
                return new TiboSignal(TiboSignalKind.ExpiredNotice, "预告参考时间已过，待确认",
                    "请以账户实际额度为准 · 点此看原帖", sourceUrl, postTime, checkedAt)
                { ExpectedAt = expected, ExpectedEndAt = end };
            var timing = end > now && end - now < TimeSpan.FromHours(24)
                ? $"社区预计最晚北京时间 {ChinaDate(end.Value)} 前"
                : expected > now && expected - now < TimeSpan.FromHours(24)
                    ? $"社区预计北京时间 {ChinaDate(expected.Value)} 后"
                    : "北京时间待确认";
            return new TiboSignal(TiboSignalKind.Planned, "Tibo 预告重置",
                $"{timing} · 正在加密监测", sourceUrl, postTime, checkedAt)
            { ExpectedAt = expected, ExpectedEndAt = end };
        }
        return RecentPost(new TiboPost(postText, sourceUrl, postTime), checkedAt);
    }

    private static TiboSignal RecentPost(TiboPost post, DateTimeOffset checkedAt, bool cached = false)
    {
        var text = Regex.Replace(post.Text, @"\s+", " ").Trim();
        if (text.Length > 72) text = text[..72] + "…";
        return new(TiboSignalKind.RecentPost,
            cached ? "Tibo 最近已核实动态" : $"Tibo 最新已核实动态 · {ChinaDate(post.PostTime)}",
            string.IsNullOrEmpty(text) ? "原帖暂无文字内容，点击查看" : $"原帖：{text}",
            post.SourceUrl, post.PostTime, checkedAt)
        { LatestPost = post };
    }

    private static TiboSignal Unavailable(DateTimeOffset now, string reason) =>
        new(TiboSignalKind.Unavailable, "Tibo 动态暂不可确认", reason, null, null, now);

    private static bool HasOversizedPostText(JsonElement element) =>
        ReadString(element, "text") is { Length: > MaxPostTextLength };

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static DateTimeOffset? ReadTime(JsonElement element, string name) =>
        DateTimeOffset.TryParse(ReadString(element, name), out var value) ? value : null;

    private static string ChinaDate(DateTimeOffset time) =>
        TimeZoneInfo.ConvertTime(time, ChinaZone).ToString("M月d日 HH:mm");

    public void Dispose() => _client.Dispose();
}
