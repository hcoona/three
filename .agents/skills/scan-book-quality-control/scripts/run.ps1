param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Script,
    [ValidateRange(1, 86400)]
    [int]$MiseTimeoutSeconds = 300,
    [ValidateRange(1, 86400)]
    [int]$AzureAuthTimeoutSeconds = 120,
    [ValidateRange(1, 86400)]
    [int]$PipTimeoutSeconds = 600,
    [ValidateRange(1, 86400)]
    [int]$ValidatorTimeoutSeconds = 1800,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ScriptArgs
)

$ErrorActionPreference = "Stop"
$skillBase = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $skillBase (".runtime-" + [Guid]::NewGuid().ToString("N"))
$requiredPython = "3.12.11"
$exitCode = 2

function ConvertTo-WindowsCommandLineArgument {
    param([AllowEmptyString()][Parameter(Mandatory = $true)][string]$Argument)
    if ($Argument.Length -eq 0) { return '""' }
    if ($Argument -notmatch '[\s"]') { return $Argument }
    $builder = [Text.StringBuilder]::new()
    [void]$builder.Append('"')
    $backslashes = 0
    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq '\') {
            $backslashes++
            continue
        }
        if ($character -eq '"') {
            [void]$builder.Append('\' * ($backslashes * 2 + 1))
            [void]$builder.Append('"')
        }
        else {
            [void]$builder.Append('\' * $backslashes)
            [void]$builder.Append($character)
        }
        $backslashes = 0
    }
    [void]$builder.Append('\' * ($backslashes * 2))
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-ProcessWithTimeout {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [AllowEmptyString()][Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [Parameter(Mandatory = $true)][string]$Description,
        [hashtable]$EnvironmentVariables = @{}
    )
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = (
        $ArgumentList | ForEach-Object {
            ConvertTo-WindowsCommandLineArgument -Argument $_
        }
    ) -join " "
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($entry in $EnvironmentVariables.GetEnumerator()) {
        $startInfo.EnvironmentVariables[[string]$entry.Key] = [string]$entry.Value
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "$Description could not be started."
        }
        foreach ($entry in $EnvironmentVariables.GetEnumerator()) {
            $startInfo.EnvironmentVariables.Remove([string]$entry.Key)
        }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "$Description timed out after $TimeoutSeconds seconds."
        }
        $process.WaitForExit()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut   = $stdout.GetAwaiter().GetResult()
            StdErr   = $stderr.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Reset-PipEnvironment {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute(
        "PSUseShouldProcessForStateChangingFunctions",
        "",
        Justification = "The function resets only this runner process."
    )]
    param()

    Get-ChildItem Env: | Where-Object {
        $_.Name -imatch '^PIP_' -or
        $_.Name -imatch '^(HTTP_PROXY|HTTPS_PROXY|ALL_PROXY|NO_PROXY|REQUESTS_CA_BUNDLE|CURL_CA_BUNDLE|SSL_CERT_FILE|SSL_CERT_DIR)$'
    } | ForEach-Object {
        Remove-Item -LiteralPath ("Env:" + $_.Name) -ErrorAction SilentlyContinue
    }
    $env:PIP_CONFIG_FILE = "nul"
    $env:PIP_DISABLE_PIP_VERSION_CHECK = "1"
    $env:PIP_NO_INPUT = "1"
    $env:PIP_NO_CACHE_DIR = "1"
}

if ((Get-Location).Path.TrimEnd('\') -ne $skillBase.TrimEnd('\')) {
    throw "Run this command from the skill base directory: $skillBase"
}
if ($Script -cne "validate_book.py") {
    throw "Script must be exactly validate_book.py."
}

$validator = Join-Path $PSScriptRoot "validate_book.py"
$requirements = Join-Path $PSScriptRoot "requirements.lock"
if (-not (Test-Path -LiteralPath $validator -PathType Leaf) -or
    -not (Test-Path -LiteralPath $requirements -PathType Leaf)) {
    throw "The validator or dependency lock is missing."
}

$miseCommand = Get-Command mise -CommandType Application -ErrorAction Stop |
    Select-Object -First 1
$mise = $miseCommand.Source
$env:MISE_CONFIG_FILE = "NUL"
$env:MISE_GLOBAL_CONFIG_FILE = "NUL"
$env:MISE_SYSTEM_CONFIG_FILE = "NUL"
$env:MISE_IGNORED_CONFIG_PATHS = $skillBase
Reset-PipEnvironment

try {
    $miseResult = Invoke-ProcessWithTimeout -FilePath $mise `
        -ArgumentList @(
        "exec", "python@$requiredPython", "--",
        "python", "-I", "-m", "venv", $runtime
    ) -TimeoutSeconds $MiseTimeoutSeconds `
        -Description "mise runtime creation"
    if ($miseResult.ExitCode -ne 0) {
        throw "mise could not create the fresh ephemeral Python runtime."
    }

    $python = Join-Path $runtime "Scripts\python.exe"
    $versionResult = Invoke-ProcessWithTimeout -FilePath $python `
        -ArgumentList @(
        "-I", "-B", "-c",
        "import platform; print(platform.python_version())"
    ) -TimeoutSeconds $MiseTimeoutSeconds `
        -Description "Python runtime version verification"
    if ($versionResult.ExitCode -ne 0 -or
        $versionResult.StdOut.Trim() -cne $requiredPython) {
        throw "mise returned an unexpected Python runtime."
    }

    $azureAuth = Join-Path $env:LOCALAPPDATA `
        "Programs\AzureAuth\0.9.5\azureauth.exe"
    if (-not (Test-Path -LiteralPath $azureAuth -PathType Leaf)) {
        throw "AzureAuth 0.9.5 is required at its standard installation path."
    }
    $tokenResult = Invoke-ProcessWithTimeout -FilePath $azureAuth `
        -ArgumentList @("ado", "token", "--output", "token") `
        -TimeoutSeconds $AzureAuthTimeoutSeconds `
        -Description "AzureAuth token acquisition"
    if ($tokenResult.ExitCode -ne 0 -or
        [string]::IsNullOrWhiteSpace($tokenResult.StdOut)) {
        throw "AzureAuth did not return an Azure DevOps packaging token."
    }

    $encodedToken = [Uri]::EscapeDataString($tokenResult.StdOut.Trim())
    $luciaIndex = "https://azureauth:${encodedToken}@pkgs.dev.azure.com/msazure/One/_packaging/Lucia_PrivatePackages/pypi/simple/"
    try {
        Reset-PipEnvironment
        $pipResult = Invoke-ProcessWithTimeout -FilePath $python `
            -ArgumentList @(
            "-I", "-B", "-m", "pip", "install", "--quiet",
            "--disable-pip-version-check", "--no-input", "--no-cache-dir",
            "--no-deps", "--only-binary=:all:", "--require-hashes",
            "--requirement", $requirements
        ) -EnvironmentVariables @{ PIP_INDEX_URL = $luciaIndex } `
            -TimeoutSeconds $PipTimeoutSeconds `
            -Description "pip dependency installation"
        if ($pipResult.ExitCode -ne 0) {
            throw "pip could not install the hash-locked dependencies from Lucia_PrivatePackages."
        }
    }
    finally {
        $tokenResult = $null
        $encodedToken = $null
        $luciaIndex = $null
    }

    $verifyDependencies = @'
from importlib import metadata
from pathlib import Path
import cv2, numpy, PIL, sys
root = Path(sys.prefix).resolve()
expected = {
    "numpy": (numpy, "numpy", "2.2.6"),
    "cv2": (cv2, "opencv-python-headless", "4.12.0.88"),
    "PIL": (PIL, "pillow", "12.3.0"),
}
for module, (loaded, distribution, version) in expected.items():
    if root not in Path(loaded.__file__).resolve().parents:
        raise SystemExit(f"{module} loaded outside ephemeral runtime")
    if metadata.version(distribution) != version:
        raise SystemExit(f"unexpected {distribution} version")
'@
    $dependencyResult = Invoke-ProcessWithTimeout -FilePath $python `
        -ArgumentList @("-I", "-B", "-c", $verifyDependencies) `
        -TimeoutSeconds $PipTimeoutSeconds `
        -Description "Python dependency verification"
    if ($dependencyResult.ExitCode -ne 0) {
        throw "Exact dependency origin or version verification failed."
    }

    $validatorResult = Invoke-ProcessWithTimeout -FilePath $python `
        -ArgumentList (@("-I", "-B", $validator) + $ScriptArgs) `
        -TimeoutSeconds $ValidatorTimeoutSeconds -Description "book validator"
    if ($validatorResult.StdOut) {
        [Console]::Out.Write($validatorResult.StdOut)
    }
    if ($validatorResult.StdErr) {
        [Console]::Error.Write($validatorResult.StdErr)
    }
    $exitCode = $validatorResult.ExitCode
}
finally {
    if (Test-Path -LiteralPath $runtime) {
        Remove-Item -LiteralPath $runtime -Recurse -Force
    }
}
exit $exitCode
