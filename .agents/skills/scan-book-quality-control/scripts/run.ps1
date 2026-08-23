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
            $timeoutCleanupGraceMilliseconds = 5000
            $cleanupTimer = [Diagnostics.Stopwatch]::StartNew()
            $cleanupFailureReasons = @()
            $stopProcess = {
                param(
                    [Diagnostics.Process]$TargetProcess,
                    [string]$FailureDescription
                )
                try {
                    $TargetProcess.Kill()
                    return $null
                }
                catch [System.Management.Automation.MethodInvocationException] {
                    return (
                        $FailureDescription + ": " +
                        $_.Exception.GetBaseException().Message
                    )
                }
            }
            $taskkill = Join-Path $env:SystemRoot "System32\taskkill.exe"
            $taskkillProcess = [Diagnostics.Process]::new()
            $taskkillProcess.StartInfo.FileName = $taskkill
            $taskkillProcess.StartInfo.Arguments = "/PID $($process.Id) /T /F"
            $taskkillProcess.StartInfo.UseShellExecute = $false
            $taskkillProcess.StartInfo.CreateNoWindow = $true
            $taskkillProcess.StartInfo.RedirectStandardOutput = $true
            $taskkillProcess.StartInfo.RedirectStandardError = $true
            $taskkillSucceeded = $false
            try {
                try {
                    if ($taskkillProcess.Start()) {
                        [void]$taskkillProcess.StandardOutput.ReadToEndAsync()
                        [void]$taskkillProcess.StandardError.ReadToEndAsync()
                        $remainingCleanupMilliseconds = [Math]::Max(
                            0,
                            $timeoutCleanupGraceMilliseconds -
                            [int]$cleanupTimer.ElapsedMilliseconds
                        )
                        $taskkillWaitMilliseconds = [Math]::Min(
                            [int]($timeoutCleanupGraceMilliseconds / 2),
                            $remainingCleanupMilliseconds
                        )
                        if ($taskkillProcess.WaitForExit(
                                $taskkillWaitMilliseconds
                            )) {
                            $taskkillSucceeded = $taskkillProcess.ExitCode -eq 0
                            if (-not $taskkillSucceeded) {
                                $cleanupFailureReasons += (
                                    "taskkill exited with code " +
                                    $taskkillProcess.ExitCode
                                )
                            }
                        }
                        else {
                            $cleanupFailureReasons += (
                                "taskkill exceeded its bounded cleanup wait"
                            )
                            $stopFailure = & $stopProcess $taskkillProcess `
                                "taskkill could not be stopped"
                            if ($null -ne $stopFailure) {
                                $cleanupFailureReasons += $stopFailure
                            }
                            [void]$taskkillProcess.WaitForExit(0)
                        }
                    }
                    else {
                        $cleanupFailureReasons += "taskkill could not be started"
                    }
                }
                catch [System.Management.Automation.MethodInvocationException] {
                    $cleanupFailureReasons += (
                        "taskkill could not be started: " +
                        $_.Exception.GetBaseException().Message
                    )
                }
            }
            finally {
                $taskkillProcess.Dispose()
            }

            if (-not $taskkillSucceeded -and -not $process.HasExited) {
                $stopFailure = & $stopProcess $process `
                    "the root process could not be stopped"
                if ($null -ne $stopFailure) {
                    $cleanupFailureReasons += $stopFailure
                }
            }

            $remainingCleanupMilliseconds = [Math]::Max(
                0,
                $timeoutCleanupGraceMilliseconds -
                [int]$cleanupTimer.ElapsedMilliseconds
            )
            $processExited = $process.HasExited
            if (-not $processExited -and $remainingCleanupMilliseconds -gt 0) {
                $processExited = $process.WaitForExit(
                    $remainingCleanupMilliseconds
                )
            }
            if (-not $taskkillSucceeded -or -not $processExited) {
                if (-not $processExited) {
                    $cleanupFailureReasons += (
                        "the root process did not exit within the cleanup deadline"
                    )
                    $stopFailure = & $stopProcess $process `
                        "the final root-process stop failed"
                    if ($null -ne $stopFailure) {
                        $cleanupFailureReasons += $stopFailure
                    }
                    [void]$process.WaitForExit(0)
                }
                if ($cleanupFailureReasons.Count -eq 0) {
                    $cleanupFailureReasons += (
                        "taskkill did not confirm process-tree termination"
                    )
                }
                throw (
                    "$Description timed out after $TimeoutSeconds seconds, and " +
                    "process-tree termination could not be confirmed within " +
                    "$timeoutCleanupGraceMilliseconds milliseconds: " +
                    ($cleanupFailureReasons -join "; ")
                )
            }
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
if ($Script -cnotin @("validate_book.py", "tests/test_validate_book.py")) {
    throw "Script must be exactly validate_book.py or tests/test_validate_book.py."
}

$target = if ($Script -ceq "validate_book.py") {
    Join-Path $PSScriptRoot "validate_book.py"
}
else {
    Join-Path $skillBase "tests\test_validate_book.py"
}
$requirements = Join-Path $PSScriptRoot "requirements.lock"
if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or
    -not (Test-Path -LiteralPath $requirements -PathType Leaf)) {
    throw "The requested script or dependency lock is missing."
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
        -ArgumentList (@("-I", "-B", $target) + $ScriptArgs) `
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
