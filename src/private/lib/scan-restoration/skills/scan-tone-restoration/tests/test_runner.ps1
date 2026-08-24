$ErrorActionPreference = "Stop"
$skillRoot = Split-Path -Parent $PSScriptRoot
$scriptsRoot = Join-Path $skillRoot "scripts"
$runner = Join-Path $scriptsRoot "run.ps1"
$document = Join-Path $skillRoot "SKILL.md"

$tokens = $null
$errors = $null
$runnerAst = [Management.Automation.Language.Parser]::ParseFile(
    $runner, [ref]$tokens, [ref]$errors
)
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
        "& `$python -I -B `$scriptPath @ScriptArgs",
        'Programs\AzureAuth\0.9.5\azureauth.exe',
        'Test-Path -LiteralPath $azureAuthPath -PathType Leaf',
        "AzureAuth 0.9.5 was not found at '`$azureAuthPath'.",
        '(& $azureAuthPath ado token --output token | Out-String).Trim()',
        "[string]::IsNullOrWhiteSpace(`$token)",
        "([Uri]::EscapeDataString(`$token))"
    )) {
    if (-not $source.Contains($required)) {
        throw "run.ps1 is missing practical runner behavior: $required"
    }
}
if ($source -match "startup_launcher|azureauth-0\.9\.5\.manifest|Get-AuthenticodeSignature|COMSPEC|AppDomain|DirectoryLock") {
    throw "run.ps1 still contains removed adversarial launcher machinery."
}

$azureAuthAssignments = @(
    $runnerAst.FindAll(
        {
            param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -ceq "azureAuthPath"
        },
        $true
    )
)
if ($azureAuthAssignments.Count -ne 1) {
    throw "run.ps1 must assign the fixed AzureAuth path exactly once."
}
$azureAuthSelection = $azureAuthAssignments[0].Right.Extent.Text
foreach ($required in @(
        "Join-Path",
        "[Environment]::GetFolderPath(",
        "[Environment+SpecialFolder]::LocalApplicationData",
        'Programs\AzureAuth\0.9.5\azureauth.exe'
    )) {
    if (-not $azureAuthSelection.Contains($required)) {
        throw "AzureAuth fixed-path selection is missing: $required"
    }
}

$azureAuthDiscovery = @(
    $runnerAst.FindAll(
        {
            param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -ceq "Get-Command" -and
            $node.Extent.Text -match "AzureAuth"
        },
        $true
    )
)
if ($azureAuthDiscovery.Count -ne 0) {
    throw "run.ps1 must not discover AzureAuth from PATH."
}

$tokenCommands = @(
    $runnerAst.FindAll(
        {
            param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
            $node.InvocationOperator -eq (
                [Management.Automation.Language.TokenKind]::Ampersand
            ) -and
            $node.Extent.Text -ceq (
                "& `$azureAuthPath ado token --output token"
            )
        },
        $true
    )
)
if ($tokenCommands.Count -ne 1) {
    throw "Token acquisition must invoke the fixed AzureAuth path exactly once."
}

$indexCommands = @(
    $runnerAst.FindAll(
        {
            param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -ceq "Set-IsolatedEnvironmentVariable" -and
            $node.CommandElements.Count -eq 3 -and
            $node.CommandElements[1].Extent.Text -ceq '"PIP_INDEX_URL"'
        },
        $true
    )
)
if ($indexCommands.Count -ne 1) {
    throw "run.ps1 must set the isolated package index exactly once."
}
$fakeToken = "fake token+/@:?&=#%"
Set-Variable -Name token -Value $fakeToken
$indexExpression = [ScriptBlock]::Create(
    $indexCommands[0].CommandElements[2].Extent.Text
)
$capturedUrl = & $indexExpression
Remove-Variable -Name token
$expectedUrl = (
    "https://azureauth:{0}@pkgs.dev.azure.com/msazure/One/" +
    "_packaging/Lucia_PrivatePackages/pypi/simple/"
) -f ([Uri]::EscapeDataString($fakeToken))
if ($capturedUrl -cne $expectedUrl) {
    throw "PIP_INDEX_URL did not contain the escaped fake token."
}
if ($capturedUrl.Contains("{0}")) {
    throw "PIP_INDEX_URL retained the literal format placeholder."
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
