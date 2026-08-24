param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("restore_tone.py", "tests/test_restore_tone.py")]
    [string]$Script,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ScriptArgs
)

$ErrorActionPreference = "Stop"
$pythonVersion = "3.12.10"
$imagecodecsVersion = "2026.6.26"
$numpyVersion = "2.2.6"
$opencvVersion = "4.12.0.88"
$pillowVersion = "12.3.0"
$tifffileVersion = "2026.7.31"
$runtime = $null
$exitCode = 1
$savedEnvironment = @{}
$skillRoot = Split-Path -Parent $PSScriptRoot
$scriptPath = if ($Script -ieq "restore_tone.py") {
    Join-Path $PSScriptRoot "restore_tone.py"
}
else {
    Join-Path $skillRoot "tests\test_restore_tone.py"
}

function Save-EnvironmentVariable {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (-not $savedEnvironment.ContainsKey($Name)) {
        $savedEnvironment[$Name] = [Environment]::GetEnvironmentVariable(
            $Name, "Process"
        )
    }
}

function Set-IsolatedEnvironmentVariable {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute(
        "PSUseShouldProcessForStateChangingFunctions",
        "",
        Justification = "The function changes only this runner process."
    )]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()][string]$Value
    )

    Save-EnvironmentVariable $Name
    [Environment]::SetEnvironmentVariable($Name, $Value, "Process")
}

try {
    $requirementsPath = Join-Path $PSScriptRoot "requirements.lock"
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $requirementsPath -PathType Leaf)) {
        throw "Required skill files are missing."
    }

    $runtime = Join-Path $PSScriptRoot (
        ".runtime-" + [Guid]::NewGuid().ToString("N")
    )
    New-Item -ItemType Directory -Path $runtime | Out-Null

    $miseConfig = Join-Path $runtime "mise.toml"
    @"
[tools]
python = "$pythonVersion"
"@ | Set-Content -LiteralPath $miseConfig -Encoding UTF8

    foreach ($name in @(
            "MISE_CONFIG_FILE",
            "MISE_GLOBAL_CONFIG_FILE",
            "MISE_SYSTEM_CONFIG_FILE"
        )) {
        Set-IsolatedEnvironmentVariable $name $miseConfig
    }
    foreach ($entry in @(Get-ChildItem Env:)) {
        if ($entry.Name -match '^(?:PIP|PYTHON)') {
            Set-IsolatedEnvironmentVariable $entry.Name $null
        }
    }
    Set-IsolatedEnvironmentVariable "PYTHONNOUSERSITE" "1"
    Set-IsolatedEnvironmentVariable "PIP_CONFIG_FILE" "nul"
    Set-IsolatedEnvironmentVariable "PIP_DISABLE_PIP_VERSION_CHECK" "1"
    Set-IsolatedEnvironmentVariable "PIP_NO_INPUT" "1"

    $mise = Get-Command mise -CommandType Application -ErrorAction Stop |
        Select-Object -First 1
    & $mise.Source exec -C $runtime "python@$pythonVersion" -- `
        python -I -B -m venv $runtime
    if ($LASTEXITCODE -ne 0) {
        throw "mise could not create the ephemeral Python runtime."
    }

    $python = Join-Path $runtime "Scripts\python.exe"
    if (-not (Test-Path -LiteralPath $python -PathType Leaf)) {
        throw "mise did not create the required Python executable."
    }

    & $python -I -B -m pip install --quiet --disable-pip-version-check `
        --no-input --no-cache-dir --require-hashes --only-binary=:all: `
        --no-deps -r $requirementsPath
    if ($LASTEXITCODE -ne 0) {
        throw "pip could not install the hash-locked PyPI dependencies."
    }

    $verifyDependencies = @'
import importlib.metadata as metadata
import sys
from pathlib import Path
import cv2, imagecodecs, numpy, PIL, tifffile

root = Path(sys.argv[1]).resolve()
expected = {
    'imagecodecs': (imagecodecs, 'imagecodecs', sys.argv[2]),
    'numpy': (numpy, 'numpy', sys.argv[3]),
    'cv2': (cv2, 'opencv-python-headless', sys.argv[4]),
    'PIL': (PIL, 'Pillow', sys.argv[5]),
    'tifffile': (tifffile, 'tifffile', sys.argv[6]),
}
for package, (module, distribution, version) in expected.items():
    if root not in Path(module.__file__).resolve().parents:
        raise SystemExit(f'{package} loaded outside the ephemeral runtime')
    if metadata.version(distribution) != version:
        raise SystemExit(f'unexpected {distribution} version')
    providers = metadata.packages_distributions().get(package, ())
    if distribution.casefold() not in {provider.casefold() for provider in providers}:
        raise SystemExit(f'unexpected {package} provider')
'@
    & $python -I -B -c $verifyDependencies `
        $runtime $imagecodecsVersion $numpyVersion $opencvVersion `
        $pillowVersion $tifffileVersion
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned dependency origin or version verification failed."
    }

    & $python -I -B $scriptPath @ScriptArgs
    $exitCode = $LASTEXITCODE
}
finally {
    foreach ($entry in $savedEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable(
            $entry.Key, $entry.Value, "Process"
        )
    }
    if ($null -ne $runtime -and (Test-Path -LiteralPath $runtime)) {
        Remove-Item -LiteralPath $runtime -Recurse -Force
    }
}

exit $exitCode
