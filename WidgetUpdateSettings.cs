using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexUsageWidget;

public static partial class WidgetUpdateSettings
{
    public static string FilePath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "CodexUsageWidget", "update-settings.json");

    public static bool IsOwnedRepository(string? repository) => repository is not null &&
        RepositoryPattern().IsMatch(repository) && !repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\AJackmaolk/[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\z", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();

    public static WidgetUpdateOptions Load(string? path = null)
    {
        try
        {
            var file = path ?? FilePath;
            if (new FileInfo(file).Length is < 2 or > 8192) return new();
            var options = JsonSerializer.Deserialize<WidgetUpdateOptions>(File.ReadAllText(file));
            return options is not null && IsOwnedRepository(options.Repository) ? options : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public static void Save(WidgetUpdateOptions options, string? path = null)
    {
        if (options.Repository is not null && !IsOwnedRepository(options.Repository) ||
            options.Enabled && options.Repository is null) throw new ArgumentException("请选择本软件所有者的具体仓库。", nameof(options));
        var file = Path.GetFullPath(path ?? FilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(options));
            File.Move(temporary, file, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
