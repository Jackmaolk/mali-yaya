using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class TiboResetMonitorTests
{
    [Fact]
    public void ConfirmedNoticeUsesChinaTimeAndKeepsEstimateLabel()
    {
        using var document = JsonDocument.Parse("""
            {"activeWindow":{"active":true,"kind":"official","source":"https://x.com/thsottiaux/status/2113492187770175603",
            "expectedAt":"2026-10-23T15:00:00Z","expectedEndAt":"2026-10-23T18:00:00Z"}}
            """);
        var signal = TiboResetMonitor.Classify(
            "But yes... that's a reset!", document.RootElement,
            "https://x.com/thsottiaux/status/2113492187770175603",
            DateTimeOffset.Parse("2026-10-23T04:46:19Z"),
            DateTimeOffset.Parse("2026-10-23T08:00:00Z"),
            DateTimeOffset.Parse("2026-10-23T08:00:00Z"));

        Assert.Equal(TiboSignalKind.Planned, signal.Kind);
        Assert.Contains("社区预计最晚北京时间 10月24日 02:00 前", signal.Detail);
        Assert.DoesNotContain("23:00", signal.Detail);
        Assert.Equal(DateTimeOffset.Parse("2026-10-23T15:00:00Z"), signal.ExpectedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-10-23T18:00:00Z"), signal.ExpectedEndAt);
    }

    [Fact]
    public void EstimateKeepsDeadlineVisibleAfterSuggestedStart()
    {
        using var document = JsonDocument.Parse("""
            {"activeWindow":{"active":true,"kind":"official","source":"https://x.com/thsottiaux/status/2113492187770175603",
            "expectedAt":"2026-10-23T15:00:00Z","expectedEndAt":"2026-10-23T18:00:00Z"}}
            """);
        var now = DateTimeOffset.Parse("2026-10-23T15:30:00Z");
        var signal = TiboResetMonitor.Classify(
            "But yes... that's a reset!", document.RootElement,
            "https://x.com/thsottiaux/status/2113492187770175603",
            DateTimeOffset.Parse("2026-10-23T04:46:19Z"), now, now);

        Assert.Equal(TiboSignalKind.Planned, signal.Kind);
        Assert.Contains("10月24日 02:00 前", signal.Detail);
    }

    [Fact]
    public void ExpiredEstimateDoesNotRecommendFasterUsage()
    {
        using var document = JsonDocument.Parse("""
            {"activeWindow":{"active":true,"kind":"official","source":"https://x.com/thsottiaux/status/2113492187770175603",
            "expectedAt":"2026-10-23T15:00:00Z","expectedEndAt":"2026-10-23T18:00:00Z"}}
            """);
        var signal = TiboResetMonitor.Classify(
            "Reset today", document.RootElement,
            "https://x.com/thsottiaux/status/2113492187770175603",
            DateTimeOffset.Parse("2026-10-23T04:46:19Z"),
            DateTimeOffset.Parse("2026-10-23T21:00:00Z"),
            DateTimeOffset.Parse("2026-10-23T21:00:00Z"));

        Assert.Equal(TiboSignalKind.ExpiredNotice, signal.Kind);
        Assert.DoesNotContain("优先推进", signal.Detail);
    }

    [Fact]
    public void BankedResetPostIsNotReportedAsAlreadyCredited()
    {
        using var document = JsonDocument.Parse("{}");
        var signal = TiboResetMonitor.Classify(
            "A free banked reset is coming for Codex users.", document.RootElement,
            "https://x.com/thsottiaux/status/2113492187770175603",
            DateTimeOffset.Parse("2026-10-23T04:46:19Z"),
            DateTimeOffset.Parse("2026-10-23T08:00:00Z"),
            DateTimeOffset.Parse("2026-10-23T08:00:00Z"));

        Assert.Equal(TiboSignalKind.Bonus, signal.Kind);
        Assert.Contains("是否到账", signal.Detail);
    }

    [Fact]
    public void CompletedPublicWindowIsReportedButNotClaimedForAccount()
    {
        var now = DateTimeOffset.Parse("2026-10-25T06:24:00Z");
        using var document = JsonDocument.Parse("""
            {"latestWindow":{"recordKind":"confirmed_global",
            "source":"https://x.com/thsottiaux/status/2114200152701575656",
            "closedAt":"2026-10-25T06:11:35Z"}}
            """);
        var signal = TiboResetMonitor.Classify("Day 20/ Rest and reset", document.RootElement,
            "https://x.com/thsottiaux/status/2114200152701575656",
            DateTimeOffset.Parse("2026-10-25T03:39:31Z"), now, now);

        Assert.Equal(TiboSignalKind.ObservedReset, signal.Kind);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T06:11:35Z"), signal.ObservedAt);
        Assert.Contains("核对账户", signal.Headline);
        Assert.DoesNotContain("已回满", signal.Detail);
    }

    [Fact]
    public async Task MissingLatestPostStillReadsActiveNotice()
    {
        var now = DateTimeOffset.UtcNow;
        var posted = now.AddHours(-1);
        const string source = "https://x.com/thsottiaux/status/2113492187770175603";
        var feed = JsonSerializer.Serialize(new
        {
            checkedAt = now,
            viewModel = new
            {
                activeWindow = new
                {
                    active = true, kind = "official", source,
                    openedAt = posted, expectedAt = now.AddHours(2),
                    expectedEndAt = now.AddHours(3)
                }
            }
        });
        var post = JsonSerializer.Serialize(new
        {
            tweet = new
            {
                id = "2113492187770175603",
                text = "But yes, that's a reset!",
                created_timestamp = posted.ToUnixTimeSeconds(),
                author = new { screen_name = "thsottiaux" }
            }
        });
        using var client = new HttpClient(new FixedHandler(feed, post));
        using var monitor = new TiboResetMonitor(client);

        var signal = await monitor.CheckAsync();

        Assert.Equal(TiboSignalKind.Planned, signal.Kind);
        Assert.Contains("北京时间", signal.Detail);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GenericLatestPostShowsVerifiedOriginalText(bool topLevelActivity)
    {
        var now = DateTimeOffset.UtcNow;
        var posted = now.AddMinutes(-30);
        const string source = "https://x.com/thsottiaux/status/2113492187770175603";
        var activity = new { sourceUrl = source, createdAt = posted };
        var model = topLevelActivity ? new { } as object : new { latestTiboActivity = activity };
        var body = new Dictionary<string, object>
        {
            ["checkedAt"] = now, ["viewModel"] = model
        };
        if (topLevelActivity) body["latestTiboActivity"] = activity;
        var feed = JsonSerializer.Serialize(body);
        var post = JsonSerializer.Serialize(new
        {
            tweet = new
            {
                id = "2113492187770175603", text = "Shipping Codex improvements today",
                created_timestamp = posted.ToUnixTimeSeconds(),
                author = new { screen_name = "thsottiaux" }
            }
        });
        var handler = new FixedHandler(feed, post);
        using var client = new HttpClient(handler);
        using var monitor = new TiboResetMonitor(client);

        var signal = await monitor.CheckAsync();

        Assert.Equal(TiboSignalKind.RecentPost, signal.Kind);
        Assert.Contains("Shipping Codex improvements", signal.Detail);
        Assert.Equal(source, signal.SourceUrl);
        await monitor.CheckAsync();
        Assert.Equal(1, handler.PostRequestCount);
    }

    [Fact]
    public async Task FallsBackToSecondVerifiedMirrorWhenFirstRejectsRequest()
    {
        var now = DateTimeOffset.UtcNow;
        var posted = now.AddMinutes(-25);
        const string source = "https://x.com/thsottiaux/status/2113492187770175603";
        var feed = JsonSerializer.Serialize(new
        {
            checkedAt = now,
            viewModel = new { latestTiboActivity = new { sourceUrl = source, createdAt = posted } }
        });
        var fallbackPost = JsonSerializer.Serialize(new
        {
            tweetID = "2113492187770175603", user_screen_name = "thsottiaux",
            date_epoch = posted.ToUnixTimeSeconds(), text = "A newer Codex update"
        });
        using var client = new HttpClient(new FallbackHandler(feed, fallbackPost));
        using var monitor = new TiboResetMonitor(client);

        var signal = await monitor.CheckAsync();

        Assert.Equal(TiboSignalKind.RecentPost, signal.Kind);
        Assert.Contains("newer Codex update", signal.Detail);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("author")]
    [InlineData("schema")]
    public async Task PartialLatestFailurePreservesObservedSignalAndReportsDegradation(string failure)
    {
        var now = DateTimeOffset.UtcNow;
        var olderTime = now.AddHours(-1);
        const string olderId = "2113492187770175603";
        const string latestId = "2113492187770175604";
        var olderUrl = $"https://x.com/thsottiaux/status/{olderId}";
        var feed = JsonSerializer.Serialize(new
        {
            checkedAt = now,
            latestTiboActivity = new
            {
                sourceUrl = $"https://x.com/thsottiaux/status/{latestId}", createdAt = now.AddMinutes(-10)
            },
            viewModel = new
            {
                latestWindow = new
                {
                    recordKind = "confirmed_global", source = olderUrl,
                    openedAt = olderTime, closedAt = now.AddMinutes(-20)
                }
            }
        });
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            if (request.RequestUri!.Host == "codex.gussuriworks.com") return JsonResponse(feed);
            if (request.RequestUri.AbsolutePath.EndsWith(olderId, StringComparison.Ordinal))
                return JsonResponse(VerifiedPost(olderId, olderTime, "Rest and reset"));
            return failure switch
            {
                "network" => new HttpResponseMessage(HttpStatusCode.BadGateway),
                "author" => JsonResponse(VerifiedPost(latestId, now.AddMinutes(-10), "New post", "someone_else")),
                _ => JsonResponse("{\"tweet\":[]}")
            };
        }));
        using var monitor = new TiboResetMonitor(client);

        var signal = await monitor.CheckAsync();

        Assert.Equal(TiboSignalKind.ObservedReset, signal.Kind);
        Assert.Equal(olderUrl, signal.SourceUrl);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(olderTime.ToUnixTimeSeconds()), signal.PostTime);
        Assert.Equal(now.AddMinutes(-20), signal.ObservedAt);
        Assert.True(signal.VerificationDegraded);
        Assert.Null(signal.LatestPost);
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public async Task InvalidPrimaryUnixTimestampFallsBackToValidSecondaryMirror(long invalidTimestamp)
    {
        var now = DateTimeOffset.UtcNow;
        var posted = now.AddMinutes(-30);
        const string id = "2113492187770175603";
        var feed = LatestFeed(now, id, posted);
        var hosts = new List<string>();
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            var host = request.RequestUri!.Host;
            hosts.Add(host);
            return JsonResponse(host switch
            {
                "codex.gussuriworks.com" => feed,
                "api.fxtwitter.com" => JsonSerializer.Serialize(new
                {
                    tweet = new
                    {
                        id, text = "Bad timestamp", created_timestamp = invalidTimestamp,
                        author = new { screen_name = "thsottiaux" }
                    }
                }),
                _ => JsonSerializer.Serialize(new
                {
                    tweetID = id, user_screen_name = "thsottiaux",
                    date_epoch = posted.ToUnixTimeSeconds(), text = "Verified secondary post"
                })
            });
        }));
        using var monitor = new TiboResetMonitor(client);

        var signal = await monitor.CheckAsync();

        Assert.Equal(TiboSignalKind.RecentPost, signal.Kind);
        Assert.Contains("Verified secondary post", signal.Detail);
        Assert.False(signal.VerificationDegraded);
        Assert.Equal(new[] { "codex.gussuriworks.com", "api.fxtwitter.com", "api.vxtwitter.com" }, hosts);
    }

    [Fact]
    public async Task InvalidCandidateWithoutUsableContentIsUnavailableAndDegraded()
    {
        var now = DateTimeOffset.UtcNow;
        const string id = "2113492187770175603";
        var feed = LatestFeed(now, id, now.AddMinutes(-30));
        using var client = new HttpClient(new RoutingHandler(request => JsonResponse(
            request.RequestUri!.Host == "codex.gussuriworks.com" ? feed : "{}")));
        using var monitor = new TiboResetMonitor(client);

        var signal = await monitor.CheckAsync();

        Assert.Equal(TiboSignalKind.Unavailable, signal.Kind);
        Assert.True(signal.VerificationDegraded);
    }

    [Fact]
    public async Task CachedPostCannotBypassChangedFeedTimestampValidation()
    {
        var now = DateTimeOffset.UtcNow;
        var posted = now.AddMinutes(-30);
        const string id = "2113492187770175603";
        var feed = LatestFeed(now, id, posted);
        var postRequests = 0;
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            if (request.RequestUri!.Host == "codex.gussuriworks.com") return JsonResponse(feed);
            postRequests++;
            return JsonResponse(VerifiedPost(id, posted));
        }));
        using var monitor = new TiboResetMonitor(client);
        var original = await monitor.CheckAsync();
        feed = LatestFeed(now, id, posted.AddMinutes(-30));

        var signal = await monitor.CheckAsync();

        Assert.Equal(TiboSignalKind.RecentPost, signal.Kind);
        Assert.True(signal.VerificationDegraded);
        Assert.Equal(original.LatestPost, signal.LatestPost);
        Assert.Contains("最近已核实", signal.Headline);
        Assert.DoesNotContain("最新已核实", signal.Headline);
        Assert.Equal(1, postRequests);
    }

    [Fact]
    public async Task MissingLatestActivityUsesCachedPostWithoutClaimingFreshLatestVerification()
    {
        var now = DateTimeOffset.UtcNow;
        var posted = now.AddMinutes(-30);
        const string id = "2113492187770175603";
        var feed = LatestFeed(now, id, posted);
        var postRequests = 0;
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            if (request.RequestUri!.Host == "codex.gussuriworks.com") return JsonResponse(feed);
            postRequests++;
            return JsonResponse(VerifiedPost(id, posted));
        }));
        using var monitor = new TiboResetMonitor(client);
        var original = await monitor.CheckAsync();
        feed = JsonSerializer.Serialize(new { checkedAt = now, viewModel = new { } });

        var signal = await monitor.CheckAsync();

        Assert.Equal(TiboSignalKind.RecentPost, signal.Kind);
        Assert.Equal(original.LatestPost, signal.LatestPost);
        Assert.Contains("最近已核实", signal.Headline);
        Assert.DoesNotContain("最新已核实", signal.Headline);
        Assert.False(signal.VerificationDegraded);
        Assert.Equal(1, postRequests);
    }

    [Fact]
    public void DefaultClientDisablesRedirectsAndLimitsResponseBufferToOneMiB()
    {
        using var monitor = new TiboResetMonitor();
        var client = MonitorClient(monitor);
        var handler = Assert.IsType<HttpClientHandler>(typeof(HttpMessageInvoker)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));

        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(1024L * 1024, client.MaxResponseContentBufferSize);
        Assert.Equal(TimeSpan.FromSeconds(12), client.Timeout);
    }

    [Theory]
    [InlineData("codex.gussuriworks.com", 0, false)]
    [InlineData("codex.gussuriworks.com", 1, false)]
    [InlineData("codex.gussuriworks.com", 0, true)]
    [InlineData("codex.gussuriworks.com", 1, true)]
    [InlineData("api.fxtwitter.com", 0, false)]
    [InlineData("api.fxtwitter.com", 1, false)]
    [InlineData("api.fxtwitter.com", 0, true)]
    [InlineData("api.fxtwitter.com", 1, true)]
    [InlineData("api.vxtwitter.com", 0, false)]
    [InlineData("api.vxtwitter.com", 1, false)]
    [InlineData("api.vxtwitter.com", 0, true)]
    [InlineData("api.vxtwitter.com", 1, true)]
    public async Task ResponseBufferBoundaryAppliesToFeedAndBothMirrors(
        string targetHost, int extraBytes, bool unknownLength)
    {
        var now = DateTimeOffset.UtcNow;
        var posted = now.AddMinutes(-30);
        const string id = "2113492187770175603";
        var feed = LatestFeed(now, id, posted);
        var post = VerifiedPost(id, posted);
        var hosts = new List<string>();
        using var defaults = new TiboResetMonitor();
        var limit = MonitorClient(defaults).MaxResponseContentBufferSize;
        Assert.Equal(1024L * 1024, limit);
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            var host = request.RequestUri!.Host;
            hosts.Add(host);
            if (targetHost == "api.vxtwitter.com" && host == "api.fxtwitter.com")
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            var json = host == "codex.gussuriworks.com" ? feed : post;
            if (host != targetHost) return JsonResponse(json);
            var bytes = Encoding.UTF8.GetBytes(json.PadRight(checked((int)limit + extraBytes)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = unknownLength ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes)
            };
        })) { MaxResponseContentBufferSize = limit };
        using var monitor = new TiboResetMonitor(client);

        var signal = await monitor.CheckAsync();

        var rejected = extraBytes > 0 && targetHost != "api.fxtwitter.com";
        Assert.Equal(rejected ? TiboSignalKind.Unavailable : TiboSignalKind.RecentPost, signal.Kind);
        if (rejected)
        {
            Assert.Null(signal.LatestPost);
            Assert.Equal(targetHost == "api.vxtwitter.com", signal.VerificationDegraded);
        }
        else
        {
            Assert.Equal("Verified post", signal.LatestPost?.Text);
            Assert.False(signal.VerificationDegraded);
        }
        if (targetHost == "codex.gussuriworks.com" && extraBytes > 0)
            Assert.Equal(new[] { "codex.gussuriworks.com" }, hosts);
        else if (targetHost == "api.vxtwitter.com" || targetHost == "api.fxtwitter.com" && extraBytes > 0)
            Assert.Equal(new[] { "codex.gussuriworks.com", "api.fxtwitter.com", "api.vxtwitter.com" }, hosts);
        else
            Assert.Equal(new[] { "codex.gussuriworks.com", "api.fxtwitter.com" }, hosts);
    }

    [Theory]
    [InlineData("top-level", 50_000)]
    [InlineData("top-level", 50_001)]
    [InlineData("nested", 50_000)]
    [InlineData("nested", 50_001)]
    [InlineData("active", 50_000)]
    [InlineData("active", 50_001)]
    [InlineData("completed", 50_000)]
    [InlineData("completed", 50_001)]
    public async Task FeedPostTextBoundaryDoesNotReplaceVerifiedMirrorText(string source, int length)
    {
        var now = DateTimeOffset.UtcNow;
        var posted = now.AddMinutes(-30);
        const string id = "2113492187770175603";
        var url = $"https://x.com/thsottiaux/status/{id}";
        var text = "free banked reset".PadRight(length, 'x');
        var activity = new { sourceUrl = url, createdAt = posted, text };
        var model = new Dictionary<string, object>();
        var body = new Dictionary<string, object> { ["checkedAt"] = now, ["viewModel"] = model };
        if (source == "top-level") body["latestTiboActivity"] = activity;
        else if (source == "nested") model["latestTiboActivity"] = activity;
        else if (source == "active") model["activeWindow"] = new
        {
            active = true, kind = "official", source = url, openedAt = posted, text
        };
        else model["latestWindow"] = new
        {
            recordKind = "confirmed_global", source = url, openedAt = posted,
            closedAt = now.AddMinutes(-10), text
        };
        var feed = JsonSerializer.Serialize(body);
        var postRequests = 0;
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            if (request.RequestUri!.Host == "codex.gussuriworks.com") return JsonResponse(feed);
            postRequests++;
            return JsonResponse(VerifiedPost(id, posted));
        }));
        using var monitor = new TiboResetMonitor(client);

        var signal = await monitor.CheckAsync();

        Assert.Equal(length > 50_000 ? TiboSignalKind.Unavailable : TiboSignalKind.RecentPost, signal.Kind);
        Assert.Equal(length > 50_000, signal.VerificationDegraded);
        Assert.Equal(length > 50_000 ? 0 : 1, postRequests);
        var hasLatest = length <= 50_000 && (source is "top-level" or "nested");
        Assert.Equal(hasLatest ? "Verified post" : null, signal.LatestPost?.Text);
        if (length <= 50_000) Assert.Contains("Verified post", signal.Detail);
    }

    [Fact]
    public async Task OversizedLatestFeedTextPreservesAValidObservedWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var posted = now.AddHours(-1);
        const string latestId = "2113492187770175603";
        const string observedId = "2113492187770175604";
        var observedUrl = $"https://x.com/thsottiaux/status/{observedId}";
        var feed = JsonSerializer.Serialize(new
        {
            checkedAt = now,
            latestTiboActivity = new
            {
                sourceUrl = $"https://x.com/thsottiaux/status/{latestId}",
                createdAt = posted, text = new string('x', 50_001)
            },
            viewModel = new
            {
                latestWindow = new
                {
                    recordKind = "confirmed_global", source = observedUrl,
                    openedAt = posted, closedAt = now.AddMinutes(-20)
                }
            }
        });
        var postRequests = new List<string>();
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            if (request.RequestUri!.Host == "codex.gussuriworks.com") return JsonResponse(feed);
            postRequests.Add(request.RequestUri.AbsolutePath);
            return JsonResponse(VerifiedPost(observedId, posted, "Rest and reset"));
        }));
        using var monitor = new TiboResetMonitor(client);

        var signal = await monitor.CheckAsync();

        Assert.Equal(TiboSignalKind.ObservedReset, signal.Kind);
        Assert.Equal(observedUrl, signal.SourceUrl);
        Assert.True(signal.VerificationDegraded);
        Assert.Null(signal.LatestPost);
        Assert.Equal(new[] { $"/thsottiaux/status/{observedId}" }, postRequests);
    }

    [Theory]
    [InlineData(false, 50_000)]
    [InlineData(false, 50_001)]
    [InlineData(true, 50_000)]
    [InlineData(true, 50_001)]
    public async Task MirrorPostTextBoundaryPreservesFallback(bool secondary, int length)
    {
        var now = DateTimeOffset.UtcNow;
        var posted = now.AddMinutes(-30);
        const string id = "2113492187770175603";
        var feed = LatestFeed(now, id, posted);
        var text = new string('x', length);
        var hosts = new List<string>();
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            var host = request.RequestUri!.Host;
            hosts.Add(host);
            if (host == "codex.gussuriworks.com") return JsonResponse(feed);
            if (host == "api.fxtwitter.com") return secondary
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                : JsonResponse(VerifiedPost(id, posted, text));
            return JsonResponse(JsonSerializer.Serialize(new
            {
                tweetID = id, user_screen_name = "thsottiaux", date_epoch = posted.ToUnixTimeSeconds(),
                text = secondary ? text : "Verified fallback"
            }));
        }));
        using var monitor = new TiboResetMonitor(client);

        var signal = await monitor.CheckAsync();

        var rejected = secondary && length > 50_000;
        Assert.Equal(rejected ? TiboSignalKind.Unavailable : TiboSignalKind.RecentPost, signal.Kind);
        Assert.Equal(rejected, signal.VerificationDegraded);
        Assert.Equal(rejected ? null : length <= 50_000 ? text : "Verified fallback", signal.LatestPost?.Text);
        Assert.Equal(secondary || length > 50_000 ? 3 : 2, hosts.Count);
    }

    [Fact]
    public async Task OversizedMirrorTextIsNotCachedAndCanRecoverOnNextCheck()
    {
        var now = DateTimeOffset.UtcNow;
        var posted = now.AddMinutes(-30);
        const string id = "2113492187770175603";
        var feed = LatestFeed(now, id, posted);
        var text = new string('x', 50_001);
        var postRequests = 0;
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            if (request.RequestUri!.Host == "codex.gussuriworks.com") return JsonResponse(feed);
            postRequests++;
            return JsonResponse(VerifiedPost(id, posted, text));
        }));
        using var monitor = new TiboResetMonitor(client);

        var rejected = await monitor.CheckAsync();
        text = "Recovered post";
        var recovered = await monitor.CheckAsync();

        Assert.Equal(TiboSignalKind.Unavailable, rejected.Kind);
        Assert.True(rejected.VerificationDegraded);
        Assert.Null(rejected.LatestPost);
        Assert.Equal(TiboSignalKind.RecentPost, recovered.Kind);
        Assert.False(recovered.VerificationDegraded);
        Assert.Equal(text, recovered.LatestPost?.Text);
        Assert.Equal(3, postRequests);
    }

    private static HttpClient MonitorClient(TiboResetMonitor monitor) =>
        Assert.IsType<HttpClient>(typeof(TiboResetMonitor)
            .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor));

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(System.IO.Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes, 0, bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private static string LatestFeed(DateTimeOffset now, string id, DateTimeOffset posted) =>
        JsonSerializer.Serialize(new
        {
            checkedAt = now,
            latestTiboActivity = new { sourceUrl = $"https://x.com/thsottiaux/status/{id}", createdAt = posted },
            viewModel = new { }
        });

    private static string VerifiedPost(string id, DateTimeOffset posted, string text = "Verified post",
        string author = "thsottiaux") => JsonSerializer.Serialize(new
        {
            tweet = new
            {
                id, text, created_timestamp = posted.ToUnixTimeSeconds(), author = new { screen_name = author }
            }
        });

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json)
    };

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }

    private sealed class FixedHandler(string feed, string post) : HttpMessageHandler
    {
        public int PostRequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.Headers.UserAgent.ToString().Contains("MaliYaya/1.0"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var content = request.RequestUri!.Host == "api.fxtwitter.com" ? post : feed;
            if (request.RequestUri.Host == "api.fxtwitter.com") PostRequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }

    private sealed class FallbackHandler(string feed, string fallbackPost) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "api.fxtwitter.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var content = request.RequestUri.Host == "api.vxtwitter.com" ? fallbackPost : feed;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
