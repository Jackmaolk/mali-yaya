using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class TiboChineseBriefClientTests : IDisposable
{
    private const string Id = "2114800277677092936";
    private const string Source = "https://x.com/thsottiaux/status/" + Id;
    private const string Original = "A full original post.\nAnother paragraph with important context.";
    private const string Chinese = "这是一篇完整原帖的中文机译。\n第二段包含重要的上下文，不能只保留开头，也不能据此判定账户额度是否已经重置。";
    private static readonly DateTimeOffset Posted = DateTimeOffset.Parse("2026-10-27T04:00:00Z");
    private static readonly TiboPost Post = new(Original, Source, Posted);
    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), "CodexUsageWidget.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _clock = new(Posted.AddHours(1));

    public TiboChineseBriefClientTests() => Directory.CreateDirectory(_cacheDirectory);

    [Fact]
    public async Task MatchingOriginalReturnsCompleteChineseWithoutChangingThePost()
    {
        var payload = Payload();
        payload["status"]!["text"] = "  A full original post.\r\n\tAnother   paragraph with important context.  ";
        payload["status"]!["translation"]!["text"] = string.Concat(Enumerable.Repeat(Chinese, 4));
        using var handler = Responding(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"https://api.fxtwitter.com/2/status/{Id}?lang=zh", request.RequestUri!.AbsoluteUri);
            Assert.Contains("CodexUsageWidget/1.0", request.Headers.UserAgent.ToString());
            return JsonResponse(payload.ToJsonString());
        });
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        var brief = await client.GetAsync(Post);

        Assert.Equal(string.Concat(Enumerable.Repeat(Chinese, 4)), brief?.Text);
        Assert.Equal("grok", brief?.Provider);
        Assert.Equal(Original, Post.Text);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("zh")]
    [InlineData("zh-cn")]
    [InlineData("zh-hans")]
    public async Task SupportedChineseTargetLanguagesAreAccepted(string language)
    {
        var payload = Payload();
        payload["status"]!["translation"]!["target_lang"] = language;
        using var http = new HttpClient(Responding(_ => JsonResponse(payload.ToJsonString())));
        using var client = CreateClient(http);

        Assert.Equal(Chinese, (await client.GetAsync(Post))?.Text);
    }

    [Theory]
    [InlineData("http://x.com/thsottiaux/status/2114800277677092936")]
    [InlineData("https://evil.example/thsottiaux/status/2114800277677092936")]
    [InlineData("https://x.com.evil.example/thsottiaux/status/2114800277677092936")]
    [InlineData("https://x.com@evil.example/thsottiaux/status/2114800277677092936")]
    [InlineData("https://evil.example@x.com/thsottiaux/status/2114800277677092936")]
    [InlineData("https://x.com:443/thsottiaux/status/2114800277677092936")]
    [InlineData("https://x.com/other/status/2114800277677092936")]
    [InlineData("https://x.com/thsottiaux/status/12345678901")]
    [InlineData("https://x.com/thsottiaux/status/12345678901234567890123")]
    [InlineData("https://x.com/thsottiaux/status/２１１４８００２７７６７７０９２９３６")]
    [InlineData(Source + "?lang=zh")]
    [InlineData(Source + "#fragment")]
    [InlineData(Source + "/")]
    [InlineData(Source + "\n")]
    public async Task InvalidSourceUrlsNeverMakeRequests(string source)
    {
        using var handler = Responding(_ => JsonResponse(Payload().ToJsonString()));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        Assert.Null(await client.GetAsync(Post with { SourceUrl = source }));
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("author")]
    [InlineData("id")]
    [InlineData("original")]
    [InlineData("truncated-original")]
    [InlineData("language")]
    [InlineData("no-han")]
    [InlineData("empty-translation")]
    [InlineData("oversized-translation")]
    [InlineData("provider")]
    [InlineData("code")]
    [InlineData("old-time")]
    [InlineData("future-time")]
    [InlineData("out-of-range-time")]
    [InlineData("missing-time")]
    public async Task UnverifiedOrInvalidTranslationsAreRejected(string invalidField)
    {
        var payload = Payload();
        var status = payload["status"]!;
        var translation = status["translation"]!;
        switch (invalidField)
        {
            case "author": status["author"]!["screen_name"] = "someone_else"; break;
            case "id": status["id"] = "2114800277677092937"; break;
            case "original": status["text"] = Original.ToUpperInvariant(); break;
            case "truncated-original": status["text"] = "A full original post."; break;
            case "language": translation["target_lang"] = "zh-tw"; break;
            case "no-han": translation["text"] = "This is not a Chinese translation."; break;
            case "empty-translation": translation["text"] = " "; break;
            case "oversized-translation": translation["text"] = new string('\u4E2D', 50_001); break;
            case "provider": translation["provider"] = null; break;
            case "code": payload["code"] = 404; break;
            case "old-time": status["created_timestamp"] = Posted.AddSeconds(-601).ToUnixTimeSeconds(); break;
            case "future-time": status["created_timestamp"] = Posted.AddSeconds(601).ToUnixTimeSeconds(); break;
            case "out-of-range-time": status["created_timestamp"] = long.MaxValue; break;
            case "missing-time": status.AsObject().Remove("created_timestamp"); break;
        }
        using var http = new HttpClient(Responding(_ => JsonResponse(payload.ToJsonString())));
        using var client = CreateClient(http);

        Assert.Null(await client.GetAsync(Post));
    }

    [Theory]
    [InlineData(-600)]
    [InlineData(600)]
    public async Task TenMinuteTimestampToleranceIsInclusive(int seconds)
    {
        var payload = Payload();
        payload["status"]!["created_timestamp"] = Posted.AddSeconds(seconds).ToUnixTimeSeconds();
        using var http = new HttpClient(Responding(_ => JsonResponse(payload.ToJsonString())));
        using var client = CreateClient(http);

        Assert.NotNull(await client.GetAsync(Post));
    }

    [Fact]
    public async Task PositiveCacheIsReusedInMemoryAndAfterRestartForSevenDays()
    {
        using var handler = Responding(_ => JsonResponse(Payload().ToJsonString()));
        using var http = new HttpClient(handler);
        using (var client = CreateClient(http))
        {
            var first = await client.GetAsync(Post);
            Assert.Equal(first, await client.GetAsync(Post));
        }
        Assert.True(File.Exists(CachePath));
        Assert.Equal(1, handler.RequestCount);

        _clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        using var restarted = CreateClient(http);
        Assert.Equal(new TiboChineseBrief(Chinese, "grok"), await restarted.GetAsync(Post));
        Assert.Equal(1, handler.RequestCount);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(await restarted.GetAsync(Post));
        Assert.Equal(2, handler.RequestCount);
        Assert.Single(Directory.GetFiles(_cacheDirectory));
    }

    [Fact]
    public async Task ChangedOriginalInvalidatesPersistentCacheAndHasADifferentKey()
    {
        var changed = Post with { Text = Original + " Edited after publication." };
        var actualPost = Post;
        using var handler = Responding(_ => JsonResponse(Payload(actualPost).ToJsonString()));
        using var http = new HttpClient(handler);
        using (var first = CreateClient(http)) Assert.NotNull(await first.GetAsync(Post));
        actualPost = changed;
        using var restarted = CreateClient(http);

        Assert.NotEqual(TiboChineseBriefClient.CacheKey(Post), TiboChineseBriefClient.CacheKey(changed));
        Assert.NotEqual(TiboChineseBriefClient.CacheKey(Post),
            TiboChineseBriefClient.CacheKey(Post with { SourceUrl = Source + "1" }));
        Assert.NotNull(await restarted.GetAsync(changed));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task CachedTranslationCannotBypassChangedPostTimestamp()
    {
        using var handler = Responding(_ => JsonResponse(Payload().ToJsonString()));
        using var http = new HttpClient(handler);
        using (var first = CreateClient(http)) Assert.NotNull(await first.GetAsync(Post));
        using var restarted = CreateClient(http);

        Assert.Null(await restarted.GetAsync(Post with { PostTime = Posted.AddMinutes(11) }));
        Assert.Equal(2, handler.RequestCount);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("http-error")]
    [InlineData("timeout")]
    [InlineData("broken-json")]
    [InlineData("wrong-root")]
    public async Task NetworkAndMalformedResponsesReturnNull(string failure)
    {
        using var http = new HttpClient(Responding(_ => failure switch
        {
            "network" => throw new HttpRequestException("Offline"),
            "http-error" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "timeout" => throw new TaskCanceledException("Timed out without caller cancellation"),
            "wrong-root" => JsonResponse("[]"),
            _ => JsonResponse("{broken")
        }));
        using var client = CreateClient(http);

        Assert.Null(await client.GetAsync(Post));
    }

    [Fact]
    public async Task NegativeCacheRetriesAtTwoMinutesAndIsNotPersisted()
    {
        using var handler = Responding(_ => throw new HttpRequestException("Offline"));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        Assert.Null(await client.GetAsync(Post));
        _clock.Advance(TimeSpan.FromSeconds(119));
        Assert.Null(await client.GetAsync(Post));
        Assert.Equal(1, handler.RequestCount);
        Assert.False(File.Exists(CachePath));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(await client.GetAsync(Post));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task CacheWriteFailureDoesNotDiscardNetworkOrMemoryResults()
    {
        var blockedDirectory = Path.Combine(_cacheDirectory, "a-file-not-a-directory");
        File.WriteAllText(blockedDirectory, "blocked");
        using var handler = Responding(_ => JsonResponse(Payload().ToJsonString()));
        using var http = new HttpClient(handler);
        using var client = new TiboChineseBriefClient(http, blockedDirectory, _clock);

        Assert.Equal(Chinese, (await client.GetAsync(Post))?.Text);
        Assert.Equal(Chinese, (await client.GetAsync(Post))?.Text);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("broken-json")]
    [InlineData("wrong-root")]
    [InlineData("null-entry")]
    [InlineData("null-brief")]
    [InlineData("missing-field")]
    [InlineData("not-chinese")]
    [InlineData("oversized-text")]
    [InlineData("oversized-provider")]
    [InlineData("bad-key")]
    [InlineData("future-cache-time")]
    [InlineData("expired")]
    public async Task DamagedOrInvalidCacheFallsBackToNetwork(string damage)
    {
        WriteDamagedCache(damage);
        using var handler = Responding(_ => JsonResponse(Payload().ToJsonString()));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        Assert.Equal(Chinese, (await client.GetAsync(Post))?.Text);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task DamagedCacheAndNetworkFailureTogetherReturnNull()
    {
        File.WriteAllText(CachePath, "{broken");
        using var http = new HttpClient(Responding(_ => throw new HttpRequestException("Offline")));
        using var client = CreateClient(http);

        Assert.Null(await client.GetAsync(Post));
    }

    [Fact]
    public async Task AlreadyCanceledRequestsPropagateEvenWhenPositiveResultIsCached()
    {
        using var handler = Responding(_ => JsonResponse(Payload().ToJsonString()));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);
        Assert.NotNull(await client.GetAsync(Post));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(Post, cancellation.Token));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CancellationDuringRequestPropagatesAndDoesNotCreateNegativeCache()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelFirst = true;
        using var handler = new StubHandler((_, token) =>
        {
            if (!cancelFirst) return Task.FromResult(JsonResponse(Payload().ToJsonString()));
            cancelFirst = false;
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        });
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(Post, cancellation.Token));
        Assert.False(File.Exists(CachePath));
        Assert.NotNull(await client.GetAsync(Post));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneValidatedNetworkResult()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, token) =>
        {
            started.SetResult();
            await release.Task.WaitAsync(token);
            return JsonResponse(Payload().ToJsonString());
        });
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        var first = client.GetAsync(Post);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = client.GetAsync(Post);
        release.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.All(results, brief => Assert.Equal(Chinese, brief?.Text));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CancellationWhileWaitingDoesNotCancelOrPoisonTheActiveRequest()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, token) =>
        {
            started.SetResult();
            await release.Task.WaitAsync(token);
            return JsonResponse(Payload().ToJsonString());
        });
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);
        var first = client.GetAsync(Post);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var waiting = client.GetAsync(Post, cancellation.Token);
        cancellation.Cancel();
        release.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.NotNull(await first);
        Assert.NotNull(await client.GetAsync(Post));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task NegativeCacheAlsoEvictsItsOldestEntryAtThirtyTwoItems()
    {
        using var handler = Responding(_ => throw new HttpRequestException("Offline"));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);
        var posts = Enumerable.Range(0, 33)
            .Select(index => Post with { SourceUrl = $"https://x.com/thsottiaux/status/{2114800277677092936L + index}" })
            .ToArray();
        foreach (var post in posts)
        {
            Assert.Null(await client.GetAsync(post));
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Null(await client.GetAsync(posts[^1]));
        Assert.Equal(33, handler.RequestCount);
        Assert.Null(await client.GetAsync(posts[0]));
        Assert.Equal(34, handler.RequestCount);
        Assert.False(File.Exists(CachePath));
    }

    [Fact]
    public async Task CacheKeepsOnlyTheNewestThirtyTwoSuccessfulEntries()
    {
        using var handler = Responding(request =>
        {
            var id = request.RequestUri!.Segments[^1];
            return JsonResponse(Payload(Post with { SourceUrl = "https://x.com/thsottiaux/status/" + id }).ToJsonString());
        });
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);
        var posts = Enumerable.Range(0, 33)
            .Select(index => Post with { SourceUrl = $"https://x.com/thsottiaux/status/{2114800277677092936L + index}" })
            .ToArray();
        foreach (var post in posts)
        {
            Assert.NotNull(await client.GetAsync(post));
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        using (var cache = JsonDocument.Parse(File.ReadAllText(CachePath)))
            Assert.Equal(32, cache.RootElement.GetArrayLength());
        Assert.NotNull(await client.GetAsync(posts[^1]));
        Assert.Equal(33, handler.RequestCount);
        Assert.NotNull(await client.GetAsync(posts[0]));
        Assert.Equal(34, handler.RequestCount);
    }

    private string CachePath => Path.Combine(_cacheDirectory, "tibo-chinese-cache.json");

    private TiboChineseBriefClient CreateClient(HttpClient http) => new(http, _cacheDirectory, _clock);

    private static JsonObject Payload(TiboPost? post = null)
    {
        post ??= Post;
        return JsonSerializer.SerializeToNode(new
        {
            code = 200,
            status = new
            {
                id = post.SourceUrl.Split('/')[^1], text = post.Text,
                created_timestamp = post.PostTime.ToUnixTimeSeconds(),
                author = new { screen_name = "thsottiaux" },
                translation = new { text = Chinese, target_lang = "zh-cn", provider = "grok" }
            }
        })!.AsObject();
    }

    private void WriteDamagedCache(string damage)
    {
        var entry = JsonSerializer.SerializeToNode(new
        {
            Key = TiboChineseBriefClient.CacheKey(Post),
            Brief = new { Text = "缓存中的机译不应被使用", Provider = "grok" },
            StoredAt = _clock.GetUtcNow(), PostTime = Posted
        })!.AsObject();
        switch (damage)
        {
            case "broken-json": File.WriteAllText(CachePath, "{broken"); return;
            case "wrong-root": File.WriteAllText(CachePath, "{}"); return;
            case "null-entry": File.WriteAllText(CachePath, "[null]"); return;
            case "null-brief": entry["Brief"] = null; break;
            case "missing-field": entry["Brief"]!.AsObject().Remove("Text"); break;
            case "not-chinese": entry["Brief"]!["Text"] = "English only"; break;
            case "oversized-text": entry["Brief"]!["Text"] = new string('\u4E2D', 50_001); break;
            case "oversized-provider": entry["Brief"]!["Provider"] = new string('a', 65); break;
            case "bad-key": entry["Key"] = "not-a-sha256-key"; break;
            case "future-cache-time": entry["StoredAt"] = JsonValue.Create(_clock.GetUtcNow().AddMinutes(1)); break;
            case "expired": entry["StoredAt"] = JsonValue.Create(_clock.GetUtcNow().AddDays(-7)); break;
        }
        File.WriteAllText(CachePath, new JsonArray(entry).ToJsonString());
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static StubHandler Responding(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new((request, _) => Task.FromResult(response(request)));

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            return response(request, cancellationToken);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan interval) => _now += interval;
    }

    public void Dispose() => Directory.Delete(_cacheDirectory, recursive: true);
}
