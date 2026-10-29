using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace CodexUsageWidget.Tests;

// Source-only regression guards: no process launches, settings, auth or network.
public sealed class CiContractTests
{
    private const string Checkout = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1";
    private const string SetupDotnet = "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68";

    [Fact]
    public void PushAndPullRequestsUseReadOnlyWindowsNet10Gates()
    {
        var source = Read(".github/workflows/verify.yml");
        foreach (var trigger in new[] { "push", "pull_request", "workflow_dispatch", "workflow_call" })
            Assert.Matches($@"(?m)^  {trigger}:\s*$", source);
        Assert.Matches(@"(?m)^permissions:\r?\n  contents: read\s*$", source);
        Assert.Contains("runs-on: windows-2025", source);
        Assert.Contains("dotnet-version: '10.0.x'", source);
        Assert.DoesNotContain("contents: write", source);
        Assert.DoesNotContain("--filter", source);
        foreach (var gate in new[] { "Test-SourcePackaging.ps1", "Test-Launcher.ps1", "Test-RuntimeContract.ps1",
                     "dotnet restore", "dotnet test", "dotnet publish", "Test-Diagnostics.ps1", "Test-Icon.ps1", "Test-ReleasePackage.ps1" })
            Assert.Contains(gate, source);
        Assert.Contains("-p:ContinuousIntegrationBuild=true", source);
        Assert.Contains("-p:NuGetAudit=true -p:NuGetAuditMode=all", source);
        Assert.Contains("--self-contained false", source);
        Assert.Contains("-p:UseAppHost=false", source);
    }

