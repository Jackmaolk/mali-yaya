using System.IO;
using System.Text.Json;

namespace CodexUsageWidget;

public sealed record WidgetPreferences(bool TiboEnabled = true, bool CompactMode = false)
{
    private static string DefaultPath => Path.Combine(WidgetDataPaths.Root, "preferences.json");

    public static WidgetPreferences Load(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            WidgetDataPaths.RejectReparseAncestors(path);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (input.Length is < 1 or > 8192) return new(false, false);
            var bytes = new byte[(int)input.Length];
            input.ReadExactly(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(false, false);
            var names = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Length != 2 || names.Distinct(StringComparer.Ordinal).Count() != 2 ||
                !names.Contains(nameof(TiboEnabled)) || !names.Contains(nameof(CompactMode))) return new(false, false);
            return new(root.GetProperty(nameof(TiboEnabled)).GetBoolean(), root.GetProperty(nameof(CompactMode)).GetBoolean());
        }
        // Only a validated, missing settings file is a first run; invalid settings stay offline.
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (Exception error) when (WidgetDataPaths.IsStorageError(error) ||
            error is JsonException or InvalidOperationException)
        { return new(false, false); }
    }

    public static void Save(WidgetPreferences preferences, string? path = null)
    {
        path ??= DefaultPath;
        WidgetDataPaths.RejectReparseAncestors(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        WidgetDataPaths.EnsureSafeDirectory(directory);
        WidgetDataPaths.RejectReparseAncestors(path);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, preferences);
                output.Flush(true);
            }
            WidgetDataPaths.RejectReparseAncestors(path);
            File.Move(temporary, path, true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}

// UI-thread-owned session: an old asynchronous response cannot survive off/on toggles.
internal sealed class TiboNetworkGate : IDisposable
{
    private CancellationTokenSource? _session;
    private int _generation;
    private bool _disposed;
    public bool Enabled => !_disposed && _session is not null;

    public void SetEnabled(bool enabled)
    {
        if (_disposed || enabled == Enabled) return;
        _generation++;
        var previous = _session;
        _session = enabled ? new CancellationTokenSource() : null;
        previous?.Cancel();
        previous?.Dispose();
    }

    public bool TryCapture(out CancellationToken token, out int generation)
    {
        token = _session?.Token ?? new CancellationToken(true);
        generation = _generation;
        return Enabled;
    }

    public bool IsCurrent(int generation) => Enabled && generation == _generation;
    public void Dispose() { SetEnabled(false); _disposed = true; }
}
