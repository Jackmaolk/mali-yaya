using System.IO;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexUsageWidget;

public sealed record TiboChineseBrief(string Text, string Provider);

// Display-only machine translation; never supplies text to reset classification.
public sealed class TiboChineseBriefClient : IDisposable
{
    private const int MaxEntries = 32;
    private const int MaxTextLength = 50_000;
    private const long MaxCacheBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan SuccessLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PostTimeTolerance = TimeSpan.FromMinutes(10);
    private static readonly Regex SourcePattern = new(
        @"\Ahttps://x\.com/thsottiaux/status/([0-9]{12,22})\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.CultureInvariant);
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly string _cacheDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, CacheEntry> _cache = [];
    private bool _disposed;

    public TiboChineseBriefClient(HttpClient? client = null, string? cacheDirectory = null,
        TimeProvider? timeProvider = null)
    {
        _ownsClient = client is null;
        _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 1024 * 1024
        };
        _cacheDirectory = cacheDirectory ?? AppContext.BaseDirectory;
        _timeProvider = timeProvider ?? TimeProvider.System;
        LoadCache();
    }

    public static string CacheKey(TiboPost post)
    {
        ArgumentNullException.ThrowIfNull(post);
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(post.SourceUrl + "\n" + post.Text)));
    }

    public async Task<TiboChineseBrief?> GetAsync(TiboPost post,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (post.SourceUrl is null || post.SourceUrl.Length > 128 || !ValidText(post.Text))
                return null;
            var match = SourcePattern.Match(post.SourceUrl);
            if (!match.Success) return null;

            var now = _timeProvider.GetUtcNow();
            PruneCache(now);
            var key = CacheKey(post);
            if (_cache.TryGetValue(key, out var cached) &&
                (cached.PostTime - post.PostTime).Duration() <= PostTimeTolerance)
                return cached.Brief;

            CacheEntry? entry;
            try
            {
                entry = await FetchAsync(post, match.Groups[1].Value, key, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or
                                       JsonException or IOException)
            {
                entry = null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            _cache[key] = entry ?? new(key, null, _timeProvider.GetUtcNow(), post.PostTime);
            PruneCache(_timeProvider.GetUtcNow());
            if (entry is not null) SaveCache();
            return entry?.Brief;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CacheEntry?> FetchAsync(TiboPost post, string id, string key,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.fxtwitter.com/2/status/{id}?lang=zh");
        request.Headers.UserAgent.ParseAdd("CodexUsageWidget/1.0");
        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (json.Length > 1024 * 1024) return null;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number ||
            !code.TryGetInt32(out var codeValue) || codeValue != 200 ||
            !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object ||
            ReadString(status, "id") != id ||
            !status.TryGetProperty("author", out var author) ||
            !string.Equals(ReadString(author, "screen_name"), "thsottiaux", StringComparison.OrdinalIgnoreCase) ||
            !status.TryGetProperty("created_timestamp", out var timestamp) ||
            timestamp.ValueKind != JsonValueKind.Number || !timestamp.TryGetInt64(out var seconds) ||
            seconds < DateTimeOffset.MinValue.ToUnixTimeSeconds() ||
            seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            return null;

        var postTime = DateTimeOffset.FromUnixTimeSeconds(seconds);
        var original = ReadString(status, "text");
        if ((postTime - post.PostTime).Duration() > PostTimeTolerance || !ValidText(original) ||
            !string.Equals(NormalizeWhitespace(original!), NormalizeWhitespace(post.Text), StringComparison.Ordinal) ||
            !status.TryGetProperty("translation", out var translation))
            return null;

        var language = ReadString(translation, "target_lang");
        if (!string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(language, "zh-cn", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(language, "zh-hans", StringComparison.OrdinalIgnoreCase))
            return null;
        var brief = new TiboChineseBrief(ReadString(translation, "text")!, ReadString(translation, "provider")!);
        return ValidBrief(brief) ? new(key, brief, _timeProvider.GetUtcNow(), postTime) : null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string NormalizeWhitespace(string text) => WhitespacePattern.Replace(text, " ").Trim();

    private static bool ValidText(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextLength;

    private static bool ValidBrief(TiboChineseBrief? brief) =>
        brief is not null && ValidText(brief.Text) && ContainsHan(brief.Text) &&
        !string.IsNullOrWhiteSpace(brief.Provider) && brief.Provider.Length <= 64 &&
        !brief.Provider.Any(char.IsControl);

    private static bool ContainsHan(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or
                >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x2FA1F or >= 0x30000 and <= 0x323AF)
                return true;
        }
        return false;
    }

    private static bool Fresh(CacheEntry entry, DateTimeOffset now) =>
        entry.StoredAt <= now && now - entry.StoredAt <
        (entry.Brief is null ? FailureLifetime : SuccessLifetime);

    private void PruneCache(DateTimeOffset now)
    {
        foreach (var key in _cache.Where(pair => !Fresh(pair.Value, now)).Select(pair => pair.Key).ToArray())
            _cache.Remove(key);
        while (_cache.Count > MaxEntries)
            _cache.Remove(_cache.MinBy(pair => pair.Value.StoredAt).Key);
    }

    private void LoadCache()
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(_cacheDirectory, "tibo-chinese-cache.json"));
            if (stream.Length > MaxCacheBytes) return;
            var entries = JsonSerializer.Deserialize<List<CacheEntry?>>(stream);
            if (entries is null) return;
            var now = _timeProvider.GetUtcNow();
            foreach (var entry in entries)
            {
                if (entry is null || entry.Key is null || entry.Key.Length != 64 ||
                    !entry.Key.All(Uri.IsHexDigit) || !ValidBrief(entry.Brief) || !Fresh(entry, now))
                    continue;
                if (!_cache.TryGetValue(entry.Key, out var existing) || existing.StoredAt < entry.StoredAt)
                    _cache[entry.Key] = entry;
                PruneCache(now);
            }
        }
        catch (Exception ex) when (CacheError(ex))
        {
            // A missing or damaged optional cache must never disable translation.
        }
    }

    private void SaveCache()
    {
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var cachePath = Path.Combine(_cacheDirectory, "tibo-chinese-cache.json");
            temporaryPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, _cache.Values.Where(entry => entry.Brief is not null).ToArray());
            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        catch (Exception ex) when (CacheError(ex))
        {
            // Keep the validated network result even when the cache is not writable.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (CacheError(ex)) { }
            }
        }
    }

    private static bool CacheError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or
            NotSupportedException or SecurityException;

    private sealed record CacheEntry(string Key, TiboChineseBrief? Brief, DateTimeOffset StoredAt,
        DateTimeOffset PostTime);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsClient) _client.Dispose();
        // In-flight requests still release the gate after HTTP cancellation on shutdown.
    }
}
