using System.IO;
using System.Runtime.CompilerServices;

namespace CodexUsageWidget.Tests;

internal static class TestDataIsolation
{
    internal static string DirectoryPath { get; private set; } = null!;

    [ModuleInitializer]
    internal static void Initialize()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "MaliYaya.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        // Set this before any WPF/settings/cache static initialization can read user data.
        AppContext.SetData("MaliYaya.DataDirectory", DirectoryPath);
        // Product first-run defaults must never enable live HTTP in offline WPF tests.
        WidgetPreferences.Save(new(false, false));
    }
}
