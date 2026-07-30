using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public interface IWindowsArtifactProbe
{
    WindowsArtifactProbeResult Probe(
        AzureAuthDeploymentConfig config,
        CancellationToken cancellationToken = default);
}

public sealed record WindowsArtifactProbeResult
{
    public required AzureAuthArtifactTrustStatus Status { get; init; }

    public AzureAuthArtifactEvidence? Evidence { get; init; }

    public required string SafeDetail { get; init; }

    public static WindowsArtifactProbeResult Deferred(string detail) =>
        new() { Status = AzureAuthArtifactTrustStatus.Deferred, SafeDetail = detail };

    public static WindowsArtifactProbeResult Untrusted(
        string detail,
        AzureAuthArtifactEvidence? evidence = null) =>
        new()
        {
            Status = AzureAuthArtifactTrustStatus.Untrusted,
            Evidence = evidence,
            SafeDetail = detail,
        };

    public static WindowsArtifactProbeResult Trusted(AzureAuthArtifactEvidence evidence) =>
        new()
        {
            Status = AzureAuthArtifactTrustStatus.Trusted,
            Evidence = evidence,
            SafeDetail = "The Windows AzureAuth artifact passed trust inspection.",
        };
}

public sealed class WslWindowsArtifactTrustInspector : IAzureAuthArtifactTrustInspector
{
    private readonly IWindowsArtifactProbe probe;

    public WslWindowsArtifactTrustInspector(IWindowsArtifactProbe probe)
    {
        this.probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public AzureAuthArtifactInspection Inspect(
        AzureAuthDeploymentConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            WindowsArtifactProbeResult result = probe.Probe(config, cancellationToken);
            return result.Status switch
            {
                AzureAuthArtifactTrustStatus.Trusted when result.Evidence is not null =>
                    AzureAuthArtifactInspection.Trusted(result.Evidence),
                AzureAuthArtifactTrustStatus.Untrusted =>
                    AzureAuthArtifactInspection.Untrusted(result.Evidence, result.SafeDetail),
                _ => AzureAuthArtifactInspection.Deferred(result.Evidence, result.SafeDetail),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return AzureAuthArtifactInspection.Deferred(
                safeDetail: "Windows AzureAuth trust inspection is currently unavailable.");
        }
    }

}

public sealed record SystemWindowsArtifactProbeOptions
{
    public string WindowsMountRoot { get; init; } = "/mnt/c";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    public int MaximumOutputBytes { get; init; } = 32 * 1024;

    public string? WslInterop { get; init; }

