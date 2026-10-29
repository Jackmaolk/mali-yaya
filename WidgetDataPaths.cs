using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexUsageWidget;

public static class WidgetDataPaths
{
    public const string DataDirectoryOverride = "MaliYaya.DataDirectory";

    public static string RootDirectory
    {
        get
        {
            var configured = AppContext.GetData(DataDirectoryOverride);
            if (configured is not null && configured is not string)
                throw new InvalidDataException("Invalid data directory override.");
            var path = configured as string ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageWidget");
            return LocalDirectory(path);
        }
    }

    public static string CacheDirectory => LocalDirectory(Path.Combine(RootDirectory, "cache"));
    public static string Root => RootDirectory;
    public static string ConnectionSettingsPath => LocalFile(Path.Combine(RootDirectory, "connection-settings.json"));
    public static string UpdateSettingsPath => LocalFile(Path.Combine(RootDirectory, "update-settings.json"));

    public static string RuntimeStatusPath(string? installationDirectory = null) =>
        RuntimeFile("widget-runtime.json", installationDirectory);

    public static string AccountRuntimePath(string? installationDirectory = null) =>
        RuntimeFile("account-runtime.json", installationDirectory);

    private static string RuntimeFile(string name, string? installationDirectory)
    {
        var installation = LocalDirectory(installationDirectory ?? AppContext.BaseDirectory);
        // Windows paths are case-insensitive. Hash the canonical local directory, not the working directory.
        var canonical = installation.TrimEnd('\\', '/').ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return LocalFile(Path.Combine(RootDirectory, "runtime", hash, name));
    }

    public static string LocalDirectory(string path) => LocalPath(path, directory: true);
    public static string LocalFile(string path) => LocalPath(path, directory: false);

    public static string EnsureSafeDirectory(string path)
    {
        var full = LocalDirectory(path);
        Directory.CreateDirectory(full);
        return LocalDirectory(full);
    }

    public static void RejectReparseAncestors(string path) => LocalPath(path, directory: null);

    private static string LocalPath(string path, bool? directory)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' ||
            path[2] is not ('\\' or '/') || path.AsSpan(2).Contains(':'))
            throw new InvalidDataException("An absolute local drive path is required.");
        try
        {
            foreach (var segment in path[3..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment is "." or "..") continue;
                var stem = segment.Split('.')[0].TrimEnd(' ');
                if (segment.EndsWith(' ') || segment.EndsWith('.') ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    IsDeviceName(stem))
                    throw new InvalidDataException("Ambiguous or device path rejected.");
            }
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var root = Path.GetPathRoot(full) ?? throw new InvalidDataException("A drive is required.");
            var drive = new DriveInfo(root);
            if (drive.DriveType is DriveType.Network or DriveType.Unknown or DriveType.NoRootDirectory)
                throw new InvalidDataException("A local drive is required.");
            string? current = full;
            var leaf = true;
            while (current is not null)
            {
                try
                {
                    var attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Reparse paths are not allowed.");
                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if ((!leaf || directory == true) && !isDirectory || leaf && directory == false && isDirectory)
                        throw new InvalidDataException("Unexpected storage path type.");
                }
                catch (FileNotFoundException) { /* Missing components may be created after validation. */ }
                catch (DirectoryNotFoundException) { /* Still inspect every existing ancestor. */ }
                current = Path.GetDirectoryName(current);
                leaf = false;
            }
            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException("Invalid storage path.", ex);
        }
    }

    private static bool IsDeviceName(string stem) =>
        stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
        stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
        stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
        stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
        stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
        stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
        stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
            stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            (stem[3] is >= '1' and <= '9' or '\u00B9' or '\u00B2' or '\u00B3');

    internal static byte[] ReadBytes(string path, int maximumBytes)
    {
        path = LocalFile(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        LocalFile(path);
        if (stream.Length is <= 0 || stream.Length > maximumBytes)
            throw new InvalidDataException("Storage record exceeds its size limit.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public static void WriteJsonAtomic<T>(string path, T value)
    {
        path = LocalFile(path);
        EnsureSafeDirectory(Path.GetDirectoryName(path)!);
        var temporary = LocalFile(path + ".tmp-" + Guid.NewGuid().ToString("N"));
        var created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                JsonSerializer.Serialize(output, value);
                output.Flush(flushToDisk: true);
            }
            LocalFile(path);
            LocalFile(temporary);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (created)
            {
                try { File.Delete(LocalFile(temporary)); }
                catch (Exception ex) when (IsStorageError(ex))
                {
                    Debug.WriteLine("Unable to clean up a storage temporary file.");
                }
            }
        }
    }

    internal static bool IsStorageError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or SecurityException;
}
