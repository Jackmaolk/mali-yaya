$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$binRoot = [IO.Path]::GetFullPath((Join-Path $root 'bin'))
$testRoot = Join-Path $binRoot ('launcher-tests-' + [Guid]::NewGuid().ToString('N'))
$script:scenarios = 0
$utf8 = New-Object Text.UTF8Encoding($false)

function Assert-Equal($Expected, $Actual, [string]$Name) {
    if ($Expected -cne $Actual) {
        throw "$Name expected '$Expected', got '$Actual'."
    }
}

function Quote-Argument([string]$Value) {
    return '"' + (($Value -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
}

function New-FixtureDirectory([string]$RelativePath) {
    $directory = Join-Path $testRoot $RelativePath
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    return $directory
}

function Copy-FixtureHost([string]$Directory) {
    Copy-Item -LiteralPath $hostTemplate -Destination (Join-Path $Directory 'dotnet.exe')
}

function Invoke-Diagnose {
    param(
        [string]$Launcher = $portableLauncher,
        [string]$Documents = $documents,
        [string]$DotnetRoot = '',
        [string]$ProgramFiles = '',
        [string]$SearchPath = '',
        [int]$ExpectedExit = 0,
        [switch]$UseEnvironment,
        [switch]$OutputFixture,
        [string[]]$ExtraArguments = @()
    )
    $arguments = @('--diagnose', "--documents-root=$Documents", "--program-files=$ProgramFiles")
    if (-not $UseEnvironment) {
        $arguments += @("--dotnet-root=$DotnetRoot", "--path=$SearchPath")
    }
    $arguments += $ExtraArguments
    # Only the separately compiled fixture entry point exercises StartWidget.
    if ($OutputFixture) { $arguments = @() }
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $Launcher
    $info.Arguments = (($arguments | ForEach-Object { Quote-Argument $_ }) -join ' ')
    $info.WorkingDirectory = $workingDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = $utf8
    $info.StandardErrorEncoding = $utf8
    # Framework 4 can reject inherited environment blocks containing both PATH and Path.
    try { $environment = $info.get_EnvironmentVariables() }
    catch {
        if ($_.Exception.InnerException -isnot [ArgumentException]) { throw }
        $environment = $info.get_EnvironmentVariables()
    }
    # Rebuild only the child block; no credentials or real account paths are inherited.
    $environment.Clear()
    $environment.Add('SystemRoot', $env:SystemRoot)
    $environment.Add('WINDIR', $env:WINDIR)
    $environment.Add('TEMP', $testRoot)
    $environment.Add('TMP', $testRoot)
    $environment.Add('DOTNET_ROOT', $DotnetRoot)
    $environment.Add('ProgramFiles', $ProgramFiles)
    $environment.Add('PATH', $SearchPath)
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    try {
        if (-not $process.Start()) { throw 'Diagnostic launcher did not start.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(10000)) {
            $process.Kill()
            $process.WaitForExit()
            throw 'Diagnostic mode timed out; it must never show UI or launch a widget.'
        }
        if ($OutputFixture -and $stderr.Result.Length -ne 0) {
            throw 'Synthetic stderr escaped launcher redirection.'
        }
        Assert-Equal '' $stderr.Result 'Diagnostic stderr'
        Assert-Equal $ExpectedExit $process.ExitCode 'Diagnostic exit code'
        $values = @{}
        foreach ($line in ($stdout.Result -split '\r?\n')) {
            if (-not $line) { continue }
            if ($line -notmatch '^(AdjacentDll|LegacyDll|WidgetDll|WidgetSource|DotnetHost|DotnetSource|Error)=(.*)$') {
                throw 'Unexpected diagnostic output; only path resolution fields are allowed.'
            }
            if ($values.ContainsKey($Matches[1])) { throw 'Duplicate diagnostic field.' }
            $values[$Matches[1]] = $Matches[2]
        }
        Assert-Equal $(if ($OutputFixture) { 0 } elseif ($ExpectedExit -eq 64) { 1 } else { 6 }) $values.Count 'Diagnostic field count'
        $script:scenarios++
        return $values
    }
    finally { $process.Dispose() }
}

New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {
        $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    }
    if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {
        throw 'The local .NET Framework 4 C# compiler is required; nothing will be downloaded.'
    }

    $unicodeName = ([string][char]0x4e2d) + [char]0x6587
    $portableDirectory = New-FixtureDirectory ('portable space ' + $unicodeName)
    $portableLauncher = Join-Path $portableDirectory 'CodexUsageWidget.exe'
    $launcherSource = Join-Path $root 'launcher\WidgetLauncher.cs'
    $icon = Join-Path $root 'Assets\mali-yaya.ico'
    & $csc /nologo /codepage:65001 /langversion:4 /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /win32icon:$icon /out:$portableLauncher $launcherSource
    if ($LASTEXITCODE -ne 0) { throw "Launcher compilation failed: $LASTEXITCODE" }

    $reader = New-Object IO.BinaryReader([IO.File]::OpenRead($portableLauncher))
    try {
        $reader.BaseStream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        $reader.BaseStream.Position = $peOffset + 92
        Assert-Equal 2 ([int]$reader.ReadUInt16()) 'Windows GUI PE subsystem (no console)'
    }
    finally { $reader.Dispose() }

    # This harmless executable records any accidental host invocation, then exits.
    $hostSource = Join-Path $testRoot 'FixtureHost.cs'
    [IO.File]::WriteAllText($hostSource, @'
internal static class FixtureHost
{
    private static int Main()
    {
        System.IO.File.WriteAllText(System.IO.Path.Combine(
            System.AppDomain.CurrentDomain.BaseDirectory, "host-executed.txt"), "unexpected");
        return 99;
    }
}
'@, $utf8)
    $hostTemplate = Join-Path $testRoot 'FixtureHost.exe'
    & $csc /nologo /langversion:4 /target:exe /platform:x64 /out:$hostTemplate $hostSource
    if ($LASTEXITCODE -ne 0) { throw "Fixture compilation failed: $LASTEXITCODE" }

    $documents = New-FixtureDirectory ('fixture documents ' + $unicodeName)
    $legacyDirectory = Join-Path $documents 'Codex\tools\codex-usage-widget\publish'
    New-Item -ItemType Directory -Path $legacyDirectory | Out-Null
    $legacyDll = Join-Path $legacyDirectory 'CodexUsageWidget.dll'
    $adjacentDll = Join-Path $portableDirectory 'CodexUsageWidget.dll'
    [IO.File]::WriteAllText($legacyDll, 'not a real widget', $utf8)
    [IO.File]::WriteAllText($adjacentDll, 'not a real widget', $utf8)
    $emptyDocuments = New-FixtureDirectory 'empty documents'
    $copiedDirectory = New-FixtureDirectory 'desktop copy fixture'
    $copiedLauncher = Join-Path $copiedDirectory 'CodexUsageWidget.exe'
    Copy-Item -LiteralPath $portableLauncher -Destination $copiedLauncher
    $workingDirectory = New-FixtureDirectory 'unrelated working directory'
    [IO.File]::WriteAllText((Join-Path $workingDirectory 'CodexUsageWidget.dll'), 'cwd decoy', $utf8)
    Copy-FixtureHost $workingDirectory
    $relativeHost = New-FixtureDirectory 'unrelated working directory\relative host'
    Copy-FixtureHost $relativeHost
    $rootHost = New-FixtureDirectory ('dotnet root ' + $unicodeName)
    Copy-FixtureHost $rootHost
    $programFiles = New-FixtureDirectory 'Program Files fixture'
    $programHost = New-FixtureDirectory 'Program Files fixture\dotnet'
    Copy-FixtureHost $programHost
    $pathHost = New-FixtureDirectory ('PATH space ' + $unicodeName)
    Copy-FixtureHost $pathHost
    $scriptsOnly = New-FixtureDirectory 'scripts and text impostor'
    foreach ($name in @('dotnet.cmd', 'dotnet.bat', 'dotnet.ps1', 'dotnet.com', 'dotnet.exe')) {
        [IO.File]::WriteAllText((Join-Path $scriptsOnly $name), 'not an executable', $utf8)
    }

    $result = Invoke-Diagnose -DotnetRoot $rootHost -ProgramFiles $programFiles -SearchPath $pathHost
    Assert-Equal $adjacentDll $result.WidgetDll 'Adjacent DLL wins over legacy and cwd'
    Assert-Equal $adjacentDll $result.AdjacentDll 'EXE-relative DLL path with spaces and Unicode'
    Assert-Equal $legacyDll $result.LegacyDll 'Isolated legacy lookup path'
    Assert-Equal 'Adjacent' $result.WidgetSource 'Adjacent source'
    Assert-Equal (Join-Path $rootHost 'dotnet.exe') $result.DotnetHost 'DOTNET_ROOT precedence'
    Assert-Equal 'DOTNET_ROOT' $result.DotnetSource 'Root host source'

    $result = Invoke-Diagnose -Launcher $copiedLauncher -DotnetRoot $rootHost
    Assert-Equal $legacyDll $result.WidgetDll 'Legacy desktop-copy fallback'
    Assert-Equal 'Legacy' $result.WidgetSource 'Legacy source'

    $result = Invoke-Diagnose -Launcher $copiedLauncher -Documents $emptyDocuments -DotnetRoot $rootHost -ExpectedExit 2
    Assert-Equal '' $result.WidgetDll 'Missing DLL must not use working directory'
    Assert-Equal 'Missing' $result.WidgetSource 'Missing DLL source'

    $result = Invoke-Diagnose -DotnetRoot $rootHost -SearchPath $pathHost -UseEnvironment
    Assert-Equal (Join-Path $rootHost 'dotnet.exe') $result.DotnetHost 'Child DOTNET_ROOT environment'

    $result = Invoke-Diagnose -ProgramFiles $programFiles -SearchPath $pathHost
    Assert-Equal (Join-Path $programHost 'dotnet.exe') $result.DotnetHost 'ProgramFiles precedence over PATH'
    Assert-Equal 'ProgramFiles' $result.DotnetSource 'ProgramFiles source'

    $result = Invoke-Diagnose -DotnetRoot $scriptsOnly -ProgramFiles $programFiles
    Assert-Equal (Join-Path $programHost 'dotnet.exe') $result.DotnetHost 'Text EXE rejected before ProgramFiles fallback'

    $result = Invoke-Diagnose -DotnetRoot $workingDirectory -ProgramFiles $programFiles
    Assert-Equal (Join-Path $programHost 'dotnet.exe') $result.DotnetHost 'Explicit current-directory host rejected'

    $result = Invoke-Diagnose -DotnetRoot (Join-Path $testRoot 'missing root') -SearchPath $pathHost
    Assert-Equal (Join-Path $pathHost 'dotnet.exe') $result.DotnetHost 'PATH fallback'
    Assert-Equal 'PATH' $result.DotnetSource 'PATH source'

    $result = Invoke-Diagnose -SearchPath $pathHost -UseEnvironment
    Assert-Equal (Join-Path $pathHost 'dotnet.exe') $result.DotnetHost 'Child PATH environment'

    $unsafePath = ';.;relative host;' + $workingDirectory + ';' + $scriptsOnly + ';' + $pathHost
    $result = Invoke-Diagnose -DotnetRoot '.' -ProgramFiles 'relative host' -SearchPath $unsafePath
    Assert-Equal (Join-Path $pathHost 'dotnet.exe') $result.DotnetHost 'Unsafe PATH entries skipped'

    $result = Invoke-Diagnose -SearchPath ('"' + $pathHost + '\"')
    Assert-Equal (Join-Path $pathHost 'dotnet.exe') $result.DotnetHost 'Quoted PATH directory with trailing slash'

    $result = Invoke-Diagnose -DotnetRoot ('"' + $rootHost + '\"')
    Assert-Equal (Join-Path $rootHost 'dotnet.exe') $result.DotnetHost 'Quoted root directory with trailing slash'

    $drive = [IO.Path]::GetPathRoot($testRoot).Substring(0, 2)
    $result = Invoke-Diagnose -DotnetRoot ($drive + 'relative host') -ProgramFiles '\relative host' -SearchPath $pathHost
    Assert-Equal (Join-Path $pathHost 'dotnet.exe') $result.DotnetHost 'Drive-relative and root-relative directories rejected'

    $result = Invoke-Diagnose -DotnetRoot (Join-Path $testRoot 'invalid|root') -SearchPath $pathHost
    Assert-Equal (Join-Path $pathHost 'dotnet.exe') $result.DotnetHost 'Malformed root falls through'

    $result = Invoke-Diagnose -ExpectedExit 3
    Assert-Equal '' $result.DotnetHost 'No implicit installed-host or cwd fallback'
    Assert-Equal 'Missing' $result.DotnetSource 'Missing host source'

    $result = Invoke-Diagnose -DotnetRoot $scriptsOnly -SearchPath ($workingDirectory + ';.;relative host;' + $scriptsOnly) -ExpectedExit 3
    Assert-Equal '' $result.DotnetHost 'Scripts, text EXE and cwd cannot satisfy host lookup'

    $result = Invoke-Diagnose -ExtraArguments '--unknown=fixture' -ExpectedExit 64
    Assert-Equal 'InvalidArguments' $result.Error 'Invalid diagnostic arguments do not show UI'

    $networkRoots = @(
        '\\launcher-fixture.invalid', '//launcher-fixture.invalid',
        '\\launcher-fixture.invalid\share', '//launcher-fixture.invalid/share',
        '\\?\UNC\launcher-fixture.invalid\share', '\\?\C:\fixture'
    )
    foreach ($networkRoot in $networkRoots) {
        $result = Invoke-Diagnose -Documents $networkRoot -DotnetRoot $networkRoot -ProgramFiles $networkRoot -SearchPath ($networkRoot + ';' + $pathHost)
        Assert-Equal '' $result.LegacyDll 'Network/device legacy root rejected before file lookup'
        Assert-Equal (Join-Path $pathHost 'dotnet.exe') $result.DotnetHost 'Network/device host roots skipped for local PATH fallback'
    }
    $result = Invoke-Diagnose -DotnetRoot $networkRoots[0] -ProgramFiles $networkRoots[1] -SearchPath ($networkRoots -join ';') -ExpectedExit 3
    Assert-Equal '' $result.DotnetHost 'Network-only roots cannot satisfy host lookup'

    # Compile-time double: simulate mapped drives without mapping or accessing a share.
    $driveFixtureSource = Join-Path $testRoot 'NetworkDriveFixture.cs'
    [IO.File]::WriteAllText($driveFixtureSource, @'
internal sealed class DriveInfo
{
    internal DriveInfo(string root) { }
    internal System.IO.DriveType DriveType
    {
        get { return System.IO.DriveType.Network; }
    }
}
'@, $utf8)
    $mappedLauncher = Join-Path $portableDirectory 'MappedDriveLauncher.exe'
    & $csc /nologo /codepage:65001 /langversion:4 /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /out:$mappedLauncher $launcherSource $driveFixtureSource
    if ($LASTEXITCODE -ne 0) { throw "Mapped-drive fixture compilation failed: $LASTEXITCODE" }
    $result = Invoke-Diagnose -Launcher $mappedLauncher -DotnetRoot $rootHost -ProgramFiles $programFiles -SearchPath $pathHost -ExpectedExit 3
    Assert-Equal '' $result.DotnetHost 'Mapped-network drives rejected in every host source'
    Assert-Equal '' $result.LegacyDll 'Mapped-network legacy root rejected before file lookup'

    # Alternate test entry point invokes private StartWidget with this harmless EXE.
    # Both streams exceed pipe capacity before and after the startup grace period.
    $outputFixtureSource = Join-Path $testRoot 'LauncherOutputFixture.cs'
    [IO.File]::WriteAllText($outputFixtureSource, @'
using System;
using System.IO;
using System.Reflection;
using System.Threading;

internal static class LauncherOutputFixture
{
    private static void WriteNoise(TextWriter writer, char value)
    {
        var line = new string(value, 8192);
        for (var i = 0; i < 128; i++) writer.WriteLine(line);
        writer.Flush();
    }

    private static void EmitNoise()
    {
        var stdout = new Thread(delegate() { WriteNoise(Console.Out, 'O'); });
        var stderr = new Thread(delegate() { WriteNoise(Console.Error, 'E'); });
        stdout.Start();
        stderr.Start();
        stdout.Join();
        stderr.Join();
    }

    private static int Main(string[] args)
    {
        if (args.Length == 1)
        {
            EmitNoise();
            Thread.Sleep(2000);
            EmitNoise();
            File.WriteAllText(args[0], "complete");
            return 0;
        }
        var launcher = typeof(WidgetLauncher);
        var resolutionType = launcher.GetNestedType("Resolution", BindingFlags.NonPublic);
        var resolution = Activator.CreateInstance(resolutionType, true);
        resolutionType.GetField("DotnetHost", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(resolution, Assembly.GetExecutingAssembly().Location);
        resolutionType.GetField("WidgetDll", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(resolution, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output-fixture-completed.txt"));
        return (int)launcher.GetMethod("StartWidget", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { resolution });
    }
}
'@, $utf8)
    $outputFixture = Join-Path $testRoot 'LauncherOutputFixture.exe'
    & $csc /nologo /codepage:65001 /langversion:4 /target:exe /platform:x64 /main:LauncherOutputFixture /reference:System.Windows.Forms.dll /out:$outputFixture $launcherSource $outputFixtureSource
    if ($LASTEXITCODE -ne 0) { throw "Output fixture compilation failed: $LASTEXITCODE" }
    Invoke-Diagnose -Launcher $outputFixture -OutputFixture | Out-Null
    Assert-Equal 'complete' ([IO.File]::ReadAllText((Join-Path $testRoot 'output-fixture-completed.txt'))) 'Both streams drained through child exit'

    $executed = @(Get-ChildItem -LiteralPath $testRoot -Filter 'host-executed.txt' -Recurse -File)
    Assert-Equal 0 $executed.Count 'No host or widget was executed'
    Write-Host "PASS: $script:scenarios isolated launcher scenarios; Framework 4 / C# 4; WinExe; network-drive rejection; async output fixture; no real widget executed."
}
finally {
    # Delete only this run's generated fixture directory, never the project's bin root.
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $allowedPrefix = $binRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTestRoot.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedTestRoot) -notlike 'launcher-tests-*') {
        throw 'Refusing to remove a fixture outside the project bin directory.'
    }
    if (Test-Path -LiteralPath $resolvedTestRoot) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
