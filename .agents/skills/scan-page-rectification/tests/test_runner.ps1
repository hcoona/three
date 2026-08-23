$ErrorActionPreference = "Stop"
$skillRoot = Split-Path -Parent $PSScriptRoot
$scripts = Join-Path $skillRoot "scripts"
$runner = Join-Path $scripts "run.ps1"
$runnerText = Get-Content -LiteralPath $runner -Raw

$tokens = $null
$errors = $null
$runnerAst = [Management.Automation.Language.Parser]::ParseFile(
    $runner, [ref]$tokens, [ref]$errors
)
if ($errors.Count -ne 0) {
    throw "run.ps1 does not parse: $($errors[0].Message)"
}

foreach ($removed in @(
        "run.cmd",
        "startup_launcher.exe",
        "startup_launcher.c",
        "invoke_trusted_launcher.ps1",
        "install-dependencies.ps1",
        "environment.ps1",
        "python-runtime-manifest.json",
        "azureauth-0.9.5.manifest.json",
        "trusted-scripts.manifest.json",
        "update-python-runtime-pin.ps1"
    )) {
    if (Test-Path -LiteralPath (Join-Path $scripts $removed)) {
        throw "Removed hardening artifact still exists: $removed"
    }
}

foreach ($required in @(
        '$pythonVersion = "3.12.10"',
        '$miseVersion = "2026.8.8"',
        "MISE_INSTALLS_DIR",
        "--require-hashes --only-binary=:all: --no-deps",
        "ado token --output token",
        "-NoProfile -NonInteractive -EncodedCommand",
        'Remove-Item Env:PIP_INDEX_URL',
        "Remove-Item -LiteralPath `$sessionRoot -Recurse -Force"
    )) {
    if (-not $runnerText.Contains($required)) {
        throw "Runner is missing required behavior: $required"
    }
}

if ($runnerText -notmatch 'MISE_CONFIG_FILE\s*=\s*"NUL"') {
    throw "Runner is missing the isolated mise configuration root."
}
if ($runnerText -notmatch (
        'EnvironmentVariables\.Keys[\s\S]*?-like "PIP_\*"[\s\S]*?' +
        'EnvironmentVariables\.Remove'
    )) {
    throw "Runner does not remove every inherited PIP_* child variable."
}

if ($runnerText -match "AzureAuthSha256|runtimeManifest|FileShare|AccessControl") {
    throw "Runner still contains rolled-back hardening machinery."
}

