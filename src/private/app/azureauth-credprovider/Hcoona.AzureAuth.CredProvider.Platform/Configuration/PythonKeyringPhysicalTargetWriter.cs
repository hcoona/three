using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal sealed class PythonKeyringPhysicalTargetWriter(IFileSystem fileSystem)
    : IConfigurationPhysicalTargetWriter
{
    private const string SupportedKey = "physical-target";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);
    private static readonly UnixFileMode ExecutableOwnerMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public void Validate(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        ValidateCurrentState(request);
    }

    public void Write(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        ValidateCurrentState(request);
        string targetPath = fileSystem.GetFullPath(request.Change.TargetPathOrName);
        bool remove =
            request.PlanOperation == ConfigurationPlanOperation.Remove
            || request.Change.Operation == ConfigurationChangeOperation.Remove;
        if (remove)
        {
            if (fileSystem.FileExists(targetPath))
            {
                fileSystem.DeleteFile(targetPath);
            }
            return;
        }

        if (
            !fileSystem.FileExists(targetPath)
            || !string.Equals(
                fileSystem.ReadAllText(targetPath, Utf8NoBom),
                request.Change.Value,
                StringComparison.Ordinal
            )
        )
        {
            fileSystem.AtomicWriteAllText(targetPath, request.Change.Value!, Utf8NoBom);
        }

        if (
            request.TargetKind == ConfigurationTargetKind.KeyringShim
            && !FileSystemPathSemantics.UsesWindowsPaths(fileSystem)
            && fileSystem.GetUnixFileMode(targetPath) != ExecutableOwnerMode
        )
        {
            fileSystem.SetUnixFileMode(targetPath, ExecutableOwnerMode);
        }
    }

    public bool IsSatisfied(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        string targetPath = fileSystem.GetFullPath(request.Change.TargetPathOrName);
        return fileSystem.FileExists(targetPath)
            && string.Equals(
                fileSystem.ReadAllText(targetPath, Utf8NoBom),
                request.Change.Value,
                StringComparison.Ordinal
            );
    }

    internal static string? GetPlanningValidationViolation(ConfigurationChange change)
    {
        if (
            change.TargetKind
            is not (
                ConfigurationTargetKind.PythonKeyringBackend
                or ConfigurationTargetKind.KeyringShim
            )
        )
        {
            return null;
        }
        if (!string.Equals(change.Key, SupportedKey, StringComparison.Ordinal))
        {
            return "The Python keyring writer requires the physical-target key.";
        }
        if (
            change.Operation
            is not (
                ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh
                or ConfigurationChangeOperation.Remove
            )
        )
        {
            return "The Python keyring writer supports value-writing and remove operations.";
        }
        if (
            change.Operation != ConfigurationChangeOperation.Remove
            && string.IsNullOrWhiteSpace(change.Value)
        )
        {
            return "The Python keyring target value is required.";
        }
        return change.IsSecretValue
            ? "Python keyring configuration targets must not contain secrets."
            : null;
    }

    private string? GetTargetPathValidationViolation(
        string targetPathOrName,
        ConfigurationTargetKind targetKind
    ) =>
        targetKind
            is ConfigurationTargetKind.PythonKeyringBackend
                or ConfigurationTargetKind.KeyringShim
        && (
            string.IsNullOrWhiteSpace(targetPathOrName)
            || !fileSystem.IsPathFullyQualified(targetPathOrName)
        )
            ? "The Python keyring target path must be fully qualified."
            : null;

    private void ValidateRequest(ConfigurationPhysicalTargetWriterRequest request)
    {
        if (
            request.TargetKind
                is not (
                    ConfigurationTargetKind.PythonKeyringBackend
                    or ConfigurationTargetKind.KeyringShim
                )
            || request.Changes.Count != 1
        )
        {
            throw new NotSupportedException(
                "The Python keyring writer requires one backend or shim change."
            );
        }

        string? violation =
            GetTargetPathValidationViolation(request.Change.TargetPathOrName, request.TargetKind)
            ?? GetPlanningValidationViolation(request.Change);
        if (violation is not null)
        {
            throw new NotSupportedException(violation);
        }
    }

    private void ValidateCurrentState(ConfigurationPhysicalTargetWriterRequest request)
    {
        string targetPath = fileSystem.GetFullPath(request.Change.TargetPathOrName);
        if (fileSystem.DirectoryExists(targetPath))
        {
            throw new InvalidOperationException("The Python keyring target exists as a directory.");
        }

        bool exists = fileSystem.FileExists(targetPath);
        bool remove =
            request.PlanOperation == ConfigurationPlanOperation.Remove
            || request.Change.Operation == ConfigurationChangeOperation.Remove;
        if (remove && !request.IsOwned(request.Change, fileSystem))
        {
            throw new InvalidOperationException(
                "Python keyring target removal requires recognized ownership."
            );
        }
        if (!remove && exists && !request.IsOwned(request.Change, fileSystem))
        {
            throw new InvalidOperationException(
                "The Python keyring target exists without recognized ownership."
            );
        }
    }
}
