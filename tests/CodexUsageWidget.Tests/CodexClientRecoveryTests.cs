using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class CodexClientRecoveryTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedTimeoutsCloseOwnedConnectionAndNextRefreshRebuilds(bool blockedWrite)
    {
        var clock = new ManualClock();
        var first = new FakeConnection();
        first.OnWrite = (method, _) =>
        {
            if (method != "account/rateLimits/read") return false;
            clock.Expire();
            return true;
        };
        if (blockedWrite) first.BlockedMethod = "account/rateLimits/read";
        var second = new FakeConnection();
        var starts = 0;
        await using var client = Client(Resolver(), _ => ++starts == 1 ? first : second, clock);
        var snapshots = 0;
        client.SnapshotUpdated += _ => snapshots++;

        await client.RefreshAsync().WaitAsync(Guard);

        Assert.Equal(2, first.Methods.Count(m => m == "account/rateLimits/read"));
        Assert.True(first.Killed);
        Assert.True(first.Disposed);
        Assert.Equal(0, snapshots);
        await client.RefreshAsync().WaitAsync(Guard);
        Assert.Equal(2, starts);
        Assert.Equal(1, snapshots);
        Assert.False(second.Killed);
    }

    [Fact]
    public async Task OneTimeoutFollowedBySuccessKeepsConnection()
    {
        var clock = new ManualClock();
        var connection = new FakeConnection();
        var reads = 0;
        connection.OnWrite = (method, _) =>
        {
            if (method != "account/rateLimits/read" || ++reads != 1) return false;
            clock.Expire();
            return true;
        };
        var starts = 0;
        await using var client = Client(Resolver(), _ => { starts++; return connection; }, clock);

        await client.RefreshAsync().WaitAsync(Guard);
        await client.RefreshAsync().WaitAsync(Guard);

        Assert.Equal(1, starts);
        Assert.Equal(3, reads);
        Assert.False(connection.Killed);
    }

    [Theory]
    [InlineData("eof")]
    [InlineData("invalid-json")]
    [InlineData("missing-rate-limits")]
    public async Task DamagedConnectionIsClosedAndRebuilt(string damage)
    {
        var first = new FakeConnection();
        first.OnWrite = (method, id) =>
        {
            if (method != "account/rateLimits/read") return false;
            if (damage == "eof") first.CloseOutput();
            else if (damage == "invalid-json") first.Output("not json");
            else first.Result(id!.Value, "{}");
            return true;
        };
        var starts = 0;
        await using var client = Client(Resolver(), _ => ++starts == 1 ? first : new FakeConnection());
        var snapshots = 0;
        client.SnapshotUpdated += _ => snapshots++;

        await client.RefreshAsync().WaitAsync(Guard);
        Assert.True(first.Killed);
        Assert.True(first.Disposed);
        await client.RefreshAsync().WaitAsync(Guard);
        Assert.Equal(2, starts);
        Assert.Equal(1, snapshots);
    }

    [Theory]
    [InlineData(-32601)]
    [InlineData(-32602)]
    public async Task AutomaticModeFallsBackOnlyAfterInitializeIncompatibility(int code)
    {
        var first = Incompatible(code);
        var paths = new List<string>();
        await using var client = Client(Resolver(2), path =>
        {
            paths.Add(path);
            if (paths.Count == 1) return first;
            Assert.True(first.Killed);
            Assert.True(first.Disposed);
            return new FakeConnection();
        });

        await client.RefreshAsync().WaitAsync(Guard);

        Assert.Equal(new[] { "candidate-0", "candidate-1" }, paths);
        Assert.Equal("candidate-1", client.Installation!.ExecutablePath);
    }

    [Theory]
    [InlineData("Error: unexpected argument '--listen' found")]
    [InlineData("Error: unrecognized subcommand 'app-server'")]
    public async Task ExplicitAppServerStartupDiagnosticAllowsAutomaticFallback(string diagnostic)
    {
        var first = new FakeConnection();
        first.StartupFailure(diagnostic);
        var starts = 0;
        await using var client = Client(Resolver(2), _ => ++starts == 1 ? first : new FakeConnection());

        await client.StartAsync().WaitAsync(Guard);

        Assert.Equal(2, starts);
        Assert.True(first.Disposed);
    }

    [Theory]
    [InlineData("missing-account")]
    [InlineData("api-key")]
    [InlineData("login")]
    [InlineData("network")]
    [InlineData("account-protocol")]
    [InlineData("permission")]
    [InlineData("win32-permission")]
    [InlineData("initialize-timeout")]
    [InlineData("state-directory")]
    [InlineData("unrelated-argument")]
    [InlineData("unexplained-eof")]
    public async Task LoginPermissionNetworkAndUnclassifiedFailuresNeverSwitchEntry(string failure)
    {
        var clock = new ManualClock();
        var first = new FakeConnection();
        if (failure == "state-directory") first.StartupFailure("failed to initialize state runtime");
        if (failure == "unrelated-argument") first.StartupFailure("unexpected argument '--some-config-option'");
        if (failure == "unexplained-eof") first.CloseOutput();
        first.OnWrite = (method, id) =>
        {
            if (method == "initialize" && failure is "login" or "network")
            {
                first.Error(id!.Value, -32000, failure == "login" ? "not authenticated" : "network unavailable");
                return true;
            }
            if (method == "initialize" && failure == "initialize-timeout")
            {
                clock.Expire();
                return true;
            }
            if (method != "account/read") return false;
            if (failure == "account-protocol") first.Error(id!.Value, -32601, "unknown method");
            else if (failure == "missing-account") first.Result(id!.Value, "{\"account\":null}");
            else if (failure == "api-key") first.Result(id!.Value, "{\"account\":{\"type\":\"apiKey\"}}");
            else return false;
            return true;
        };
        var starts = 0;
        await using var client = Client(Resolver(2), _ =>
        {
            starts++;
            if (failure == "permission") throw new UnauthorizedAccessException("fixture");
            if (failure == "win32-permission") throw new System.ComponentModel.Win32Exception(5);
            return first;
        }, clock);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => client.StartAsync().WaitAsync(Guard));

        Assert.IsNotType<TimeoutException>(error);
        if (failure == "initialize-timeout") Assert.IsAssignableFrom<OperationCanceledException>(error);
        else if (failure == "permission") Assert.IsType<UnauthorizedAccessException>(error);
        else if (failure == "win32-permission") Assert.IsType<System.ComponentModel.Win32Exception>(error);
        else if (failure == "unexplained-eof") Assert.IsType<IOException>(error);
        else if (failure is "login" or "network" or "account-protocol") Assert.IsType<CodexProtocolException>(error);
        else Assert.IsType<CodexDiscoveryException>(error);
        Assert.Equal(1, starts);
        if (failure is not ("permission" or "win32-permission")) Assert.True(first.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualAndDedicatedEnvironmentOverridesNeverFallBack(bool environmentOverride)
    {
        var starts = 0;
        var resolver = Resolver(2, environmentOverride: environmentOverride ? "candidate-0" : null);
        await using var client = Client(resolver, _ => { starts++; return Incompatible(-32601); },
            executable: environmentOverride ? null : "candidate-0");

        await Assert.ThrowsAsync<CodexProtocolException>(() => client.StartAsync().WaitAsync(Guard));

        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task AutomaticHandshakeFallbackIsLimitedToEightCandidates()
    {
        var connections = new List<FakeConnection>();
        await using var client = Client(Resolver(12), _ =>
        {
            var connection = Incompatible(-32601);
            connections.Add(connection);
            return connection;
        });

        await Assert.ThrowsAsync<CodexProtocolException>(() => client.StartAsync().WaitAsync(Guard));

        Assert.Equal(8, connections.Count);
        Assert.All(connections, c => { Assert.True(c.Killed); Assert.True(c.Disposed); });
    }

    [Fact]
    public async Task DiscoveryDeadlineIsPausedWhileCallerPerformsHandshake()
    {
        var discoveryClock = new ManualClock();
        var first = new FakeConnection();
        first.OnWrite = (method, id) =>
        {
            if (method != "initialize") return false;
            discoveryClock.Expire();
            first.Error(id!.Value, -32601, "unsupported initialization protocol");
            return true;
        };
        var starts = 0;
        await using var client = Client(Resolver(2, clock: discoveryClock), _ => ++starts == 1 ? first : new FakeConnection());

        await client.StartAsync().WaitAsync(Guard);

        Assert.Equal(2, starts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchAndDisposeCancelBlockedScanWaitingWithoutWaitingForKernelIo(bool dispose)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scans = 0;
        var resolver = Resolver(scan: (configured, token) =>
        {
            if (Interlocked.Increment(ref scans) == 1)
            {
                entered.TrySetResult(token);
                try { release.Wait(); token.ThrowIfCancellationRequested(); }
                finally { finished.TrySetResult(); }
            }
            return [new(configured ?? "candidate-0", "fixture")];
        });
        var starts = 0;
        await using var client = Client(resolver, _ => { starts++; return new FakeConnection(); });
        var starting = client.StartAsync();
        try
        {
            var token = await entered.Task.WaitAsync(Guard);
            if (dispose) await client.DisposeAsync().AsTask().WaitAsync(Guard);
            else await client.ChangeExecutableAsync("replacement").WaitAsync(Guard);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting.WaitAsync(Guard));
            Assert.True(token.IsCancellationRequested);
            Assert.Equal(0, starts);
            if (!dispose)
            {
                await client.RefreshAsync().WaitAsync(Guard);
                Assert.Equal("replacement", client.Installation!.ExecutablePath);
                Assert.Equal(1, starts);
            }
        }
        finally
        {
            release.Set();
            await finished.Task.WaitAsync(Guard);
        }
    }

    [Theory]
    [InlineData(false, "initialize")]
    [InlineData(true, "initialize")]
    [InlineData(false, "account/rateLimits/read")]
    [InlineData(true, "account/rateLimits/read")]
    public async Task SwitchAndDisposeCancelInFlightHandshakeOrRefresh(bool dispose, string blockedMethod)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new FakeConnection();
        first.OnWrite = (method, _) =>
        {
            if (method != blockedMethod) return false;
            entered.TrySetResult();
            return true;
        };
        var starts = 0;
        await using var client = Client(Resolver(), _ => ++starts == 1 ? first : new FakeConnection(), new ManualClock());
        var refresh = client.RefreshAsync();
        await entered.Task.WaitAsync(Guard);

        if (dispose) await client.DisposeAsync().AsTask().WaitAsync(Guard);
        else await client.ChangeExecutableAsync("replacement").WaitAsync(Guard);
        await refresh.WaitAsync(Guard);

        Assert.True(first.Killed);
        Assert.True(first.Disposed);
        if (!dispose)
        {
            await client.RefreshAsync().WaitAsync(Guard);
            Assert.Equal("replacement", client.Installation!.ExecutablePath);
            Assert.Equal(2, starts);
        }
    }

    [Fact]
    public async Task ScanBudgetCoversWaitingEvenWhenScannerIgnoresCancellation()
    {
        var clock = new ManualClock();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = Resolver(scan: (_, token) =>
        {
            entered.TrySetResult(token);
            try { release.Wait(); return []; }
            finally { finished.TrySetResult(); }
        }, clock: clock);
        var resolving = resolver.ResolveAsync();
        try
        {
            var token = await entered.Task.WaitAsync(Guard);
            clock.Expire();
            await Assert.ThrowsAsync<CodexDiscoveryException>(() => resolving.WaitAsync(Guard));
            Assert.True(token.IsCancellationRequested);
            Assert.False(finished.Task.IsCompleted);
        }
        finally
        {
            release.Set();
            await finished.Task.WaitAsync(Guard);
        }
    }

    [Fact]
    public async Task ProbeWaitingHonorsCancellationEvenWhenProbeIgnoresToken()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var resolver = Resolver(probe: (_, _) => { entered.TrySetResult(); return blocked.Task; });
        var resolving = resolver.ResolveAsync(cancellationToken: cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(Guard);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolving.WaitAsync(Guard));
        }
        finally { blocked.TrySetResult(null); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchAndDisposeCancelInProgressVersionDetection(bool dispose)
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = Resolver(probe: (_, token) => { entered.TrySetResult(token); return blocked.Task; });
        var starts = 0;
        await using var client = Client(resolver, _ => { starts++; return new FakeConnection(); });
        var starting = client.StartAsync();
        try
        {
            var token = await entered.Task.WaitAsync(Guard);
            if (dispose) await client.DisposeAsync().AsTask().WaitAsync(Guard);
            else await client.ChangeExecutableAsync("replacement").WaitAsync(Guard);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting.WaitAsync(Guard));
            Assert.True(token.IsCancellationRequested);
            Assert.Equal(0, starts);
        }
        finally { blocked.TrySetResult(null); }
    }

    [Fact]
    public async Task VersionProbePermissionFailureDoesNotTryAnotherEntry()
    {
        var probes = 0;
        var resolver = Resolver(2, probe: (_, _) => { probes++; throw new UnauthorizedAccessException("fixture"); });

        await Assert.ThrowsAsync<CodexDiscoveryException>(() => resolver.ResolveAsync().WaitAsync(Guard));

        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task HandshakeOrderAndReadOnlyDiagnosticParametersArePreserved()
    {
        var connection = new FakeConnection();
        await using var client = Client(Resolver(), _ => connection);

        await client.RefreshAsync().WaitAsync(Guard);

        Assert.Equal(new[] { "initialize", "initialized", "account/read", "account/rateLimits/read" }, connection.Methods);
        var initialize = connection.Messages[0].GetProperty("params");
        Assert.Equal("codex-usage-widget", initialize.GetProperty("clientInfo").GetProperty("name").GetString());
        Assert.Equal(WidgetRuntimeStatus.BuildVersion, initialize.GetProperty("clientInfo").GetProperty("version").GetString());
        Assert.True(initialize.GetProperty("capabilities").GetProperty("experimentalApi").GetBoolean());
        Assert.False(connection.Messages[2].GetProperty("params").GetProperty("refreshToken").GetBoolean());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"usedPercent\":-1}")]
    [InlineData("{\"usedPercent\":101}")]
    [InlineData("{\"usedPercent\":42,\"windowDurationMins\":-9223372036854775808}")]
    public async Task InvalidWindowNeverPublishesFalseFullAllowance(string window)
    {
        var connection = new FakeConnection();
        connection.OnWrite = (method, id) =>
        {
            if (method != "account/rateLimits/read") return false;
            connection.Result(id!.Value, "{\"rateLimits\":{\"primary\":" + window + "}}");
            return true;
        };
        await using var client = Client(Resolver(), _ => connection);
        var snapshots = new List<RateLimitSnapshot>();
        client.SnapshotUpdated += snapshots.Add;
        await client.RefreshAsync().WaitAsync(Guard);
        Assert.Empty(snapshots);
        Assert.True(connection.Killed);
    }

    [Fact]
    public async Task MissingUsageNotificationCannotPublishAFalseRecovery()
    {
        var connection = new FakeConnection();
        await using var client = Client(Resolver(), _ => connection);
        var snapshots = new List<RateLimitSnapshot>();
        client.SnapshotUpdated += snapshots.Add;
        await client.RefreshAsync().WaitAsync(Guard);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionStatusChanged += _ => disconnected.TrySetResult();
        connection.Output("{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{\"primary\":{\"windowDurationMins\":10080}}}}");
        await disconnected.Task.WaitAsync(Guard);
        Assert.Single(snapshots);
        Assert.Equal(42, snapshots[0].Primary!.UsedPercent);
    }

    [Fact]
    public async Task ResetOpportunitiesAreNotCarriedIntoANewConnection()
    {
        var first = new FakeConnection();
        first.OnWrite = (method, id) =>
        {
            if (method != "account/rateLimits/read") return false;
            first.Result(id!.Value, "{\"rateLimits\":{\"primary\":{\"usedPercent\":42}},\"rateLimitResetCredits\":{\"availableCount\":2,\"credits\":[{\"status\":\"available\",\"expiresAt\":1794000000}]}}");
            return true;
        };
        var starts = 0;
        await using var client = Client(Resolver(), _ => ++starts == 1 ? first : new FakeConnection());
        var snapshots = new List<RateLimitSnapshot>();
        client.SnapshotUpdated += snapshots.Add;
        await client.RefreshAsync().WaitAsync(Guard);
        Assert.Equal(2, snapshots[0].ResetOpportunityCount);
        first.Kill();
        await client.RefreshAsync().WaitAsync(Guard);
        Assert.Equal(2, starts);
        Assert.Null(snapshots[1].ResetOpportunityCount);
        Assert.Null(snapshots[1].ResetOpportunityExpiresAt);
        Assert.False(snapshots[1].ResetOpportunityReported);
        Assert.NotNull(snapshots[0].ConnectionSessionId);
        Assert.NotNull(snapshots[1].ConnectionSessionId);
        Assert.NotEqual(snapshots[0].ConnectionSessionId, snapshots[1].ConnectionSessionId);
    }

    [Fact]
    public async Task CountOnlyReportCannotReuseExpiryFromDifferentCreditData()
    {
        var connection = new FakeConnection();
        var reads = 0;
        connection.OnWrite = (method, id) =>
        {
            if (method != "account/rateLimits/read") return false;
            connection.Result(id!.Value, ++reads == 1
                ? "{\"rateLimits\":{\"primary\":{\"usedPercent\":42}},\"rateLimitResetCredits\":{\"availableCount\":2,\"credits\":[{\"status\":\"available\",\"expiresAt\":1794000000}]}}"
                : "{\"rateLimits\":{\"primary\":{\"usedPercent\":42}},\"availableResets\":1}");
            return true;
        };
        await using var client = Client(Resolver(), _ => connection);
        var snapshots = new List<RateLimitSnapshot>();
        client.SnapshotUpdated += snapshots.Add;
        await client.RefreshAsync().WaitAsync(Guard);
        await client.RefreshAsync().WaitAsync(Guard);
        Assert.Equal(1, snapshots[1].ResetOpportunityCount);
        Assert.Null(snapshots[1].ResetOpportunityExpiresAt);
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public async Task InvalidOptionalResetTimeKeepsValidUsageButBecomesUnknown(long reset)
    {
        var connection = new FakeConnection();
        connection.OnWrite = (method, id) =>
        {
            if (method != "account/rateLimits/read") return false;
            connection.Result(id!.Value, "{\"rateLimits\":{\"primary\":{\"usedPercent\":42,\"windowDurationMins\":10080,\"resetsAt\":" + reset + "}}}");
            return true;
        };
        await using var client = Client(Resolver(), _ => connection);
        var snapshots = new List<RateLimitSnapshot>();
        client.SnapshotUpdated += snapshots.Add;
        await client.RefreshAsync().WaitAsync(Guard);
        Assert.Single(snapshots);
        Assert.Equal(42, snapshots[0].Primary!.UsedPercent);
        Assert.Null(snapshots[0].Primary!.ResetsAt);
    }

    private static CodexRateLimitClient Client(CodexExecutableResolver resolver,
        Func<string, ICodexConnection> connect, ManualClock? clock = null, string? executable = null) =>
        new(executable, resolver, connect, TimeSpan.FromMinutes(5), TimeSpan.Zero, clock);

    private static CodexExecutableResolver Resolver(int count = 1, string? environmentOverride = null,
        Func<string?, CancellationToken, IReadOnlyList<CodexCandidate>>? scan = null,
        ManualClock? clock = null, Func<string, CancellationToken, Task<string?>>? probe = null) =>
        new(new("", "", "", "", "", Architecture.X64, environmentOverride),
            probe ?? ((_, _) => Task.FromResult<string?>("codex-cli 0.162.0")),
            scan ?? ((configured, token) =>
            {
                token.ThrowIfCancellationRequested();
                return configured is not null ? [new(configured, "fixture")] :
                    Enumerable.Range(0, count).Select(i => new CodexCandidate($"candidate-{i}", "fixture")).ToArray();
            }), TimeSpan.FromMinutes(1), clock);

    private static FakeConnection Incompatible(int code)
    {
        var connection = new FakeConnection();
        connection.OnWrite = (method, id) =>
        {
            if (method != "initialize") return false;
            connection.Error(id!.Value, code, "unsupported initialization protocol");
            return true;
        };
        return connection;
    }

    private sealed class FakeConnection : ICodexConnection
    {
        private readonly Channel<string> _output = Channel.CreateUnbounded<string>();
        private readonly Channel<string> _errors = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource _blockedWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<string, long?, bool>? OnWrite { get; set; }
        public string? BlockedMethod { get; set; }
        public bool HasExited { get; private set; }
        public bool Killed { get; private set; }
        public bool Disposed { get; private set; }
        public List<string> Methods { get; } = [];
        public List<JsonElement> Messages { get; } = [];

        public ValueTask<string?> ReadOutputAsync(CancellationToken token) => ReadAsync(_output, token);
        public ValueTask<string?> ReadErrorAsync(CancellationToken token) => ReadAsync(_errors, token);

        private static async ValueTask<string?> ReadAsync(Channel<string> channel, CancellationToken token)
        {
            try { return await channel.Reader.ReadAsync(token); }
            catch (ChannelClosedException) { return null; }
        }

        public Task WriteLineAsync(string line, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString()!;
            var id = root.TryGetProperty("id", out var value) ? value.GetInt64() : (long?)null;
            Methods.Add(method);
            Messages.Add(root.Clone());
            var handled = OnWrite?.Invoke(method, id) == true;
            if (method == BlockedMethod) return _blockedWrite.Task;
            if (!handled && id.HasValue)
                Result(id.Value, method == "account/read" ? "{\"account\":{\"type\":\"chatgpt\"}}" :
                    method == "account/rateLimits/read" ? "{\"rateLimits\":{\"primary\":{\"usedPercent\":42}}}" : "{}");
            return Task.CompletedTask;
        }

        public void Result(long id, string json) => Output($"{{\"id\":{id},\"result\":{json}}}");
        public void Error(long id, int code, string message) =>
            Output(JsonSerializer.Serialize(new { id, error = new { code, message } }));
        public void Output(string line) => _output.Writer.TryWrite(line);
        public void CloseOutput() { _output.Writer.TryComplete(); _errors.Writer.TryComplete(); }
        public void StartupFailure(string diagnostic) { _errors.Writer.TryWrite(diagnostic); CloseOutput(); }
        public void Kill() { Killed = true; HasExited = true; CloseOutput(); }
        public void Dispose() { Disposed = true; _blockedWrite.TrySetResult(); CloseOutput(); }
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);
            lock (_gate) _timers.Add(timer);
            return timer;
        }

        public void Expire()
        {
            ManualTimer[] timers;
            lock (_gate) timers = _timers.ToArray();
            foreach (var timer in timers) timer.Fire();
        }

        private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            private int _disposed;
            private bool _armed = dueTime != Timeout.InfiniteTimeSpan;
            public bool Change(TimeSpan due, TimeSpan period) { _armed = due != Timeout.InfiniteTimeSpan; return _disposed == 0; }
            public void Fire() { if (_armed && Interlocked.Exchange(ref _disposed, 1) == 0) callback(state); }
            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
