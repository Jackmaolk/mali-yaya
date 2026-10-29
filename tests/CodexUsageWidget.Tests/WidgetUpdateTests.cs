using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class WidgetUpdateTests : IDisposable
{
    private const string Repository = "Jackmaolk/mali-yaya";
    private const string ReleaseVersion = "2099.12.31.9";
    private const string Tag = "v" + ReleaseVersion;
    private const string AssetName = "mali-yaya-" + ReleaseVersion + "-windows-x64.zip";
    private const string DownloadUrl = "https://github.com/" + Repository + "/releases/download/" + Tag + "/" + AssetName;
    private static readonly string[] Names =
    [
        "CodexUsageWidget.dll", "CodexUsageWidget.exe",
        "CodexUsageWidget.deps.json", "CodexUsageWidget.runtimeconfig.json"
    ];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CodexUsageWidget.UpdateTests", Guid.NewGuid().ToString("N"));
    private readonly ManualClock _clock = new();
    private static WidgetUpdateOptions Enabled => new(true, Repository);

    [Fact]
    public async Task ValidReleaseStagesOnlyFourVerifiedFilesWithoutInstalling()
    {
        var zip = Zip();
        using var handler = Handler(zip);
        using var http = new HttpClient(handler);
        using var service = Create(http);
        var statuses = new List<string>();
        service.StatusChanged += statuses.Add;

        var result = await service.CheckAsync(Enabled);

        Assert.NotNull(result);
        Assert.Equal(ReleaseVersion, result.Version);
        Assert.Equal("payload", Path.GetFileName(result.PayloadDirectory));
        Assert.StartsWith(Path.GetFullPath(_directory) + Path.DirectorySeparatorChar, result.PayloadDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("staged-", Path.GetFileName(Path.GetDirectoryName(result.PayloadDirectory)));
        Assert.Equal(Names.Order(), Directory.GetFiles(result.PayloadDirectory).Select(Path.GetFileName).Order());
        Assert.Equal(4, result.Hashes.Count);
        foreach (var name in Names)
            Assert.Equal(Hash(File.ReadAllBytes(Path.Combine(result.PayloadDirectory, name))), result.Hashes[name]);
        Assert.Equal(_clock.GetUtcNow(), service.State.LastAttemptUtc);
        Assert.NotEmpty(statuses);
        Assert.Equal(2, handler.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Jackmaolk")]
    [InlineData("Jackmaolk/../mali-yaya")]
    [InlineData("Jackmaolk/mali-yaya --token=x")]
    [InlineData(" Jackmaolk/mali-yaya")]
    [InlineData("Jackmaolk/mali-yaya\n")]
    [InlineData("Jackmaolk/.git")]
    [InlineData("Jackmaolk/..")]
    [InlineData("https://github.com/Jackmaolk/mali-yaya")]
    [InlineData("foreign/mali-yaya")]
    [InlineData("steipete/CodexBar")]
    public async Task MissingOrUntrustedRepositoryNeverNetworks(string? repository)
    {
        using var handler = Handler(Zip());
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(new(true, repository)));
        Assert.Equal(0, handler.Count);
        Assert.Null(service.State.LastAttemptUtc);
    }

    [Fact]
    public async Task DisabledDefaultsDoNotCreateState()
    {
        using var handler = Handler(Zip());
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(new()));
        Assert.Equal(new WidgetUpdateState(), service.State);
        Assert.False(Directory.Exists(_directory));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task FailedAttemptIsStampedBeforeNetworkAndCooldownSurvivesRestartAndRepositoryChange()
    {
        using var handler = new StubHandler((_, _) =>
        {
            using var observerHttp = new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException()));
            using var observer = Create(observerHttp);
            Assert.Equal(_clock.GetUtcNow(), observer.State.LastAttemptUtc);
            throw new HttpRequestException("secret remote detail");
        });
        using var http = new HttpClient(handler);
        using (var first = Create(http)) Assert.Null(await first.CheckAsync(Enabled));
        using var restarted = Create(http);
        _clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromTicks(1));
        Assert.Null(await restarted.CheckAsync(new(true, "Jackmaolk/another-repo")));
        Assert.Equal(1, handler.Count);
        _clock.Advance(TimeSpan.FromTicks(1));
        Assert.Null(await restarted.CheckAsync(Enabled));
        Assert.Equal(2, handler.Count);
        Assert.DoesNotContain("secret", restarted.State.Status);
    }

    [Fact]
    public async Task ConcurrentInstancesMakeOnlyOneAttempt()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return new(HttpStatusCode.ServiceUnavailable);
        });
        using var http = new HttpClient(handler);
        using var first = Create(http);
        using var second = Create(http);
        var active = first.CheckAsync(Enabled);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var callers = Enumerable.Range(0, 10).Select(_ => first.CheckAsync(Enabled)).Append(second.CheckAsync(Enabled)).ToArray();
        release.SetResult();
        await Task.WhenAll(callers.Append(active)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"LastAttemptUtc\":null,\"Status\":\"ignored\"}")]
    [InlineData("{\"LastAttemptUtc\":\"not-a-date\",\"Status\":\"ignored\"}")]
    public async Task CorruptStateCannotClearCooldown(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "update-state.json"), content);
        using var handler = Handler(Zip());
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(Enabled));
        using var restarted = Create(http);
        Assert.Null(await restarted.CheckAsync(Enabled));
        Assert.Equal(0, handler.Count);
        Assert.Equal(content, File.ReadAllText(Path.Combine(_directory, "update-state.json")));
    }

    [Fact]
    public async Task StateWriteFailureNeverNetworks()
    {
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(Path.Combine(_directory, "update-state.json"));
        using var handler = Handler(Zip());
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(Enabled));
        Assert.Equal(0, handler.Count);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("prerelease")]
    [InlineData("missing-draft")]
    [InlineData("missing-prerelease")]
    [InlineData("old")]
    [InlineData("equal")]
    [InlineData("tag")]
    [InlineData("bad-date")]
    [InlineData("tag-leading-zero")]
    [InlineData("name")]
    [InlineData("missing-digest")]
    [InlineData("digest")]
    [InlineData("digest-prefix")]
    [InlineData("size-zero")]
    [InlineData("size-large")]
    [InlineData("size-string")]
    [InlineData("not-uploaded")]
    [InlineData("duplicate")]
    [InlineData("url-owner")]
    [InlineData("url-repo")]
    [InlineData("url-tag")]
    [InlineData("url-name")]
    [InlineData("url-query")]
    [InlineData("url-userinfo")]
    [InlineData("url-fragment")]
    [InlineData("url-port")]
    [InlineData("url-http")]
    public async Task UntrustedReleaseMetadataIsRejectedBeforeDownload(string defect)
    {
        var zip = Zip();
        var release = Release(zip);
        var asset = release["assets"]![0]!;
        switch (defect)
        {
            case "draft": release["draft"] = true; break;
            case "prerelease": release["prerelease"] = true; break;
            case "missing-draft": release.Remove("draft"); break;
            case "missing-prerelease": release.Remove("prerelease"); break;
            case "old": release["tag_name"] = "v2000.01.01.1"; break;
            case "equal": release["tag_name"] = "v" + WidgetRuntimeStatus.BuildVersion; break;
            case "tag": release["tag_name"] = Tag + "-beta"; break;
            case "bad-date": release["tag_name"] = "v2099.02.30.1"; break;
            case "tag-leading-zero": release["tag_name"] = "v2099.12.31.09"; break;
            case "name": asset["name"] = "other.zip"; break;
            case "missing-digest": asset.AsObject().Remove("digest"); break;
            case "digest": asset["digest"] = "sha256:" + new string('x', 64); break;
            case "digest-prefix": asset["digest"] = "SHA256:" + Hash(zip); break;
            case "size-zero": asset["size"] = 0; break;
            case "size-large": asset["size"] = 20 * 1024 * 1024 + 1; break;
            case "size-string": asset["size"] = zip.Length.ToString(); break;
            case "not-uploaded": asset["state"] = "new"; break;
            case "duplicate": release["assets"]!.AsArray().Add(asset.DeepClone()); break;
            case "url-owner": asset["browser_download_url"] = DownloadUrl.Replace("Jackmaolk", "foreign"); break;
            case "url-repo": asset["browser_download_url"] = DownloadUrl.Replace("/mali-yaya/", "/other/"); break;
            case "url-tag": asset["browser_download_url"] = DownloadUrl.Replace(Tag, "v2099.12.31.8"); break;
            case "url-name": asset["browser_download_url"] = DownloadUrl.Replace(AssetName, "other.zip"); break;
            case "url-query": asset["browser_download_url"] = DownloadUrl + "?x=1"; break;
            case "url-userinfo": asset["browser_download_url"] = DownloadUrl.Replace("https://", "https://user@"); break;
            case "url-fragment": asset["browser_download_url"] = DownloadUrl + "#x"; break;
            case "url-port": asset["browser_download_url"] = DownloadUrl.Replace("github.com", "github.com:443"); break;
            case "url-http": asset["browser_download_url"] = DownloadUrl.Replace("https:", "http:"); break;
        }
        using var handler = Handler(zip, release);
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(Enabled));
        Assert.Equal(1, handler.Count);
        AssertNoStage();
    }

    [Theory]
    [InlineData("https://evil.example/payload")]
    [InlineData("https://release-assets.githubusercontent.com.evil.example/payload")]
    [InlineData("http://release-assets.githubusercontent.com/payload")]
    [InlineData("https://user@objects.githubusercontent.com/payload")]
    [InlineData("https://objects.githubusercontent.com:444/payload")]
    [InlineData("https://objects.githubusercontent.com/payload#fragment")]
    [InlineData("/relative")]
    public async Task DownloadRejectsUntrustedRedirectWithoutFollowingIt(string location)
    {
        var zip = Zip();
        using var handler = new StubHandler((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com"
            ? Json(Release(zip)) : Redirect(location)));
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(Enabled));
        Assert.Equal(2, handler.Count);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task AtMostThreeExplicitCdnRedirectsAreAllowed(int redirects, bool accepted)
    {
        var zip = Zip();
        var remaining = redirects;
        using var handler = new StubHandler((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com"
            ? Json(Release(zip)) : remaining-- > 0
                ? Redirect("https://" + (remaining % 2 == 0 ? "objects.githubusercontent.com" : "release-assets.githubusercontent.com") + "/payload?signature=fixture")
                : Bytes(zip)));
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.Equal(accepted, await service.CheckAsync(Enabled) is not null);
        Assert.Equal(accepted ? redirects + 2 : 5, handler.Count);
    }

    [Fact]
    public async Task MetadataRedirectsAreNeverFollowed()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Redirect("https://objects.githubusercontent.com/data")));
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(Enabled));
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("content-length")]
    [InlineData("not-zip")]
    public async Task DownloadMustMatchDigestAndExactAnnouncedSizeBeforeZipParsing(string defect)
    {
        var zip = defect == "not-zip" ? Encoding.UTF8.GetBytes("not a zip") : Zip();
        var release = Release(zip);
        if (defect == "digest") release["assets"]![0]!["digest"] = "sha256:" + new string('0', 64);
        if (defect == "short") release["assets"]![0]!["size"] = zip.Length + 1;
        if (defect == "long") release["assets"]![0]!["size"] = zip.Length - 1;
        using var handler = Handler(zip, release, response =>
        {
            if (defect == "content-length") response.Content.Headers.ContentLength = zip.Length + 1;
        });
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(Enabled));
        AssertNoStage();
    }

    [Theory]
    [InlineData("traversal")]
    [InlineData("backslash")]
    [InlineData("absolute")]
    [InlineData("extra")]
    [InlineData("directory")]
    [InlineData("duplicate")]
    [InlineData("case")]
    [InlineData("symlink")]
    [InlineData("reparse")]
    [InlineData("entry-large")]
    [InlineData("total-large")]
    [InlineData("manifest-large")]
    [InlineData("manifest-hash")]
    [InlineData("manifest-extra")]
    [InlineData("manifest-missing")]
    [InlineData("manifest-product")]
    [InlineData("manifest-version")]
    [InlineData("manifest-platform")]
    [InlineData("manifest-runtime")]
    [InlineData("manifest-duplicate-key")]
    public async Task UnsafeArchivesAreRejectedAndOnlyTheirPartialStageIsCleaned(string defect)
    {
        Directory.CreateDirectory(_directory);
        var old = Path.Combine(_directory, "staged-existing", "payload");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "old.dll"), "untouched");
        var zip = Zip(defect);
        using var http = new HttpClient(Handler(zip));
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(Enabled));
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(old, "old.dll")));
        Assert.Single(Directory.GetDirectories(_directory, "staged-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationIsBoundedAndKeepsPersistentCooldown(bool alreadyCanceled)
    {
        using var cancel = new CancellationTokenSource();
        using var handler = new StubHandler(async (_, token) =>
        {
            cancel.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler);
        using var service = Create(http);
        if (alreadyCanceled) cancel.Cancel();
        Assert.Null(await service.CheckAsync(Enabled, cancel.Token).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(_clock.GetUtcNow(), service.State.LastAttemptUtc);
        using var restarted = Create(http);
        Assert.Null(await restarted.CheckAsync(Enabled));
        Assert.Equal(alreadyCanceled ? 0 : 1, handler.Count);
        AssertNoStage();
    }

    [Fact]
    public async Task CallerDefaultHeadersAreNotForwardedAndAreNotMutated()
    {
        using var handler = Handler(Zip());
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.Add("X-Fixture-Private", "do-not-forward");
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(Enabled));
        Assert.Equal(0, handler.Count);
        Assert.True(http.DefaultRequestHeaders.Contains("X-Fixture-Private"));
    }

    [Fact]
    public async Task OversizedMetadataIsRejectedWithoutDownload()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(new string(' ', 1024 * 1024 + 1)) }));
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(Enabled));
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"draft\":false,\"prerelease\":false}")]
    [InlineData("{\"draft\":false,\"draft\":true,\"prerelease\":false}")]
    public async Task MalformedMetadataReturnsNullRatherThanLeakingParserErrors(string json)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(json) }));
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(Enabled));
        Assert.NotNull(service.State.LastAttemptUtc);
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownLengthStreamsAreBoundedWhileReading(bool metadata)
    {
        var zip = Zip();
        var size = metadata ? 1024 * 1024 : zip.Length;
        using var stream = new NonSeekingStream(new byte[size + 100]);
        using var handler = new StubHandler((request, _) => Task.FromResult(!metadata && request.RequestUri!.Host == "api.github.com"
            ? Json(Release(zip)) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(Enabled));
        Assert.InRange(stream.BytesRead, 1, size + 1);
        AssertNoStage();
    }

    [Fact]
    public async Task ExactAnnouncedSizeAlsoWorksWithoutContentLength()
    {
        var zip = Zip();
        using var handler = new StubHandler((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com"
            ? Json(Release(zip)) : new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new NonSeekingStream(zip)) }));
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.NotNull(await service.CheckAsync(Enabled));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvenTransportIgnoringCancellationCannotKeepTheCheckWaiting(bool duringBody)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stream = new NeverCompletingStream(entered);
        using var handler = new StubHandler((_, _) =>
        {
            if (duringBody) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
            entered.TrySetResult();
            return pending.Task;
        });
        using var http = new HttpClient(handler);
        using var service = Create(http);
        using var cancel = new CancellationTokenSource();
        var checking = service.CheckAsync(Enabled, cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        Assert.Null(await checking.WaitAsync(TimeSpan.FromSeconds(5)));
        // Release the deliberately non-cooperative fixture after proving the check was bounded.
        pending.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        stream.Complete();
        using var restarted = Create(http);
        Assert.Null(await restarted.CheckAsync(Enabled));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task ClockRollbackAndDisableEnableCannotBypassCooldown()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var http = new HttpClient(handler);
        using var service = Create(http);
        Assert.Null(await service.CheckAsync(Enabled));
        var attempted = service.State.LastAttemptUtc;
        _clock.Advance(TimeSpan.FromHours(-1));
        Assert.Null(await service.CheckAsync(new()));
        Assert.Null(await service.CheckAsync(Enabled));
        using var restarted = Create(http);
        Assert.Null(await restarted.CheckAsync(Enabled));
        Assert.Equal(attempted, restarted.State.LastAttemptUtc);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task SubscriberExceptionsDoNotInterruptStampOrValidation()
    {
        using var http = new HttpClient(Handler(Zip()));
        using var service = Create(http);
        service.StatusChanged += _ => throw new InvalidOperationException("private observer detail");
        Assert.NotNull(await service.CheckAsync(Enabled));
        Assert.DoesNotContain("private", service.State.Status);
    }

    [Theory]
    [InlineData("10.0.0", "10.0.0", null)]
    [InlineData("10.0.7", "10.0.8", "LatestPatch")]
    [InlineData("10.0.0", "10.0.0", "Minor")]
    public async Task Net10RuntimeContractAcceptsStablePatchVersions(string core, string desktop, string? rollForward)
    {
        var config = JsonNode.Parse(RuntimeConfig(core, desktop))!.AsObject();
        if (rollForward is not null) config["runtimeOptions"]!["rollForward"] = rollForward;
        using var http = new HttpClient(Handler(Zip(runtimeConfig: config.ToJsonString())));
        using var service = Create(http);

        Assert.NotNull(await service.CheckAsync(Enabled));
    }

    [Theory]
    [InlineData(9, 9)]
    [InlineData(9, 10)]
    [InlineData(10, 9)]
    [InlineData(10, 11)]
    [InlineData(11, 10)]
    [InlineData(11, 11)]
    public async Task ManifestAndFrameworkMajorsMustAgreeWithNet10(int manifestMajor, int frameworkMajor)
    {
        var version = $"{frameworkMajor}.0.0";
        using var http = new HttpClient(Handler(Zip(runtimeConfig: RuntimeConfig(version, version), runtimeMajor: manifestMajor)));
        using var service = Create(http);

        Assert.Null(await service.CheckAsync(Enabled));
        AssertNoStage();
    }

    [Theory]
    [InlineData("tfm")]
    [InlineData("mixed-major")]
    [InlineData("missing-core")]
    [InlineData("missing-desktop")]
    [InlineData("duplicate-framework")]
    [InlineData("extra-framework")]
    [InlineData("framework-path")]
    [InlineData("singular-framework")]
    [InlineData("included-frameworks")]
    [InlineData("probing-path")]
    [InlineData("major-roll-forward")]
    [InlineData("latest-major-roll-forward")]
    [InlineData("framework-roll-forward")]
    [InlineData("legacy-roll-forward")]
    [InlineData("null-options")]
    [InlineData("null-frameworks")]
    [InlineData("wrong-config-properties")]
    [InlineData("malformed")]
    [InlineData("duplicate-property")]
    [InlineData("oversized")]
    public async Task IncompatibleRuntimeConfigurationsNeverStage(string defect)
    {
        var config = JsonNode.Parse(RuntimeConfig())!.AsObject();
        var options = config["runtimeOptions"]!.AsObject();
        var frameworks = options["frameworks"]!.AsArray();
        switch (defect)
        {
            case "tfm": options["tfm"] = "net9.0"; break;
            case "mixed-major": frameworks[1]!["version"] = "9.0.0"; break;
            case "missing-core": frameworks.RemoveAt(0); break;
            case "missing-desktop": frameworks.RemoveAt(1); break;
            case "duplicate-framework": frameworks[1] = frameworks[0]!.DeepClone(); break;
            case "extra-framework": frameworks.Add(new JsonObject { ["name"] = "Microsoft.AspNetCore.App", ["version"] = "10.0.0" }); break;
            case "framework-path": frameworks[1]!["name"] = "../Microsoft.WindowsDesktop.App"; break;
            case "singular-framework": options["framework"] = frameworks[0]!.DeepClone(); break;
            case "included-frameworks": options["includedFrameworks"] = frameworks.DeepClone(); break;
            case "probing-path": options["additionalProbingPaths"] = new JsonArray("../packages"); break;
            case "major-roll-forward": options["rollForward"] = "Major"; break;
            case "latest-major-roll-forward": options["rollForward"] = "LatestMajor"; break;
            case "framework-roll-forward": frameworks[1]!["rollForward"] = "LatestMajor"; break;
            case "legacy-roll-forward": options["rollForwardOnNoCandidateFx"] = 2; break;
            case "null-options": config["runtimeOptions"] = null; break;
            case "null-frameworks": options["frameworks"] = null; break;
            case "wrong-config-properties": options["configProperties"] = new JsonArray(); break;
        }
        var json = defect switch
        {
            "malformed" => "{broken",
            "duplicate-property" => config.ToJsonString().Replace("\"tfm\":", "\"tfm\":\"net9.0\",\"tfm\":"),
            "oversized" => config.ToJsonString() + new string(' ', 32 * 1024),
            _ => config.ToJsonString()
        };
        using var http = new HttpClient(Handler(Zip(runtimeConfig: json)));
        using var service = Create(http);

        Assert.Null(await service.CheckAsync(Enabled));
        AssertNoStage();
    }

    [Theory]
    [InlineData("10.1.0")]
    [InlineData("10.0")]
    [InlineData("10.0.0.1")]
    [InlineData("10.0.0-preview.1")]
    [InlineData("10.0.0/../9.0.0")]
    [InlineData("../10.0.0")]
    [InlineData("10.0.0\\..\\9.0.0")]
    [InlineData("010.0.0")]
    [InlineData("10.0.01")]
    [InlineData("10.0.2147483648")]
    public async Task RuntimeFrameworkVersionsMustBeCanonicalNet10Releases(string version)
    {
        using var http = new HttpClient(Handler(Zip(runtimeConfig: RuntimeConfig(version, "10.0.0"))));
        using var service = Create(http);

        Assert.Null(await service.CheckAsync(Enabled));
        AssertNoStage();
    }

    private static string RuntimeConfig(string core = "10.0.0", string desktop = "10.0.0") => JsonSerializer.Serialize(new
    {
        runtimeOptions = new
        {
            tfm = "net10.0",
            frameworks = new[]
            {
                new { name = "Microsoft.NETCore.App", version = core },
                new { name = "Microsoft.WindowsDesktop.App", version = desktop }
            }
        }
    });

    [Fact]
    public async Task UnsafeOptionalStorageDisablesUpdatesWithoutCrashingTheWidget()
    {
        using var http = new HttpClient(Handler(Zip()));
        using var service = new WidgetUpdateService(http, @"\\server\share\updates", _clock);
        Assert.Null(await service.CheckAsync(Enabled));
        Assert.Equal("更新记录不可用，已停止检查", service.State.Status);
    }

    private WidgetUpdateService Create(HttpClient http) => new(http, _directory, _clock);

    private void AssertNoStage() => Assert.Empty(Directory.Exists(_directory)
        ? Directory.GetDirectories(_directory, "staged-*") : []);

    private static StubHandler Handler(byte[] zip, JsonObject? release = null, Action<HttpResponseMessage>? changeDownload = null) =>
        new((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            if (request.RequestUri!.Host == "api.github.com")
            {
                Assert.Equal("https://api.github.com/repos/" + Repository + "/releases/latest", request.RequestUri.AbsoluteUri);
                return Task.FromResult(Json(release ?? Release(zip)));
            }
            Assert.Equal(DownloadUrl, request.RequestUri.AbsoluteUri);
            var response = Bytes(zip);
            changeDownload?.Invoke(response);
            return Task.FromResult(response);
        });

    private static JsonObject Release(byte[] zip) => JsonSerializer.SerializeToNode(new
    {
        tag_name = Tag, draft = false, prerelease = false,
        assets = new[] { new { name = AssetName, state = "uploaded", size = zip.Length,
            digest = "sha256:" + Hash(zip), browser_download_url = DownloadUrl } }
    })!.AsObject();

    private static byte[] Zip(string? defect = null, string? runtimeConfig = null, int runtimeMajor = 10)
    {
        var files = Names.ToDictionary(name => name, name => Encoding.UTF8.GetBytes("fixture " + name));
        files[Names[3]] = Encoding.UTF8.GetBytes(runtimeConfig ?? RuntimeConfig());
        if (defect == "entry-large") files[Names[0]] = new byte[20 * 1024 * 1024 + 1];
        if (defect == "total-large") foreach (var name in Names) files[name] = new byte[11 * 1024 * 1024];
        var manifest = JsonSerializer.SerializeToNode(new
        {
            product = "mali-yaya", version = ReleaseVersion, platform = "win-x64", runtimeMajor,
            files = files.ToDictionary(pair => pair.Key, pair => Hash(pair.Value))
        })!.AsObject();
        switch (defect)
        {
            case "manifest-hash": manifest["files"]![Names[0]] = new string('0', 64); break;
            case "manifest-extra": manifest["files"]!["evil.ps1"] = new string('0', 64); break;
            case "manifest-missing": manifest["files"]!.AsObject().Remove(Names[0]); break;
            case "manifest-product": manifest["product"] = "other"; break;
            case "manifest-version": manifest["version"] = "2099.12.31.8"; break;
            case "manifest-platform": manifest["platform"] = "linux-x64"; break;
            case "manifest-runtime": manifest["runtimeMajor"] = 9; break;
        }
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entryName = name;
                if (name == Names[0]) entryName = defect switch
                {
                    "traversal" => "../" + name, "backslash" => "dir\\" + name,
                    "absolute" => "C:/" + name, "case" => name.ToLowerInvariant(), _ => name
                };
                var entry = archive.CreateEntry(entryName);
                if (name == Names[0] && defect == "symlink") entry.ExternalAttributes = unchecked((int)0xA1FF0000);
                if (name == Names[0] && defect == "reparse") entry.ExternalAttributes = (int)FileAttributes.ReparsePoint;
                using var output = entry.Open();
                output.Write(content);
            }
            if (defect == "extra") archive.CreateEntry("debug.pdb");
            if (defect == "directory") archive.CreateEntry("cache/");
            if (defect == "duplicate") archive.CreateEntry(Names[0]);
            using var manifestStream = archive.CreateEntry("release-manifest.json").Open();
            var json = manifest.ToJsonString();
            if (defect == "manifest-duplicate-key") json = json.Insert(1, "\"product\":\"other\",");
            if (defect == "manifest-large") json += new string(' ', 32 * 1024);
            manifestStream.Write(Encoding.UTF8.GetBytes(json));
        }
        return bytes.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static HttpResponseMessage Json(JsonObject value) => new(HttpStatusCode.OK)
    { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Redirect(string url) => new(HttpStatusCode.Found)
    { Headers = { Location = new Uri(url, UriKind.RelativeOrAbsolute) } };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return send(request, cancellationToken);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class NonSeekingStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = _inner.Read(buffer.Span);
            BytesRead += read;
            return ValueTask.FromResult(read);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }

    private sealed class NeverCompletingStream(TaskCompletionSource entered) : Stream
    {
        private readonly TaskCompletionSource<int> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete() => _read.TrySetResult(0);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            return new(_read.Task);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        var path = Path.GetFullPath(_directory);
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodexUsageWidget.UpdateTests"));
        if (!string.Equals(Path.GetDirectoryName(path), parent, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(path), "N", out _))
            throw new InvalidOperationException("Refusing cleanup outside the owned test directory.");
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
