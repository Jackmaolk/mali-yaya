using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexUsageWidget;

public static partial class WidgetUpdateSettings
{
    public static string FilePath => WidgetDataPaths.UpdateSettingsPath;

    public static bool IsOwnedRepository(string? repository) => repository is not null &&
        RepositoryPattern().IsMatch(repository) && !repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\AJackmaolk/[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\z", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();

    public static WidgetUpdateOptions Load(string? path = null)
    {
        try
        {
            var options = JsonSerializer.Deserialize<WidgetUpdateOptions>(WidgetDataPaths.ReadBytes(path ?? FilePath, 8192));
            return options is not null && IsOwnedRepository(options.Repository) ? options : new();
        }
        catch (Exception ex) when (WidgetDataPaths.IsStorageError(ex) || ex is JsonException) { return new(); }
    }

    public static void Save(WidgetUpdateOptions options, string? path = null)
    {
        if (options.Repository is not null && !IsOwnedRepository(options.Repository) ||
            options.Enabled && options.Repository is null) throw new ArgumentException("请选择本软件所有者的具体仓库。", nameof(options));
        WidgetDataPaths.WriteJsonAtomic(path ?? FilePath, options);
    }
}
