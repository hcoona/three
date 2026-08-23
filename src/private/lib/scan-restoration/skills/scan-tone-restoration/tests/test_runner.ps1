$ErrorActionPreference = "Stop"
$skillRoot = Split-Path -Parent $PSScriptRoot
$scriptsRoot = Join-Path $skillRoot "scripts"
$runner = Join-Path $scriptsRoot "run.ps1"
$document = Join-Path $skillRoot "SKILL.md"

$tokens = $null
$errors = $null
[Management.Automation.Language.Parser]::ParseFile(
    $runner, [ref]$tokens, [ref]$errors
) | Out-Null
if ($errors.Count -ne 0) {
    throw "run.ps1 does not parse: $($errors[0].Message)"
}

$source = Get-Content -LiteralPath $runner -Raw
$docs = Get-Content -LiteralPath $document -Raw
foreach ($required in @(
        '$pythonVersion = "3.12.10"',
        '$imagecodecsVersion = "2026.6.26"',
        '$tifffileVersion = "2026.7.31"',
        '"PIP_CONFIG_FILE" "nul"',
        "Lucia_PrivatePackages",
        "--require-hashes",
        "--only-binary=:all:",
        "--no-cache-dir",
        'PIP_INDEX_URL", $null',
        "providers = metadata.packages_distributions().get(package, ())",
        "distribution.casefold() not in",
        '".runtime-" + [Guid]::NewGuid()',
        "Remove-Item -LiteralPath `$runtime -Recurse -Force",
        "& `$python -I -B `$scriptPath @ScriptArgs"
    )) {
    if (-not $source.Contains($required)) {
        throw "run.ps1 is missing practical runner behavior: $required"
    }
}
if ($source -match "startup_launcher|azureauth-0\.9\.5\.manifest|Get-AuthenticodeSignature|COMSPEC|AppDomain|DirectoryLock") {
    throw "run.ps1 still contains removed adversarial launcher machinery."
}

$testRoot = Join-Path $PSScriptRoot (
    ".runner-test-" + [Guid]::NewGuid().ToString("N")
)
$fakeBin = Join-Path $testRoot "bin"
$capturePath = Join-Path $testRoot "pip-index-url.txt"
$fakeToken = "fake token+/@:?&=#%"
$savedPath = $env:PATH
$savedCapturePath = $env:SCAN_TONE_CAPTURE_PATH
$savedFakeToken = $env:SCAN_TONE_FAKE_TOKEN
try {
    New-Item -ItemType Directory -Path $fakeBin | Out-Null
    $fakeTool = Join-Path $fakeBin "FakeRunnerTool.exe"
    $fakeToolSource = @'
using System;
using System.IO;
using System.Reflection;

public static class FakeRunnerTool
{
    public static int Main(string[] args)
    {
        string name = Path.GetFileName(
            Assembly.GetExecutingAssembly().Location
        );
        if (name.Equals("AzureAuth.exe", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(Environment.GetEnvironmentVariable(
                "SCAN_TONE_FAKE_TOKEN"
            ));
            return 0;
        }
        if (name.Equals("mise.exe", StringComparison.OrdinalIgnoreCase))
        {
            int configIndex = Array.IndexOf(args, "-C");
            if (configIndex < 0 || configIndex + 1 >= args.Length)
            {
                return 2;
            }
            string scripts = Path.Combine(args[configIndex + 1], "Scripts");
            Directory.CreateDirectory(scripts);
            File.Copy(
                Assembly.GetExecutingAssembly().Location,
                Path.Combine(scripts, "python.exe"),
                true
            );
            return 0;
        }

        if (Array.IndexOf(args, "pip") >= 0)
        {
            File.WriteAllText(
                Environment.GetEnvironmentVariable(
                    "SCAN_TONE_CAPTURE_PATH"
                ),
                Environment.GetEnvironmentVariable("PIP_INDEX_URL")
            );
        }
        return 0;
    }
}
'@
    $fakeToolSourcePath = Join-Path $testRoot "FakeRunnerTool.cs"
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
        throw "Could not compile the fake practical runner tool."
    }
    Copy-Item -LiteralPath $fakeTool -Destination (
        Join-Path $fakeBin "mise.exe"
    )
    Copy-Item -LiteralPath $fakeTool -Destination (
        Join-Path $fakeBin "AzureAuth.exe"
    )

    $env:PATH = "$fakeBin;$savedPath"
    $env:SCAN_TONE_CAPTURE_PATH = $capturePath
    $env:SCAN_TONE_FAKE_TOKEN = $fakeToken
    $hostExecutable = (Get-Process -Id $PID).Path
    & $hostExecutable -NoProfile -File $runner "restore_tone.py" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "run.ps1 failed with the fake practical runner tools."
    }
    if (-not (Test-Path -LiteralPath $capturePath -PathType Leaf)) {
        throw "Fake pip did not capture PIP_INDEX_URL."
    }
    $capturedUrl = Get-Content -LiteralPath $capturePath -Raw
    $expectedUrl = (
        "https://azureauth:{0}@pkgs.dev.azure.com/msazure/One/" +
        "_packaging/Lucia_PrivatePackages/pypi/simple/"
    ) -f ([Uri]::EscapeDataString($fakeToken))
    if ($capturedUrl -ne $expectedUrl) {
        throw "PIP_INDEX_URL did not contain the escaped fake token."
    }
    if ($capturedUrl.Contains("{0}")) {
        throw "PIP_INDEX_URL retained the literal format placeholder."
    }
}
finally {
    $env:PATH = $savedPath
    $env:SCAN_TONE_CAPTURE_PATH = $savedCapturePath
    $env:SCAN_TONE_FAKE_TOKEN = $savedFakeToken
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

if (([Regex]::Matches(
            $docs,
            'powershell\.exe -NoProfile -File "\.\\scripts\\run\.ps1"'
        )).Count -ne 3) {
    throw "Every documented invocation must use run.ps1 with -NoProfile."
}
foreach ($removed in @(
        "invoke_trusted_launcher.ps1",
        "startup_launcher.c",
        "startup_launcher.exe",
        "startup_launcher.obj",
        "startup_launcher.pdb",
        "azureauth-0.9.5.manifest.json"
    )) {
    if (Test-Path -LiteralPath (Join-Path $scriptsRoot $removed)) {
        throw "Removed launcher artifact remains: $removed"
    }
}