    public Func<string, string?>? EnvironmentVariableReader { get; init; }
}

/// <summary>Uses the fixed Windows PowerShell installation to inspect a Windows-host artifact.</summary>
public sealed class SystemWindowsArtifactProbe : IWindowsArtifactProbe
{
    private const string TargetPathVariable = "AZUREAUTH_PROBE_TARGET";
    private const string ProbeScript =
        """
        $ErrorActionPreference='Stop'
        function Emit($value) { $value | ConvertTo-Json -Compress -Depth 4 }
        function Resolve-Sid($identity) {
          if ($identity -is [Security.Principal.SecurityIdentifier]) { return $identity.Value }
          if ($identity -is [Security.Principal.IdentityReference]) {
            return $identity.Translate([Security.Principal.SecurityIdentifier]).Value
          }
          if ($identity -is [string] -and $identity.StartsWith('S-1-')) {
            return (New-Object Security.Principal.SecurityIdentifier($identity)).Value
          }
          return (New-Object Security.Principal.NTAccount($identity)).Translate([Security.Principal.SecurityIdentifier]).Value
        }
        function Test-PresentNonNullDacl($acl) {
          $descriptorBytes=$acl.GetSecurityDescriptorBinaryForm()
          $rawDescriptor=New-Object Security.AccessControl.RawSecurityDescriptor($descriptorBytes,0)
          $daclPresent=(($rawDescriptor.ControlFlags -band [Security.AccessControl.ControlFlags]::DiscretionaryAclPresent) -ne 0)
          return ($daclPresent -and $null -ne $rawDescriptor.DiscretionaryAcl)
        }
        function Test-TrustedDirectoryChain($start) {
          $current=Get-Item -LiteralPath $start -Force
          $canonical=$current.FullName
          $noReparse=$true
          $safeAcl=$true
          $safeDacls=$true
          $safeOwners=$true
          $isInspectedDirectory=$true
          while ($null -ne $current) {
            if (-not $current.PSIsContainer -or (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { $noReparse=$false }
            $acl=Get-Acl -LiteralPath $current.FullName
            if (-not (Test-PresentNonNullDacl $acl)) { $safeDacls=$false }
            $ownerSid=Resolve-Sid $acl.Owner
            if ($script:allowed -notcontains $ownerSid) { $safeOwners=$false }
            $mutationMask=if ($isInspectedDirectory) { $script:directoryWriteMask } else { $script:ancestorMutationMask }
            foreach ($rule in $acl.Access) {
              if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and (($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -eq 0) -and (($rule.FileSystemRights -band $mutationMask) -ne 0)) {
                $sid=Resolve-Sid $rule.IdentityReference
                if ($script:allowed -notcontains $sid) { $safeAcl=$false }
              }
            }
            $isInspectedDirectory=$false
            $current=$current.Parent
          }
          return @{ canonicalPath=$canonical; noReparsePoints=$noReparse; trustedOwners=$safeOwners; discretionaryAclsPresentAndNonNull=$safeDacls; ownerOnlyWritable=($safeOwners -and $safeAcl -and $safeDacls) }
        }
        $target=$env:AZUREAUTH_PROBE_TARGET
        $item=Get-Item -LiteralPath $target -Force
        if ($item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
          Emit @{ trusted=$false; reason='AzureAuth target is not a regular non-reparse file.' }; exit 3
        }
        if ($item.FullName -cne $target) {
          Emit @{ trusted=$false; reason='AzureAuth canonical path does not exactly match the configured path.' }; exit 3
        }
        $signature=Get-AuthenticodeSignature -LiteralPath $target
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or $null -eq $signature.SignerCertificate) {
          Emit @{ trusted=$false; reason='AzureAuth Authenticode signature is not valid.' }; exit 3
        }
        $certificate=$signature.SignerCertificate
        $version=[Diagnostics.FileVersionInfo]::GetVersionInfo($target).FileVersion
        $acl=Get-Acl -LiteralPath $target
        $executableDaclPresentAndNonNull=Test-PresentNonNullDacl $acl
        $current=[Security.Principal.WindowsIdentity]::GetCurrent()
        $ownerSid=Resolve-Sid $acl.Owner
        $trustedInstallerSid=(New-Object Security.Principal.NTAccount('NT SERVICE','TrustedInstaller')).Translate([Security.Principal.SecurityIdentifier]).Value
        $allowed=@($current.User.Value,'S-1-5-18','S-1-5-32-544',$trustedInstallerSid)
        $script:writeMask=
          [Security.AccessControl.FileSystemRights]::WriteData -bor
          [Security.AccessControl.FileSystemRights]::CreateFiles -bor
          [Security.AccessControl.FileSystemRights]::AppendData -bor
          [Security.AccessControl.FileSystemRights]::CreateDirectories -bor
          [Security.AccessControl.FileSystemRights]::WriteAttributes -bor
          [Security.AccessControl.FileSystemRights]::WriteExtendedAttributes -bor
          [Security.AccessControl.FileSystemRights]::Delete -bor
          [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
          [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
          [Security.AccessControl.FileSystemRights]::TakeOwnership
        $writeMask=$script:writeMask
        $script:directoryWriteMask=$script:writeMask
        $script:ancestorMutationMask=
          [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
          [Security.AccessControl.FileSystemRights]::Delete -bor
          [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
          [Security.AccessControl.FileSystemRights]::TakeOwnership
        $script:allowed=$allowed
        $unsafeWrite=(($allowed -notcontains $ownerSid) -or -not $executableDaclPresentAndNonNull)
        foreach ($rule in $acl.Access) {
          if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and (($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -eq 0) -and (($rule.FileSystemRights -band $writeMask) -ne 0)) {
            $sid=Resolve-Sid $rule.IdentityReference
            if ($allowed -notcontains $sid) { $unsafeWrite=$true }
          }
        }
        $directory=$item.Directory.FullName
        $executableDirectoryTrust=Test-TrustedDirectoryChain $directory
        $systemDirectoryTrust=Test-TrustedDirectoryChain 'C:\Windows\System32'
        if (-not $executableDirectoryTrust.noReparsePoints -or -not $executableDirectoryTrust.trustedOwners -or -not $executableDirectoryTrust.ownerOnlyWritable) {
          Emit @{ trusted=$false; reason='AzureAuth executable directory chain is not trusted.' }; exit 3
        }
        if (-not $systemDirectoryTrust.noReparsePoints -or -not $systemDirectoryTrust.trustedOwners -or -not $systemDirectoryTrust.ownerOnlyWritable) {
          Emit @{ trusted=$false; reason='Windows system directory chain is not trusted.' }; exit 3
        }
        if ($unsafeWrite) {
          Emit @{ trusted=$false; reason='AzureAuth executable ownership or write access is not trusted.' }; exit 3
        }
        Emit @{
          trusted=$true
          canonicalPath=$item.FullName
          stableArtifactIdentity=($item.FullName+'|'+$item.Length+'|'+$item.CreationTimeUtc.Ticks+'|'+$item.LastWriteTimeUtc.Ticks)
          sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
          signerIdentity=$certificate.Subject
          publisherName=$certificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false)
          executableVersion=$version
          provenanceIdentifier=$certificate.Thumbprint.ToLowerInvariant()
          owner=$ownerSid
          currentUserOwnsArtifact=($ownerSid -eq $current.User.Value)
          ownerOnlyWritable=(-not $unsafeWrite)
          discretionaryAclsPresentAndNonNull=($executableDaclPresentAndNonNull -and $executableDirectoryTrust.discretionaryAclsPresentAndNonNull -and $systemDirectoryTrust.discretionaryAclsPresentAndNonNull)
          trustedExecutableDirectory=$executableDirectoryTrust.canonicalPath
          executableDirectoryChainHasNoReparsePoints=$executableDirectoryTrust.noReparsePoints
          executableDirectoryChainOwnerOnlyWritable=$executableDirectoryTrust.ownerOnlyWritable
          trustedSystemDirectory=$systemDirectoryTrust.canonicalPath
          systemDirectoryChainHasNoReparsePoints=$systemDirectoryTrust.noReparsePoints
          systemDirectoryChainOwnerOnlyWritable=$systemDirectoryTrust.ownerOnlyWritable
          trustedWorkingDirectory=$systemDirectoryTrust.canonicalPath
          trustedPathEntries=@($systemDirectoryTrust.canonicalPath)
        }
        """;

