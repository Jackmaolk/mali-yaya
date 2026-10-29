using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

[assembly: InternalsVisibleTo("CodexUsageWidget.Tests")]

namespace CodexUsageWidget;

public sealed class CodexRateLimitClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _operationGate = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly Func<CodexExecutableResolver> _resolverFactory;
    private readonly Func<string, ICodexConnection> _connect;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _retryDelay;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private Connection? _connection;
    private CancellationTokenSource? _cts;
    private long _operationGeneration;
    private int _pendingChanges;
    private long _nextRequestId;
    private int _consecutiveRefreshFailures;
    private volatile bool _initialized;
    private int _disposed;
    private string? _executableOverride;

    public CodexRateLimitClient(string? executableOverride = null)
    {
        _executableOverride = executableOverride;
        _resolverFactory = () => new CodexExecutableResolver();
        _connect = executable => new CodexProcessConnection(StartProcess(executable));
        _requestTimeout = TimeSpan.FromSeconds(12);
        _retryDelay = TimeSpan.FromMilliseconds(1500);
    }

    internal CodexRateLimitClient(string? executableOverride, CodexExecutableResolver resolver,
        Func<string, ICodexConnection> connect, TimeSpan? requestTimeout = null, TimeSpan? retryDelay = null,
        TimeProvider? timeProvider = null)
        : this(executableOverride)
    {
        _resolverFactory = () => resolver;
        _connect = connect;
        _requestTimeout = requestTimeout ?? _requestTimeout;
        _retryDelay = retryDelay ?? _retryDelay;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private sealed class Connection(ICodexConnection transport, CancellationToken token)
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public ICodexConnection Transport { get; } = transport;
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(token);
        public ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> Pending { get; } = new();
        public Task ReadTask { get; set; } = Task.CompletedTask;
        public Task ErrorTask { get; set; } = Task.CompletedTask;
        public string? StartupIssue { get; set; }
        public bool StartupIncompatible { get; set; }
        public volatile bool Broken;
        public int? ResetOpportunityCount;
        public long? ResetOpportunityExpiresAt;
        public bool ResetOpportunityReported;
    }

    public CodexInstallation? Installation { get; private set; }

    public event Action<RateLimitSnapshot>? SnapshotUpdated;
    public event Action<string>? ConnectionStatusChanged;
    public event Action<CodexInstallation>? InstallationDetected;

    public async Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (IsConnected) return;
        long generation;
        lock (_operationGate) generation = _operationGeneration;
        await _startLock.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (IsConnected) return;
            await StopAsync();
            CancellationToken token;
            lock (_operationGate)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                // A queued start must not recreate a CTS after a switch/dispose canceled it.
                if (_pendingChanges != 0 || generation != _operationGeneration)
                    throw new OperationCanceledException();
                _cts = new CancellationTokenSource();
                token = _cts.Token;
            }
            Installation = null;
            ConnectionStatusChanged?.Invoke("正在自动识别 Codex CLI…");
            var resolver = _resolverFactory();
            Exception? incompatible = null;
            await foreach (var installation in resolver.ResolveCandidatesAsync(_executableOverride, token))
            {
                token.ThrowIfCancellationRequested();
                Installation = installation;
                InstallationDetected?.Invoke(installation);
                ConnectionStatusChanged?.Invoke($"已识别 {installation.Source}，正在连接…");
                var connection = new Connection(_connect(installation.ExecutablePath), token);
                _connection = connection;
                connection.ErrorTask = DrainErrorsAsync(connection);
                connection.ReadTask = ReadLoopAsync(connection);
                var initializing = true;
                try
                {
                    await SendRequestAsync(connection, "initialize", new
                    {
                        clientInfo = new { name = "codex-usage-widget", version = WidgetRuntimeStatus.BuildVersion },
                        capabilities = new { experimentalApi = true }
                    }, token);
                    initializing = false;
                    await WriteLineAsync(connection, "{\"method\":\"initialized\"}", token);
                    var account = await SendRequestAsync(connection, "account/read", new { refreshToken = false }, token);
                    ValidateAccount(account);
                    token.ThrowIfCancellationRequested();
                    if (connection.Broken) throw new IOException("Codex app-server connection closed.");
                    _initialized = true;
                    ConnectionStatusChanged?.Invoke("Codex 已连接");
                    return;
                }
                catch (Exception ex) when (!token.IsCancellationRequested && !resolver.IsExplicitSelection(_executableOverride) &&
                    initializing && (ex is CodexAppServerIncompatibleException ||
                        ex is CodexProtocolException { Code: -32601 or -32602 }))
                {
                    incompatible = ex;
                    // Keep the discovery operation alive while discarding only this child.
                    await StopConnectionAsync();
                }
            }
            throw incompatible ?? resolver.MissingInstallation(_executableOverride);
        }
        catch
        {
            await StopAsync();
            throw;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task RefreshAsync()
    {
        if (_disposed != 0) return;
        if (!await _refreshLock.WaitAsync(0)) return;
        try
        {
            await StartAsync();
            if (_cts is null || _connection is not { } connection) return;
            var token = _cts.Token;

            Exception? lastError = null;
            var timeouts = 0;
            var broken = false;
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    var result = await SendRequestAsync(connection, "account/rateLimits/read", null, token);
                    var response = result.Deserialize<RateLimitResponse>(_jsonOptions);
                    if (response?.RateLimits is null) throw new InvalidDataException("Codex 未返回用量数据。");
                    ValidateSnapshot(response.RateLimits);
                    ApplyResetCredits(connection, response.RateLimits, response.RateLimitResetCredits, result);
                    response.RateLimits.ConnectionSessionId = connection.SessionId;
                    _consecutiveRefreshFailures = 0;
                    SnapshotUpdated?.Invoke(response.RateLimits);
                    return;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    timeouts++;
                    lastError = new TimeoutException("Codex 用量请求超时。");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    lastError = ex;
                    broken = connection.Broken || ex is IOException or InvalidDataException or JsonException;
                }

                if (broken) break;
                if (attempt == 1)
                {
                    ConnectionStatusChanged?.Invoke("刷新波动，正在自动重试…");
                    await Task.Delay(_retryDelay, token);
                }
            }

            if (broken || timeouts == 2)
            {
                await _startLock.WaitAsync();
                try
                {
                    if (ReferenceEquals(connection, _connection)) await StopAsync();
                }
                finally { _startLock.Release(); }
            }
            _consecutiveRefreshFailures++;
            var suffix = _consecutiveRefreshFailures >= 3 ? "，请稍后手动刷新" : "，下轮自动重试";
            ConnectionStatusChanged?.Invoke(lastError is null ? $"暂时无法刷新，保留上次数据{suffix}" :
                DescribeConnectionError(lastError));
            Debug.WriteLine(lastError);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _consecutiveRefreshFailures++;
            ConnectionStatusChanged?.Invoke(DescribeConnectionError(ex));
            Debug.WriteLine(ex);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsConnected
    {
        get
        {
            var connection = _connection;
            if (!_initialized || connection is null || connection.Broken || _cts is not { IsCancellationRequested: false })
                return false;
            try { return !connection.Transport.HasExited && !connection.ReadTask.IsCompleted; }
            catch (InvalidOperationException) { return false; }
        }
    }

    private static Process StartProcess(string executable)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add("app-server");
        info.ArgumentList.Add("--listen");
        info.ArgumentList.Add("stdio://");
        return Process.Start(info) ?? throw new InvalidOperationException("无法启动 Codex app-server。 ");
    }

    private async Task WriteLineAsync(Connection connection, string payload, CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(_requestTimeout, _timeProvider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, connection.Cancellation.Token, deadline.Token);
        await connection.Transport.WriteLineAsync(payload, timeout.Token).WaitAsync(timeout.Token);
    }

    private async Task<JsonElement> SendRequestAsync(Connection connection, string method, object? parameters, CancellationToken token)
    {
        if (connection.Broken || connection.Transport.HasExited)
        {
            await WaitForDrainAsync(connection.ErrorTask, TimeSpan.FromMilliseconds(250));
            throw ConnectionClosed(connection);
        }
        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Pending[id] = completion;
        var payload = JsonSerializer.Serialize(new { id, method, @params = parameters });
        using var deadline = new CancellationTokenSource(_requestTimeout, _timeProvider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, connection.Cancellation.Token, deadline.Token);
        await using var registration = timeout.Token.Register(() => completion.TrySetCanceled(timeout.Token));
        try
        {
            await connection.Transport.WriteLineAsync(payload, timeout.Token).WaitAsync(timeout.Token);
            return await completion.Task;
        }
        finally
        {
            connection.Pending.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync(Connection connection)
    {
        var token = connection.Cancellation.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var line = await connection.Transport.ReadOutputAsync(token);
                if (line is null) break;
                token.ThrowIfCancellationRequested();
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id))
                {
                    if (connection.Pending.TryGetValue(id, out var completion))
                    {
                        if (root.TryGetProperty("error", out var error))
                        {
                            var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode)
                                ? parsedCode : 0;
                            var message = error.TryGetProperty("message", out var messageElement)
                                ? messageElement.GetString() ?? "Codex 请求失败" : "Codex 请求失败";
                            completion.TrySetException(new CodexProtocolException(code, message));
                        }
                        else if (root.TryGetProperty("result", out var result))
                            completion.TrySetResult(result.Clone());
                    }
                    continue;
                }

                if (root.TryGetProperty("method", out var method) && method.GetString() == "account/rateLimits/updated" &&
                    root.TryGetProperty("params", out var notificationParams) &&
                    notificationParams.TryGetProperty("rateLimits", out var rateLimits))
                {
                    var snapshot = rateLimits.Deserialize<RateLimitSnapshot>(_jsonOptions);
                    if (snapshot is not null)
                    {
                        ValidateSnapshot(snapshot);
                        RateLimitResetCreditsSnapshot? resetCredits = null;
                        if (notificationParams.TryGetProperty("rateLimitResetCredits", out var resetCreditsElement))
                            resetCredits = resetCreditsElement.Deserialize<RateLimitResetCreditsSnapshot>(_jsonOptions);
                        ApplyResetCredits(connection, snapshot, resetCredits, notificationParams);
                        snapshot.ConnectionSessionId = connection.SessionId;
                        SnapshotUpdated?.Invoke(snapshot);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ConnectionStatusChanged?.Invoke(DescribeConnectionError(ex));
        }
        finally
        {
            connection.Broken = true;
            if (ReferenceEquals(connection, _connection)) _initialized = false;
            if (!token.IsCancellationRequested)
            {
                await WaitForDrainAsync(connection.ErrorTask, TimeSpan.FromMilliseconds(250));
                Kill(connection);
            }
            foreach (var completion in connection.Pending.Values)
                completion.TrySetException(ConnectionClosed(connection));
        }
    }

    private static Exception ConnectionClosed(Connection connection) =>
        connection.StartupIncompatible ? new CodexAppServerIncompatibleException() :
        connection.StartupIssue is { } issue ? new CodexDiscoveryException(issue) :
        new IOException("Codex app-server connection closed.");

    private static void ValidateSnapshot(RateLimitSnapshot snapshot)
    {
        foreach (var window in new[] { snapshot.Primary, snapshot.Secondary })
        {
            if (window is not null && (window.UsedPercent is < 0 or > 100 ||
                window.WindowDurationMins is <= 0 or > 525600))
                throw new InvalidDataException("Codex 返回的额度窗口数据无效。");
            if (window?.ResetsAt is < -62135596800 or > 253402300799)
                window.ResetsAt = null;
        }
    }

    private static void ApplyResetCredits(
        Connection connection,
        RateLimitSnapshot snapshot,
        RateLimitResetCreditsSnapshot? resetCredits,
        JsonElement fallbackElement)
    {
        var fallbackCount = FindResetOpportunityCount(fallbackElement);
        if (resetCredits is not null)
        {
            connection.ResetOpportunityCount = Math.Max(0, resetCredits.AvailableCount);
            connection.ResetOpportunityExpiresAt = resetCredits.FindNextExpiry();
            connection.ResetOpportunityReported = true;
        }
        else if (fallbackCount.HasValue)
        {
            connection.ResetOpportunityCount = Math.Max(0, fallbackCount.Value);
            connection.ResetOpportunityExpiresAt = null;
            connection.ResetOpportunityReported = true;
        }

        // Partial reports may reuse data only within this exact app-server connection.
        snapshot.ResetOpportunityCount = connection.ResetOpportunityCount;
        snapshot.ResetOpportunityExpiresAt = connection.ResetOpportunityExpiresAt;
        snapshot.ResetOpportunityReported = connection.ResetOpportunityReported;
    }

    private static int? FindResetOpportunityCount(JsonElement element)
    {
        string[] supportedNames =
        [
            "availableResetCount", "availableResets", "bankedResetCount",
            "bankedResets", "resetOpportunityCount", "resetCredits",
            "bonusResetCount", "complimentaryResetCount", "freeResetCount",
            "remainingResets"
        ];
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (supportedNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt32(out var value)) return Math.Max(0, value);
                var nested = FindResetOpportunityCount(property.Value);
                if (nested.HasValue) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindResetOpportunityCount(item);
                if (nested.HasValue) return nested;
            }
        }
        return null;
    }

    private static async Task DrainErrorsAsync(Connection connection)
    {
        var token = connection.Cancellation.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var line = await connection.Transport.ReadErrorAsync(token);
                if (line is null) break;
                connection.StartupIssue ??= DescribeStartupDiagnostic(line);
                connection.StartupIncompatible |= IsStartupProtocolDiagnostic(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private async Task StopAsync()
    {
        _initialized = false;
        CancellationTokenSource? cancellation;
        lock (_operationGate)
        {
            cancellation = _cts;
            _cts = null;
        }
        cancellation?.Cancel();
        await StopConnectionAsync();
        cancellation?.Dispose();
    }

    private async Task StopConnectionAsync()
    {
        _initialized = false;
        var connection = _connection;
        _connection = null;
        if (connection is null) return;
        connection.Broken = true;
        connection.Cancellation.Cancel();
        Kill(connection);
        foreach (var completion in connection.Pending.Values)
            completion.TrySetCanceled();
        await WaitForDrainAsync(Task.WhenAll(connection.ReadTask, connection.ErrorTask), TimeSpan.FromSeconds(1));
        connection.Transport.Dispose();
        connection.Cancellation.Dispose();
    }

    private static void Kill(Connection connection)
    {
        try
        {
            if (!connection.Transport.HasExited) connection.Transport.Kill();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private static async Task WaitForDrainAsync(Task task, TimeSpan timeout)
    {
        try { await task.WaitAsync(timeout); }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or ObjectDisposedException) { }
    }

    private void CancelActiveOperation(bool changingExecutable = false)
    {
        CancellationTokenSource? cancellation;
        lock (_operationGate)
        {
            _operationGeneration++;
            if (changingExecutable) _pendingChanges++;
            cancellation = _cts;
        }
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        CancelActiveOperation();
        // Keep the same lock order as RefreshAsync -> StartAsync.
        await _refreshLock.WaitAsync();
        await _startLock.WaitAsync();
        try { await StopAsync(); }
        finally { _startLock.Release(); _refreshLock.Release(); }
    }

    public async Task ChangeExecutableAsync(string? executable)
    {
        if (_disposed != 0) return;
        CancelActiveOperation(changingExecutable: true);
        await _refreshLock.WaitAsync();
        await _startLock.WaitAsync();
        try
        {
            await StopAsync();
            if (_disposed != 0) return;
            _executableOverride = executable;
            Installation = null;
        }
        finally
        {
            lock (_operationGate) _pendingChanges--;
            _startLock.Release();
            _refreshLock.Release();
        }
    }

    public static void ValidateAccount(JsonElement response)
    {
        if (!response.TryGetProperty("account", out var account) || account.ValueKind == JsonValueKind.Null)
            throw new CodexDiscoveryException("Codex CLI 尚未登录；请先在终端运行 codex login，再点击刷新。" );
        if (account.ValueKind != JsonValueKind.Object)
            throw new CodexDiscoveryException("Codex CLI 的账户协议不兼容，请升级 CLI 后重试。" );
        if (account.TryGetProperty("type", out var type) && type.GetString() == "apiKey")
            throw new CodexDiscoveryException("当前 CLI 使用 API Key；请使用具有 Codex 套餐额度的 ChatGPT 账户登录。" );
    }

    public static string DescribeConnectionError(Exception error)
    {
        if (error is CodexAppServerIncompatibleException)
            return "当前 Codex CLI 不支持 app-server 协议，请升级 CLI 或手动选择其他已安装版本。";
        if (error is CodexDiscoveryException) return error.Message;
        if (error is CodexProtocolException protocol)
        {
            if (protocol.Code is -32601 or -32602)
                return "当前 Codex CLI 协议不兼容，请升级 CLI 后刷新。";
            if (new[] { "not logged", "not authenticated", "unauthenticated", "login required", "token expired" }
                .Any(term => protocol.Message.Contains(term, StringComparison.OrdinalIgnoreCase)))
                return "Codex 登录已失效；请在终端运行 codex login 后刷新。";
            return "Codex 未返回可用限额；请检查账户登录、套餐支持及代理网络。";
        }
        return error switch
        {
            TimeoutException or OperationCanceledException => "Codex 连接超时，请检查代理网络后刷新。",
            System.ComponentModel.Win32Exception => "无法启动 Codex CLI，请检查文件权限或手动选择可访问的 codex.exe。",
            IOException => "Codex CLI 连接中断，下次刷新将重新自动识别。",
            _ => "暂时无法连接 Codex；可右键刷新按钮重新识别或手动选择 CLI。"
        };
    }

    public static string? DescribeStartupDiagnostic(string line)
    {
        if (line.Contains("failed to initialize sqlite state runtime", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("failed to initialize state runtime", StringComparison.OrdinalIgnoreCase))
            return "Codex 无法初始化本机数据目录；请检查当前用户对 CODEX_HOME 的访问权限，无需复制或修改登录文件。";
        if (line.Contains("unexpected argument", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("unrecognized subcommand", StringComparison.OrdinalIgnoreCase))
            return "当前 Codex CLI 不支持 app-server 协议，请升级 CLI 或手动选择其他已安装版本。";
        return null;
    }

    private static bool IsStartupProtocolDiagnostic(string line) =>
        (line.Contains("unexpected argument", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("--listen", StringComparison.OrdinalIgnoreCase)) ||
        (line.Contains("unrecognized subcommand", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("app-server", StringComparison.OrdinalIgnoreCase));
}

internal interface ICodexConnection : IDisposable
{
    bool HasExited { get; }
    ValueTask<string?> ReadOutputAsync(CancellationToken token);
    ValueTask<string?> ReadErrorAsync(CancellationToken token);
    Task WriteLineAsync(string line, CancellationToken token);
    void Kill();
}

internal sealed class CodexProcessConnection(Process process) : ICodexConnection
{
    public bool HasExited => process.HasExited;
    public ValueTask<string?> ReadOutputAsync(CancellationToken token) => process.StandardOutput.ReadLineAsync(token);
    public ValueTask<string?> ReadErrorAsync(CancellationToken token) => process.StandardError.ReadLineAsync(token);

    public async Task WriteLineAsync(string line, CancellationToken token)
    {
        await process.StandardInput.WriteLineAsync(line.AsMemory(), token);
        await process.StandardInput.FlushAsync(token);
    }

    public void Kill() => process.Kill(entireProcessTree: true);
    public void Dispose() => process.Dispose();
}

internal sealed class CodexAppServerIncompatibleException : Exception { }

internal sealed class CodexProtocolException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