    [Theory]
    [InlineData(".github/workflows/verify.yml")]
    [InlineData(".github/workflows/release.yml")]
    public void ActionsArePinnedAndCheckoutDoesNotPersistCredentials(string path)
    {
        var source = Read(path);
        var external = Regex.Matches(source, @"(?m)^\s*-?\s*uses:\s*([^\s#]+)")
            .Select(match => match.Groups[1].Value).Where(value => !value.StartsWith("./", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(external);
        foreach (var action in external)
        {
            Assert.Matches(@"\Aactions/[a-z-]+@[0-9a-f]{40}\z", action);
            Assert.Contains(action, new[] { Checkout, SetupDotnet });
        }
        var checkouts = Regex.Matches(source, Regex.Escape(Checkout)).Count;
        Assert.Equal(checkouts, Regex.Matches(source, @"persist-credentials:\s*false").Count);
        foreach (var forbidden in new[] { "pull_request_target", "secrets.", "secrets: inherit", "write-all",
                     "id-token: write", "NuGetAudit=false", "continue-on-error: true", "git push", "gh pr merge" })
            Assert.DoesNotContain(forbidden, source);
    }

    [Fact]
    public void AuditFailuresRemainErrorsInCi()
    {
        var props = XDocument.Parse(Read("Directory.Build.props"));
        Assert.Contains(props.Descendants("NuGetAudit"), element => element.Value == "true");
        Assert.Contains(props.Descendants("NuGetAuditMode"), element => element.Value == "all");
        var group = Assert.Single(props.Descendants("PropertyGroup"), element =>
            ((string?)element.Attribute("Condition"))?.Contains("ContinuousIntegrationBuild", StringComparison.Ordinal) == true);
        var warnings = Assert.Single(group.Elements("WarningsAsErrors")).Value.Split(';');
        foreach (var code in Enumerable.Range(1900, 6)) Assert.Contains("NU" + code, warnings);
        Assert.Empty(props.Descendants("NuGetAuditSuppress"));
    }

    [Theory]
    [InlineData("CodexUsageWidget.csproj")]
    [InlineData("tests/CodexUsageWidget.Tests/CodexUsageWidget.Tests.csproj")]
    public void BothProjectsRequireNet10Windows(string path)
    {
        var project = XDocument.Parse(Read(path));
        Assert.Equal("net10.0-windows", Assert.Single(project.Descendants("TargetFramework")).Value);
    }

    [Fact]
    public void DiagnosticSourceExitsBeforeCliOrRuntimeStateAccess()
    {
        // Source ordering is not a runtime observation of Window.Show().
        var app = Read("App.xaml.cs");
        var start = app.IndexOf("if (e.Args is [\"--diagnose-build\"])", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = app.IndexOf("return;", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var branch = app[start..end];
        Assert.Contains("product = \"mali-yaya\"", branch);
        Assert.Contains("WidgetRuntimeStatus.BuildVersion", branch);
        Assert.Contains("runtimeMajor = Environment.Version.Major", branch);
        Assert.Contains("Shutdown(0)", branch);
        foreach (var forbidden in new[] { "LoadPath", "WidgetDataPaths", "HttpClient", "MainWindow", "ResolveAsync", "StartAsync" })
            Assert.DoesNotContain(forbidden, branch);
        foreach (var later in new[] { "--diagnose-cli", "RestartAfterInterruptedUpdate()", "new MainWindow()" })
            Assert.True(app.IndexOf(later, StringComparison.Ordinal) > end);
        var script = Read("scripts/Test-Diagnostics.ps1");
        Assert.Contains("--diagnose-build", script);
        Assert.DoesNotContain("--diagnose-cli", script);
        Assert.Contains("$environment.Clear()", script);
        Assert.DoesNotContain("$process.MainWindowHandle", script);
        Assert.Contains("$process.WaitForExit(20000)", script);
        Assert.Contains("$process.ExitCode -ne 0", script);
        Assert.Contains("[string]::IsNullOrWhiteSpace($errorOutput)", script);
        Assert.Contains("$report.product -cne 'mali-yaya'", script);
        Assert.Contains("$report.runtimeMajor -ne 10", script);
        Assert.Contains("$report.version -cne $version", script);
        Assert.Contains("$report.platform -cne 'win-x64'", script);
        Assert.Contains("$fields.Count -ne $allowed.Count", script);
        Assert.Contains("Get-ChildItem -LiteralPath $fixture -Force -File", script);
        Assert.Contains("Get-ChildItem -LiteralPath $fixture -Force -Directory", script);
        Assert.Contains("Get-FileHash", script);
        Assert.Contains("NOT VERIFIED: real GUI visibility/Show and outbound network behavior", script);
    }

    [Fact]
    public void ReleaseOnlyCreatesAnApprovedMainDraftWithExactIdentity()
    {
        var source = Read(".github/workflows/release.yml");
        Assert.Matches(@"(?m)^  workflow_dispatch:\s*$", source);
        foreach (var forbidden in new[] { "push", "pull_request", "release", "workflow_run", "schedule" })
            Assert.DoesNotMatch($@"(?m)^  {forbidden}:\s*$", source);
        Assert.Equal(2, Regex.Matches(source, "github.ref == 'refs/heads/main' && inputs.real_acceptance_completed == true").Count);
        Assert.Contains("needs: verify", source);
        Assert.Contains("uses: ./.github/workflows/verify.yml", source);
        Assert.Contains("environment: release-candidate", source);
        Assert.Contains("required_reviewers", source);
        Assert.Contains("prevent_self_review", source);
        Assert.Contains("$main -cne $env:GITHUB_SHA", source);
        Assert.Contains("Version must exactly match merged BuildVersion", source);
        Assert.Contains("Exact main SHA requires a successful push verification run", source);
        Assert.Contains("Tag already exists", source);
        Assert.Contains("SHA256SUMS.txt", source);
        Assert.Contains("--target $env:GITHUB_SHA", source);
        Assert.Contains("--draft --prerelease --latest=false", source);
        Assert.Contains("GH_TOKEN: ${{ github.token }}", source);
        Assert.Single(Regex.Matches(source, "contents: write"));
        Assert.DoesNotContain("gh release edit", source);
        Assert.DoesNotContain("--clobber", source);
        Assert.DoesNotContain("--draft=false", source);
    }

    private static string Read(string relative)
    {
        // Walk only repository fixture ancestors, never user profiles or settings.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodexUsageWidget.csproj")) &&
                File.Exists(Path.Combine(directory.FullName, ".github", "workflows", "verify.yml")))
                return File.ReadAllText(Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
        }
        throw new DirectoryNotFoundException("Repository source fixture not found above test output.");
    }
}