if ($env:OS -eq "Windows_NT") {
    $installFunction = $runnerAst.Find(
        {
            param($node)
            (
                $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -eq "Install-PinnedDependencySet"
            )
        },
        $true
    )
    if ($null -eq $installFunction) {
        throw "Could not locate Install-PinnedDependencySet."
    }
    $installFunctionScript = [ScriptBlock]::Create(
        $installFunction.Extent.Text
    )
    . $installFunctionScript

    $testRoot = Join-Path $PSScriptRoot (
        ".runner-test-" + [Guid]::NewGuid().ToString("N")
    )
    $capturePath = Join-Path $testRoot "pip-environment.txt"
    $fakeToken = "fake token+/@:?&=#%"
    $seededEnvironment = @{
        PIP_TARGET                    = "target-canary"
        PIP_PREFIX                    = "prefix-canary"
        PIP_ROOT                      = "root-canary"
        PIP_INDEX_URL                 = "https://index.invalid/simple/"
        PIP_EXTRA_INDEX_URL           = "https://extra.invalid/simple/"
        PIP_CONFIG_FILE               = "config-canary"
        PIP_DISABLE_PIP_VERSION_CHECK = "0"
        PIP_NO_INPUT                  = "0"
        SCAN_RECTIFY_CAPTURE_PATH     = $capturePath
        SCAN_RECTIFY_FAKE_TOKEN       = $fakeToken
    }
    $savedTestEnvironment = @{}
    try {
        New-Item -ItemType Directory -Path $testRoot | Out-Null
        $fakeToolSource = @'
using System;
using System.IO;

public static class FakePipTool
{
    private static string Capture(string name)
    {
        string value = Environment.GetEnvironmentVariable(name);
        return name + "=" + (value ?? "<null>");
    }

    public static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--version") >= 0)
        {
            Console.WriteLine("0.9.5.0");
            return 0;
        }
        if (Array.IndexOf(args, "ado") >= 0)
        {
            Console.WriteLine(Environment.GetEnvironmentVariable(
                "SCAN_RECTIFY_FAKE_TOKEN"
            ));
            return 0;
        }
        if (Array.IndexOf(args, "pip") >= 0)
        {
            File.WriteAllLines(
                Environment.GetEnvironmentVariable(
                    "SCAN_RECTIFY_CAPTURE_PATH"
                ),
                new[]
                {
                    Capture("PIP_TARGET"),
                    Capture("PIP_PREFIX"),
                    Capture("PIP_ROOT"),
                    Capture("PIP_EXTRA_INDEX_URL"),
                    Capture("PIP_CONFIG_FILE"),
                    Capture("PIP_DISABLE_PIP_VERSION_CHECK"),
                    Capture("PIP_NO_INPUT"),
                    Capture("PIP_INDEX_URL")
                }
            );
        }
        return 0;
    }
}
'@
        $fakeToolSourcePath = Join-Path $testRoot "FakePipTool.cs"
        $fakeTool = Join-Path $testRoot "FakePipTool.exe"
        $compilerScript = Join-Path $testRoot "compile-fake-tool.ps1"
        Set-Content -LiteralPath $fakeToolSourcePath -Value $fakeToolSource `
            -Encoding UTF8
        @'
param([string]$SourcePath, [string]$OutputPath)
$source = Get-Content -LiteralPath $SourcePath -Raw
Add-Type -TypeDefinition $source -Language CSharp `
    -OutputAssembly $OutputPath -OutputType ConsoleApplication
'@ | Set-Content -LiteralPath $compilerScript -Encoding UTF8
        & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
            -NoProfile -File $compilerScript $fakeToolSourcePath $fakeTool
        if ($LASTEXITCODE -ne 0) {
            throw "Could not compile the fake dependency tools."
        }
        $fakePython = Join-Path $testRoot "python.exe"
        $fakeAzureAuth = Join-Path $testRoot "azureauth.exe"
        Copy-Item -LiteralPath $fakeTool -Destination $fakePython
        Copy-Item -LiteralPath $fakeTool -Destination $fakeAzureAuth

        foreach ($entry in $seededEnvironment.GetEnumerator()) {
            $savedTestEnvironment[$entry.Key] = (
                [Environment]::GetEnvironmentVariable($entry.Key, "Process")
            )
            [Environment]::SetEnvironmentVariable(
                $entry.Key, $entry.Value, "Process"
            )
        }
        Install-PinnedDependencySet `
            -Python $fakePython `
            -Requirements (Join-Path $scripts "requirements.lock") `
            -AzureAuth $fakeAzureAuth

        if (-not (Test-Path -LiteralPath $capturePath -PathType Leaf)) {
            throw "Fake pip did not capture its environment."
        }
        $capturedEnvironment = @{}
        foreach ($line in Get-Content -LiteralPath $capturePath) {
            $name, $value = $line -split "=", 2
            $capturedEnvironment[$name] = $value
        }
        $expectedIndexUrl = (
            "https://azureauth:{0}@pkgs.dev.azure.com/msazure/One/" +
            "_packaging/Lucia_PrivatePackages/pypi/simple/"
        ) -f ([Uri]::EscapeDataString($fakeToken))
        $expectedEnvironment = @{
            PIP_TARGET                    = "<null>"
            PIP_PREFIX                    = "<null>"
            PIP_ROOT                      = "<null>"
            PIP_EXTRA_INDEX_URL           = "<null>"
            PIP_CONFIG_FILE               = "NUL"
            PIP_DISABLE_PIP_VERSION_CHECK = "1"
            PIP_NO_INPUT                  = "1"
            PIP_INDEX_URL                 = $expectedIndexUrl
        }
        foreach ($entry in $expectedEnvironment.GetEnumerator()) {
            if ($capturedEnvironment[$entry.Key] -cne $entry.Value) {
                throw (
                    "Fake pip received unexpected {0}: {1}" -f
                    $entry.Key, $capturedEnvironment[$entry.Key]
                )
            }
        }
    }
    finally {
        foreach ($entry in $savedTestEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable(
                $entry.Key, $entry.Value, "Process"
            )
        }
        if (Test-Path -LiteralPath $testRoot) {
            Remove-Item -LiteralPath $testRoot -Recurse -Force
        }
    }
}

$lock = Get-Content -LiteralPath (Join-Path $scripts "requirements.lock") -Raw
if (($lock | Select-String -Pattern "--hash=sha256:" -AllMatches).
    Matches.Count -ne 3) {
    throw "Every pinned dependency must have exactly one SHA-256 hash."
}
