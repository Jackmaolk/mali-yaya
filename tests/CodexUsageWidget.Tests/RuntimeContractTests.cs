using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class RuntimeContractTests
{
    [Fact]
    public void AssemblyRuntimeAndVersionMatchReleaseIdentity()
    {
        var assembly = typeof(App).Assembly;
        Assert.Equal(10, Environment.Version.Major);
        Assert.Equal(".NETCoreApp,Version=v10.0", assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
        Assert.Equal(Version.Parse(WidgetRuntimeStatus.BuildVersion), assembly.GetName().Version);
        Assert.Equal(WidgetRuntimeStatus.BuildVersion, assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version);
    }
}
