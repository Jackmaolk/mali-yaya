using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexUsageWidget;

public record WidgetUpdateOptions(bool Enabled = false, string? Repository = null);
public record WidgetUpdateState(DateTimeOffset? LastAttemptUtc = null, string Status = "自动更新已关闭");
public record PreparedWidgetUpdate(string Version, string PayloadDirectory, IReadOnlyDictionary<string, string> Hashes);

public sealed class WidgetUpdateService : IDisposable
{
    private const int MetadataLimit = 1024 * 1024;
    private const int DownloadLimit = 20 * 1024 * 1024;
    private const int ExpandedLimit = 40 * 1024 * 1024;
    private const int ManifestLimit = 32 * 1024;
    private const string Disabled = "自动更新已关闭";
    private const string Unconfigured = "请配置自己的 GitHub 更新仓库";
    private const string Checking = "正在检查更新";
    private const string CoolingDown = "更新检查冷却中（24 小时）";
    private const string InvalidState = "更新记录不可用，已停止检查";
    private const string Failed = "更新检查或校验失败，旧版本未改动";
    private const string Canceled = "更新检查已取消，冷却仍然有效";
    private const string Current = "暂无可用的新版本";
    private const string Ready = "更新已校验并暂存，等待应用";
    private const string Busy = "更新检查正在进行";
    private static readonly TimeSpan Cooldown = TimeSpan.FromHours(24);
    private static readonly Regex RepositoryPattern = new(
        @"\AJackmaolk/[A-Za-z0-9][A-Za-z0-9._-]{0,99}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex TagPattern = new(
        @"\Av[0-9]{4}\.[0-9]{2}\.[0-9]{2}\.(0|[1-9][0-9]{0,9})\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly string[] RuntimeFiles =
    [
        "CodexUsageWidget.dll", "CodexUsageWidget.exe",
        "CodexUsageWidget.deps.json", "CodexUsageWidget.runtimeconfig.json"
    ];
    private static readonly HashSet<string> EntryNames = new(RuntimeFiles.Append("release-manifest.json"), StringComparer.Ordinal);
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _directory;
    private readonly string _statePath;
    private readonly bool _storageUnavailable;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private WidgetUpdateState _state = new();
    private int _disposed;

    public WidgetUpdateService(HttpClient? http = null, string? stateDirectory = null, TimeProvider? clock = null)
    {
        try
        {
            _directory = WidgetDataPaths.LocalDirectory(stateDirectory ?? Path.Combine(WidgetDataPaths.Root, "updates"));
            _statePath = Path.Combine(_directory, "update-state.json");
        }
        catch (Exception error) when (WidgetDataPaths.IsStorageError(error))
        {
            _directory = _statePath = string.Empty;
            _storageUnavailable = true;
        }
        _clock = clock ?? TimeProvider.System;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            DefaultProxyCredentials = null,
            UseProxy = true,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            MaxResponseHeadersLength = 32
        }) { Timeout = Timeout.InfiniteTimeSpan };
        if (!_storageUnavailable && TryReadState(out var saved)) _state = saved;
        else _state = new(null, InvalidState);
    }

    public WidgetUpdateState State => Volatile.Read(ref _state);
    public event Action<string>? StatusChanged;

    public async Task<PreparedWidgetUpdate?> CheckAsync(WidgetUpdateOptions options, CancellationToken token = default)
    {
        if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return null;
        string? ownedStage = null;
        try
        {
            if (Volatile.Read(ref _disposed) != 0) { SetStatus(Failed); return null; }
            ArgumentNullException.ThrowIfNull(options);
            if (!options.Enabled) { SetStatus(Disabled); return null; }
            if (_storageUnavailable) { SetStatus(InvalidState); return null; }
            var repository = options.Repository;
            if (repository is null || repository.Length > 110 || !RepositoryPattern.IsMatch(repository) || repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            { SetStatus(Unconfigured); return null; }

            EnsureSafeDirectory(_directory);
            var lockPath = Path.Combine(_directory, "update.lock");
            RejectReparseAncestors(lockPath);
            FileStream lease;
            try { lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { SetStatus(Busy); return null; }
            using (lease)
            {
                if (!TryReadState(out var saved)) { SetStatus(InvalidState); return null; }
                // Keep the in-memory stamp too: deleting the file cannot reset a live instance.
                var last = saved.LastAttemptUtc;
                if (State.LastAttemptUtc is { } memory && (last is null || memory > last.Value)) last = memory;
                var now = _clock.GetUtcNow();
                if (last is { } previous && (now < previous || now - previous < Cooldown))
                {
                    Volatile.Write(ref _state, new(previous, CoolingDown));
                    Notify(CoolingDown);
                    return null;
                }

                var attempted = new WidgetUpdateState(now, Checking);
                // No cancellation check before this durable stamp: canceled attempts also consume cooldown.
                WriteState(attempted);
                Volatile.Write(ref _state, attempted);
                Notify(Checking);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), _clock);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token, _lifetime.Token);
                var bounded = linked.Token;
                bounded.ThrowIfCancellationRequested();

                // Injected clients are fixture transports. Refuse all ambient headers without reading values.
                // Their handlers must also disable redirects, cookies and credentials, as the owned handler does.
                if (_http.DefaultRequestHeaders.Any()) throw new InvalidDataException();
                using var metadataResponse = await SendAsync(new Uri($"https://api.github.com/repos/{repository}/releases/latest"), bounded).ConfigureAwait(false);
                RequireSuccess(metadataResponse);
                using var metadata = await ReadBodyAsync(metadataResponse, MetadataLimit, null, bounded).ConfigureAwait(false);
                using var releaseDocument = ParseJson(metadata);
                var asset = SelectAsset(releaseDocument.RootElement, repository);
                if (asset is null) { SetStatus(Current); return null; }

                using var zipBytes = await DownloadAsync(asset, bounded).ConfigureAwait(false);
                if (!EqualHash(Convert.ToHexString(SHA256.HashData(zipBytes.GetBuffer().AsSpan(0, checked((int)zipBytes.Length)))), asset.Hash))
                    throw new InvalidDataException();
                bounded.ThrowIfCancellationRequested();
                ValidateZipDirectory(zipBytes);
                zipBytes.Position = 0;
                using var archive = new ZipArchive(zipBytes, ZipArchiveMode.Read, leaveOpen: true);
                var entries = ValidateEntries(archive);
                using var manifestStream = entries["release-manifest.json"].Open();
                using var manifestBytes = await CopyBoundedAsync(manifestStream, ManifestLimit,
                    entries["release-manifest.json"].Length, bounded).ConfigureAwait(false);
                using var manifest = ParseJson(manifestBytes);
                var hashes = ValidateManifest(manifest.RootElement, asset.Version);
                using var runtimeStream = entries["CodexUsageWidget.runtimeconfig.json"].Open();
                using var runtimeBytes = await CopyBoundedAsync(runtimeStream, ManifestLimit,
                    entries["CodexUsageWidget.runtimeconfig.json"].Length, bounded).ConfigureAwait(false);
                using var runtime = ParseJson(runtimeBytes);
                ValidateRuntime(runtime.RootElement);

                // The archive and manifest are verified before creating a new, unguessable payload directory.
                var candidate = Path.Combine(_directory, "staged-" + Guid.NewGuid().ToString("N"));
                RejectReparseAncestors(candidate);
                if (Directory.Exists(candidate) || File.Exists(candidate)) throw new IOException();
                Directory.CreateDirectory(candidate);
                ownedStage = candidate;
                RejectReparseAncestors(candidate);
                var payload = Path.Combine(candidate, "payload");
                EnsureSafeDirectory(payload);
                long expanded = 0;
                foreach (var name in RuntimeFiles)
                {
                    bounded.ThrowIfCancellationRequested();
                    var entry = entries[name];
                    var target = Path.Combine(payload, name);
                    RejectReparseAncestors(target);
                    using var input = entry.Open();
                    using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        64 * 1024, FileOptions.Asynchronous);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var length = await CopyEntryAsync(input, output, entry.Length, hash, bounded).ConfigureAwait(false);
                    expanded = checked(expanded + length);
                    if (expanded > ExpandedLimit || !EqualHash(Convert.ToHexString(hash.GetHashAndReset()), hashes[name]))
                        throw new InvalidDataException();
                    await output.FlushAsync(bounded).ConfigureAwait(false);
                }
                bounded.ThrowIfCancellationRequested();
                var result = new PreparedWidgetUpdate(asset.Version, payload, new ReadOnlyDictionary<string, string>(hashes));
                SetStatus(Ready);
                ownedStage = null;
                return result;
            }
        }
        catch (OperationCanceledException) { SetStatus(Canceled); return null; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            System.Security.SecurityException or JsonException or HttpRequestException or ArgumentException or
            InvalidOperationException or NotSupportedException or OverflowException or CryptographicException or KeyNotFoundException)
        { SetStatus(Failed); return null; }
        finally
        {
            if (ownedStage is not null) CleanupOwnedStage(ownedStage);
            _gate.Release();
        }
    }

    private sealed record Asset(string Version, Uri Url, long Size, string Hash);

    private static Asset? SelectAsset(JsonElement root, string repository)
    {
        RequireObject(root);
        if (root.GetProperty("draft").ValueKind != JsonValueKind.False ||
            root.GetProperty("prerelease").ValueKind != JsonValueKind.False) return null;
        var tag = root.GetProperty("tag_name").GetString();
        if (tag is null || !TagPattern.IsMatch(tag)) throw new InvalidDataException();
        var versionText = tag[1..];
        if (!DateOnly.TryParseExact(versionText[..10], "yyyy.MM.dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _) || !Version.TryParse(versionText, out var version) ||
            !Version.TryParse(WidgetRuntimeStatus.BuildVersion, out var current)) throw new InvalidDataException();
        if (version <= current) return null;
        var name = $"mali-yaya-{versionText}-windows-x64.zip";
        var assets = root.GetProperty("assets");
        if (assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() > 256) throw new InvalidDataException();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonElement? selected = null;
        foreach (var entry in assets.EnumerateArray())
        {
            RequireObject(entry);
            var entryName = entry.GetProperty("name").GetString();
            if (string.IsNullOrEmpty(entryName) || !names.Add(entryName)) throw new InvalidDataException();
            if (entryName == name) selected = entry;
        }
        if (selected is not { } file) throw new InvalidDataException();
        if (file.GetProperty("state").GetString() != "uploaded" ||
            !file.GetProperty("size").TryGetInt64(out var size) || size is < 1 or > DownloadLimit)
            throw new InvalidDataException();
        var digest = file.GetProperty("digest").GetString();
        if (digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal) || !IsHash(digest[7..]))
            throw new InvalidDataException();
        var urlText = file.GetProperty("browser_download_url").GetString();
        var expected = $"https://github.com/{repository}/releases/download/{tag}/{name}";
        // Compare the original string too; Uri normalization must not hide ports, escapes, dot segments or whitespace.
        if (!string.Equals(urlText, expected, StringComparison.Ordinal) || !Uri.TryCreate(urlText, UriKind.Absolute, out var url))
            throw new InvalidDataException();
        return new(versionText, url, size, digest[7..].ToLowerInvariant());
    }

    private async Task<MemoryStream> DownloadAsync(Asset asset, CancellationToken token)
    {
        var url = asset.Url;
        for (var redirects = 0; ; redirects++)
        {
            using var response = await SendAsync(url, token).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                if (redirects >= 3 || response.Headers.Location is not { } next || !IsAllowedRedirect(next))
                    throw new InvalidDataException();
                url = next;
                continue;
            }
            RequireSuccess(response);
            return await ReadBodyAsync(response, DownloadLimit, asset.Size, token).ConfigureAwait(false);
        }
    }

    private static bool IsAllowedRedirect(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort ||
            !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Fragment) ||
            (url.Host != "release-assets.githubusercontent.com" && url.Host != "objects.githubusercontent.com")) return false;
        var prefix = "https://" + url.Host + "/";
        return url.OriginalString.StartsWith(prefix, StringComparison.Ordinal) &&
            !url.OriginalString.Any(character => char.IsControl(character) || char.IsWhiteSpace(character) || character == '\\');
    }

    private async Task<HttpResponseMessage> SendAsync(Uri url, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("CodexUsageWidget-Update/1.0");
        request.Headers.Accept.ParseAdd(url.Host == "api.github.com" ? "application/vnd.github+json" : "application/octet-stream");
        var sending = _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        HttpResponseMessage response;
        try { response = await sending.WaitAsync(token).ConfigureAwait(false); }
        catch
        {
            // A fixture handler may ignore cancellation; dispose any response that arrives after the bounded wait.
            _ = sending.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully) task.Result.Dispose();
                else _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
        if (response.RequestMessage?.RequestUri is { } actual && actual != url)
        { response.Dispose(); throw new InvalidDataException(); }
        return response;
    }

    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Any())
            throw new InvalidDataException();
    }

    private static async Task<MemoryStream> ReadBodyAsync(HttpResponseMessage response, int limit, long? expected, CancellationToken token)
    {
        var declared = response.Content.Headers.ContentLength;
        if (declared is < 0 || declared > limit || (expected.HasValue && declared.HasValue && expected != declared))
            throw new InvalidDataException();
        using var input = await response.Content.ReadAsStreamAsync(token).WaitAsync(token).ConfigureAwait(false);
        return await CopyBoundedAsync(input, limit, expected ?? declared, token).ConfigureAwait(false);
    }

    private static async Task<MemoryStream> CopyBoundedAsync(Stream input, int limit, long? expected, CancellationToken token)
    {
        var output = new MemoryStream();
        try
        {
            var buffer = new byte[64 * 1024];
            var bound = expected.HasValue ? Math.Min(limit, expected.Value) : limit;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, bound - output.Length + 1)), token)
                    .AsTask().WaitAsync(token).ConfigureAwait(false);
                if (read == 0) break;
                if (output.Length + read > bound) throw new InvalidDataException();
                output.Write(buffer, 0, read);
            }
            if (expected.HasValue && output.Length != expected.Value) throw new InvalidDataException();
            output.Position = 0;
            return output;
        }
        catch { output.Dispose(); throw; }
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateEntries(ZipArchive archive)
    {
        if (archive.Entries.Count != 5) throw new InvalidDataException();
        var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var unixType = ((uint)entry.ExternalAttributes >> 16) & 0xF000;
            if (!EntryNames.Contains(entry.FullName) || !result.TryAdd(entry.FullName, entry) ||
                entry.Name != entry.FullName ||
                (entry.ExternalAttributes & ((int)FileAttributes.Directory | (int)FileAttributes.ReparsePoint)) != 0 ||
                (unixType != 0 && unixType != 0x8000) || entry.Length < 0 || entry.Length > DownloadLimit ||
                (entry.FullName == "release-manifest.json" && entry.Length > ManifestLimit)) throw new InvalidDataException();
            total = checked(total + entry.Length);
            if (total > ExpandedLimit) throw new InvalidDataException();
        }
        return result;
    }

    private static void ValidateZipDirectory(MemoryStream bytes)
    {
        // Bound central-directory allocation before ZipArchive materializes entries. Five small root names
        // need neither ZIP64 nor a multi-disk archive, and cannot justify a huge central directory.
        var data = bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length));
        for (var offset = data.Length - 22; offset >= Math.Max(0, data.Length - 65557); offset--)
        {
            var end = data[offset..];
            if (BinaryPrimitives.ReadUInt32LittleEndian(end) != 0x06054b50 ||
                end.Length != 22 + BinaryPrimitives.ReadUInt16LittleEndian(end[20..])) continue;
            var size = BinaryPrimitives.ReadUInt32LittleEndian(end[12..]);
            var start = BinaryPrimitives.ReadUInt32LittleEndian(end[16..]);
            if (BinaryPrimitives.ReadUInt16LittleEndian(end[4..]) != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(end[6..]) != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(end[8..]) != 5 ||
                BinaryPrimitives.ReadUInt16LittleEndian(end[10..]) != 5 || size > ManifestLimit ||
                (long)start + size != offset) throw new InvalidDataException();
            var cursor = checked((int)start);
            for (var index = 0; index < 5; index++)
            {
                if (cursor > offset - 46 || BinaryPrimitives.ReadUInt32LittleEndian(data[cursor..]) != 0x02014b50)
                    throw new InvalidDataException();
                var entry = data[cursor..];
                if (BinaryPrimitives.ReadUInt16LittleEndian(entry[34..]) != 0) throw new InvalidDataException();
                cursor = checked(cursor + 46 + BinaryPrimitives.ReadUInt16LittleEndian(entry[28..]) +
                    BinaryPrimitives.ReadUInt16LittleEndian(entry[30..]) + BinaryPrimitives.ReadUInt16LittleEndian(entry[32..]));
            }
            if (cursor != offset) throw new InvalidDataException();
            return;
        }
        throw new InvalidDataException();
    }

    private static Dictionary<string, string> ValidateManifest(JsonElement root, string version)
    {
        RequireObject(root);
        if (root.EnumerateObject().Count() != 5 || root.GetProperty("product").GetString() != "mali-yaya" ||
            root.GetProperty("version").GetString() != version || root.GetProperty("platform").GetString() != "win-x64" ||
            !root.GetProperty("runtimeMajor").TryGetInt32(out var major) || major != 10) throw new InvalidDataException();
        var files = root.GetProperty("files");
        RequireObject(files);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in files.EnumerateObject())
        {
            var hash = property.Value.GetString();
            if (!RuntimeFiles.Contains(property.Name, StringComparer.Ordinal) || !IsHash(hash) ||
                !hashes.TryAdd(property.Name, hash!.ToLowerInvariant())) throw new InvalidDataException();
        }
        if (hashes.Count != 4) throw new InvalidDataException();
        return hashes;
    }

    private static void ValidateRuntime(JsonElement root)
    {
        RequireObject(root);
        var options = root.GetProperty("runtimeOptions");
        RequireObject(options);
        if (options.GetProperty("tfm").GetString() != "net10.0" ||
            options.TryGetProperty("framework", out _) || options.TryGetProperty("includedFrameworks", out _) ||
            options.TryGetProperty("additionalProbingPaths", out _) || options.TryGetProperty("rollForwardOnNoCandidateFx", out _))
            throw new InvalidDataException();
        if (options.TryGetProperty("rollForward", out var roll) &&
            roll.GetString() is not ("Disable" or "LatestPatch" or "Minor" or "LatestMinor")) throw new InvalidDataException();
        if (options.TryGetProperty("configProperties", out var properties)) RequireObject(properties);
        var frameworks = options.GetProperty("frameworks");
        if (frameworks.ValueKind != JsonValueKind.Array || frameworks.GetArrayLength() != 2) throw new InvalidDataException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var framework in frameworks.EnumerateArray())
        {
            RequireObject(framework);
            var name = framework.GetProperty("name").GetString();
            var version = framework.GetProperty("version").GetString();
            if (framework.EnumerateObject().Count() != 2 || name is not ("Microsoft.NETCore.App" or "Microsoft.WindowsDesktop.App") ||
                !names.Add(name) || version is null ||
                !Regex.IsMatch(version, @"\A10\.0\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking) ||
                !Version.TryParse(version, out _)) throw new InvalidDataException();
        }
    }

    private static async Task<long> CopyEntryAsync(Stream input, Stream output, long expected,
        IncrementalHash hash, CancellationToken token)
    {
        long total = 0;
        var buffer = new byte[64 * 1024];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, expected - total + 1)), token)
                .AsTask().WaitAsync(token).ConfigureAwait(false);
            if (read == 0) break;
            total = checked(total + read);
            if (total > expected || total > DownloadLimit) throw new InvalidDataException();
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
        if (total != expected) throw new InvalidDataException();
        return total;
    }

    private static JsonDocument ParseJson(MemoryStream bytes)
    {
        var document = JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, checked((int)bytes.Length)),
            new JsonDocumentOptions { MaxDepth = 16 });
        try { RejectDuplicateProperties(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException();
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }

    private static void RequireObject(JsonElement value)
    { if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException(); }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool EqualHash(string actual, string expected) => IsHash(actual) && IsHash(expected) &&
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expected));

    private bool TryReadState(out WidgetUpdateState saved)
    {
        saved = new();
        try
        {
            RejectReparseAncestors(_statePath);
            using var input = new FileStream(_statePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (input.Length is < 1 or > 4096) return false;
            var bytes = new byte[(int)input.Length];
            input.ReadExactly(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            RequireObject(root);
            RejectDuplicateProperties(root);
            if (root.EnumerateObject().Count() != 2 ||
                !root.GetProperty("LastAttemptUtc").TryGetDateTimeOffset(out var last) ||
                root.GetProperty("Status").GetString() is not { } status ||
                !IsKnownStatus(status)) return false;
            saved = new(last, status);
            return true;
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return !Directory.Exists(_directory); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            System.Security.SecurityException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        { return false; }
    }

    private static bool IsKnownStatus(string status) => status is Disabled or Unconfigured or Checking or
        CoolingDown or InvalidState or Failed or Canceled or Current or Ready or Busy;

    private void WriteState(WidgetUpdateState state)
    {
        EnsureSafeDirectory(_directory);
        RejectReparseAncestors(_statePath);
        var temporary = Path.Combine(_directory, "update-state-" + Guid.NewGuid().ToString("N") + ".tmp");
        var created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                JsonSerializer.Serialize(output, state);
                output.Flush(flushToDisk: true);
            }
            RejectReparseAncestors(_statePath);
            File.Move(temporary, _statePath, overwrite: true);
        }
        finally
        {
            if (created)
            {
                try { RejectReparseAncestors(temporary); File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        }
    }

    private static void EnsureSafeDirectory(string path)
    {
        RejectReparseAncestors(path);
        Directory.CreateDirectory(path);
        RejectReparseAncestors(path);
    }

    private static void RejectReparseAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException();
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private void CleanupOwnedStage(string stage)
    {
        try
        {
            var full = Path.GetFullPath(stage);
            var name = Path.GetFileName(full);
            if (!string.Equals(Path.GetDirectoryName(full), _directory, StringComparison.OrdinalIgnoreCase) ||
                !name.StartsWith("staged-", StringComparison.Ordinal) || !Guid.TryParseExact(name[7..], "N", out _)) return;
            RejectReparseAncestors(full);
            // Never recursively follow a directory inserted by another process; delete only our fixed files.
            var payload = Path.Combine(full, "payload");
            RejectReparseAncestors(payload);
            if (Directory.Exists(payload))
            {
                foreach (var file in RuntimeFiles)
                {
                    var path = Path.Combine(payload, file);
                    RejectReparseAncestors(path);
                    File.Delete(path);
                }
                Directory.Delete(payload, recursive: false);
            }
            if (Directory.Exists(full)) Directory.Delete(full, recursive: false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or ArgumentException or NotSupportedException) { }
    }

    private void SetStatus(string status)
    {
        Volatile.Write(ref _state, State with { Status = status });
        Notify(status);
    }

    private void Notify(string status)
    {
        var handlers = StatusChanged;
        if (handlers is null) return;
        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            // Observers must not interrupt the durable stamp, security checks or partial-stage cleanup.
            try { handler(status); }
            catch (Exception) { }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        if (_ownsHttp) _http.Dispose();
        // The gate/CTS remain usable by an in-flight check's finally block; neither owns an OS wait handle here.
    }
}