    internal static string ProbeScriptForTesting => ProbeScript;

    private readonly SystemWindowsArtifactProbeOptions options;
    private readonly IProcessRunner processRunner;
    private readonly string? powerShellPath;
    private readonly string? windowsSystemDirectoryPath;
    private readonly string? wslInterop;

    public SystemWindowsArtifactProbe(
        IProcessRunner? processRunner = null,
        SystemWindowsArtifactProbeOptions? options = null)
    {
        this.options = options ?? new SystemWindowsArtifactProbeOptions();
        this.processRunner = processRunner ?? new SystemProcessRunner();
        windowsSystemDirectoryPath = ResolveWindowsSystemDirectoryPath(
            this.options.WindowsMountRoot);
        powerShellPath = windowsSystemDirectoryPath is null
            ? null
            : Path.Combine(
                windowsSystemDirectoryPath,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
        Func<string, string?> readEnvironment =
            this.options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        string? candidate = this.options.WslInterop ?? readEnvironment("WSL_INTEROP");
        wslInterop = WslInteropPathPolicy.IsValid(candidate) ? candidate : null;
    }

    public WindowsArtifactProbeResult Probe(
        AzureAuthDeploymentConfig config,
        CancellationToken cancellationToken = default)
    {
        AzureAuthDeploymentConfigPolicy.EnsureValid(config);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux()
            || powerShellPath is null
            || windowsSystemDirectoryPath is null
            || !File.Exists(powerShellPath)
            || !Directory.Exists(windowsSystemDirectoryPath))
        {
            return WindowsArtifactProbeResult.Deferred(
                "Windows PowerShell is unavailable under the configured WSL Windows mount.");
        }
        if (wslInterop is null)
        {
            return WindowsArtifactProbeResult.Deferred(
                "A valid snapshotted WSL interoperability endpoint is unavailable.");
        }

        if (options.Timeout <= TimeSpan.Zero
            || options.Timeout > ProcessStartSpec.MaximumTimeout
            || options.MaximumOutputBytes <= 0
            || options.MaximumOutputBytes > ProcessOutputCaptureOptions.MaximumStreamLimit)
        {
            return WindowsArtifactProbeResult.Deferred(
                "Windows artifact probe limits are invalid.");
        }

        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = @"C:\Windows",
            ["WINDIR"] = @"C:\Windows",
            ["TEMP"] = @"C:\Windows\Temp",
            ["TMP"] = @"C:\Windows\Temp",
            ["LOCALAPPDATA"] = @"C:\Windows\Temp",
            ["USERPROFILE"] = @"C:\Windows\System32\config\systemprofile",
            ["PATH"] = @"C:\Windows\System32",
            ["PATHEXT"] = ".COM;.EXE;.BAT;.CMD",
            ["PSModulePath"] = @"C:\Windows\System32\WindowsPowerShell\v1.0\Modules",
            ["WSL_INTEROP"] = wslInterop,
            [TargetPathVariable] = config.ExecutablePath,
        };
        WslWindowsEnvironmentBridge.AddSanitizedBridge(environment, TargetPathVariable);
        var capture = new ProcessOutputCaptureOptions
        {
            StandardOutputByteLimit = options.MaximumOutputBytes,
            StandardOutputCharacterLimit = options.MaximumOutputBytes,
            StandardErrorByteLimit = options.MaximumOutputBytes,
            StandardErrorCharacterLimit = options.MaximumOutputBytes,
        };
        var startSpec = new ProcessStartSpec(
            powerShellPath,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", ProbeScript],
            workingDirectory: windowsSystemDirectoryPath,
            environment: environment,
            environmentMode: ProcessEnvironmentMode.ExplicitOnly,
            preStartValidation: _ =>
            {
                if (!File.Exists(powerShellPath)
                    || !Directory.Exists(windowsSystemDirectoryPath))
                {
                    throw new IOException(
                        "The fixed Windows PowerShell launch paths are unavailable.");
                }

                return ValueTask.CompletedTask;
            },
            timeout: options.Timeout,
            outputCaptureOptions: capture,
            useWindowsEnvironmentVariableSemantics: true);

        ProcessResult result;
        try
        {
            result = processRunner.RunAsync(startSpec, cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return WindowsArtifactProbeResult.Deferred(
                "Windows PowerShell artifact inspection could not be started.");
        }

        if (result.Status is ProcessExecutionStatus.LaunchFailure
            or ProcessExecutionStatus.TimedOut
            or ProcessExecutionStatus.OutputTooLarge
            or ProcessExecutionStatus.InvalidOutput
            or ProcessExecutionStatus.Canceled)
        {
            return WindowsArtifactProbeResult.Deferred(
                "Windows PowerShell artifact inspection did not complete safely.");
        }

        if (result.StandardOutput.Length > options.MaximumOutputBytes
            || Encoding.UTF8.GetByteCount(result.StandardOutput) > options.MaximumOutputBytes)
        {
            return WindowsArtifactProbeResult.Deferred(
                "Windows PowerShell artifact inspection output exceeded its safe limit.");
        }

        WindowsArtifactProbeOutput? output = ParseOutput(result.StandardOutput);
        if (output is null)
        {
            return WindowsArtifactProbeResult.Untrusted(
                "Windows PowerShell returned invalid artifact evidence.");
        }

        if (!result.Succeeded || !output.Trusted)
        {
            return WindowsArtifactProbeResult.Untrusted(
                NullIfWhiteSpace(output.Reason)
                    ?? "Windows rejected the configured AzureAuth artifact.");
        }

        AzureAuthArtifactEvidence? evidence = CreateEvidence(output);
        return evidence is null
            ? WindowsArtifactProbeResult.Untrusted(
                "Windows PowerShell returned incomplete artifact evidence.")
            : WindowsArtifactProbeResult.Trusted(evidence);
    }

    private static string? ResolveWindowsSystemDirectoryPath(string mountRoot)
    {
        if (string.IsNullOrWhiteSpace(mountRoot) || !Path.IsPathFullyQualified(mountRoot))
        {
            return null;
        }

        try
        {
            string root = Path.GetFullPath(mountRoot);
            return Path.Combine(
                Path.TrimEndingDirectorySeparator(root),
                "Windows",
                "System32");
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static WindowsArtifactProbeOutput? ParseOutput(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WindowsArtifactProbeOutput>(
                json.Trim(),
                WindowsArtifactProbeJsonContext.CreateSerializerOptions());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AzureAuthArtifactEvidence? CreateEvidence(WindowsArtifactProbeOutput output)
    {
        if (output.CanonicalPath is null
            || output.StableArtifactIdentity is null
            || output.Sha256 is null
            || output.SignerIdentity is null
            || output.PublisherName is null
            || output.ExecutableVersion is null
            || output.ProvenanceIdentifier is null
            || output.Owner is null
            || output.DiscretionaryAclsPresentAndNonNull is not true
            || output.TrustedExecutableDirectory is null
            || output.TrustedSystemDirectory is null
            || output.TrustedWorkingDirectory is null
            || output.TrustedPathEntries is null)
        {
            return null;
        }

        return new AzureAuthArtifactEvidence
        {
            CanonicalPath = output.CanonicalPath,
            StableArtifactIdentity = new FileSystemEntryIdentity(output.StableArtifactIdentity),
            Sha256Hash = output.Sha256,
            SignerIdentity = output.SignerIdentity,
            PublisherName = output.PublisherName,
            ExecutableVersion = output.ExecutableVersion,
            ProvenanceIdentifier = output.ProvenanceIdentifier,
            Owner = new FileSystemOwner(output.Owner),
            CurrentUserOwnsArtifact = output.CurrentUserOwnsArtifact,
            OwnerOnlyWritable = output.OwnerOnlyWritable,
            DiscretionaryAclsPresentAndNonNull =
                output.DiscretionaryAclsPresentAndNonNull.Value,
            TrustedExecutableDirectory = output.TrustedExecutableDirectory,
            ExecutableDirectoryChainHasNoReparsePoints =
                output.ExecutableDirectoryChainHasNoReparsePoints,
            ExecutableDirectoryChainOwnerOnlyWritable =
                output.ExecutableDirectoryChainOwnerOnlyWritable,
            TrustedSystemDirectory = output.TrustedSystemDirectory,
            SystemDirectoryChainHasNoReparsePoints =
                output.SystemDirectoryChainHasNoReparsePoints,
            SystemDirectoryChainOwnerOnlyWritable =
                output.SystemDirectoryChainOwnerOnlyWritable,
            TrustedWorkingDirectory = output.TrustedWorkingDirectory,
            TrustedPathEntries = output.TrustedPathEntries,
        };
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

}

internal sealed record WindowsArtifactProbeOutput
{
    public bool Trusted { get; init; }
    public string? Reason { get; init; }
    public string? CanonicalPath { get; init; }
    public string? StableArtifactIdentity { get; init; }
    public string? Sha256 { get; init; }
    public string? SignerIdentity { get; init; }
    public string? PublisherName { get; init; }
    public string? ExecutableVersion { get; init; }
    public string? ProvenanceIdentifier { get; init; }
    public string? Owner { get; init; }
    public bool CurrentUserOwnsArtifact { get; init; }
    public bool OwnerOnlyWritable { get; init; }
    public bool? DiscretionaryAclsPresentAndNonNull { get; init; }
    public string? TrustedExecutableDirectory { get; init; }
    public bool ExecutableDirectoryChainHasNoReparsePoints { get; init; }
    public bool ExecutableDirectoryChainOwnerOnlyWritable { get; init; }
    public string? TrustedSystemDirectory { get; init; }
    public bool SystemDirectoryChainHasNoReparsePoints { get; init; }
    public bool SystemDirectoryChainOwnerOnlyWritable { get; init; }
    public string? TrustedWorkingDirectory { get; init; }
    public string[]? TrustedPathEntries { get; init; }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    NumberHandling = JsonNumberHandling.Strict,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(WindowsArtifactProbeOutput))]
internal sealed partial class WindowsArtifactProbeJsonContext : JsonSerializerContext
{
    internal static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = Default,
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            PropertyNameCaseInsensitive = false,
            AllowDuplicateProperties = false,
        };
        return options;
    }
}
