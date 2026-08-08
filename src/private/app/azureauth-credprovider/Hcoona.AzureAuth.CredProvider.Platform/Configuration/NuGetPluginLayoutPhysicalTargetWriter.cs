using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal sealed class NuGetPluginLayoutPhysicalTargetWriter(IFileSystem fileSystem)
    : IConfigurationPhysicalTargetWriter
{
    private const string MarkerFileName = ".azureauth-credprovider.nuget-plugin-layout";
    private const string SupportedKey = "physical-target";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);

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
        string markerPath = GetMarkerPath(request.Change.TargetPathOrName);
        bool remove =
            request.PlanOperation == ConfigurationPlanOperation.Remove
            || request.Change.Operation == ConfigurationChangeOperation.Remove;
        if (remove)
        {
            if (fileSystem.FileExists(markerPath))
            {
                fileSystem.DeleteFile(markerPath);
            }
            return;
        }

        if (
            fileSystem.FileExists(markerPath)
            && string.Equals(
                fileSystem.ReadAllText(markerPath, Utf8NoBom),
                request.Change.Value,
                StringComparison.Ordinal
            )
        )
        {
            return;
        }

        fileSystem.AtomicWriteAllText(markerPath, request.Change.Value!, Utf8NoBom);
    }

    public bool IsSatisfied(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        string markerPath = GetMarkerPath(request.Change.TargetPathOrName);
        return fileSystem.FileExists(markerPath)
            && string.Equals(
                fileSystem.ReadAllText(markerPath, Utf8NoBom),
                request.Change.Value,
                StringComparison.Ordinal
            );
    }

    internal static string? GetPlanningValidationViolation(ConfigurationChange change)
    {
        if (change.TargetKind != ConfigurationTargetKind.NuGetPluginLayout)
        {
            return null;
        }
        if (!string.Equals(change.Key, SupportedKey, StringComparison.Ordinal))
        {
            return "The NuGet plugin layout writer requires the physical-target key.";
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
            return "The NuGet plugin layout writer supports value-writing and remove operations.";
        }
        if (
            change.Operation != ConfigurationChangeOperation.Remove
            && string.IsNullOrWhiteSpace(change.Value)
        )
        {
            return "The NuGet plugin layout marker value is required.";
        }
        return change.IsSecretValue
            ? "The NuGet plugin layout marker must not contain secrets."
            : null;
    }

    private string? GetTargetRootPathValidationViolation(string targetRootPath) =>
        string.IsNullOrWhiteSpace(targetRootPath)
        || !fileSystem.IsPathFullyQualified(targetRootPath)
            ? "The NuGet plugin layout target must be fully qualified."
            : null;

    private void ValidateRequest(ConfigurationPhysicalTargetWriterRequest request)
    {
        if (
            request.TargetKind != ConfigurationTargetKind.NuGetPluginLayout
            || request.Changes.Count != 1
        )
        {
            throw new NotSupportedException(
                "The NuGet plugin layout writer requires one layout change."
            );
        }
        string? violation =
            GetTargetRootPathValidationViolation(request.Change.TargetPathOrName)
            ?? GetPlanningValidationViolation(request.Change);
        if (violation is not null)
        {
            throw new NotSupportedException(violation);
        }
    }

    private void ValidateCurrentState(ConfigurationPhysicalTargetWriterRequest request)
    {
        string markerPath = GetMarkerPath(request.Change.TargetPathOrName);
        bool exists = fileSystem.FileExists(markerPath);
        bool remove =
            request.PlanOperation == ConfigurationPlanOperation.Remove
            || request.Change.Operation == ConfigurationChangeOperation.Remove;
        if (remove && !request.IsOwned(request.Change, fileSystem))
        {
            throw new InvalidOperationException(
                "NuGet plugin layout removal requires recognized ownership."
            );
        }
        if (!remove && exists && !request.IsOwned(request.Change, fileSystem))
        {
            throw new InvalidOperationException(
                "The NuGet plugin layout marker exists without recognized ownership."
            );
        }
    }

    private string GetMarkerPath(string targetRootPath) =>
        FileSystemPathSemantics.Combine(fileSystem, targetRootPath, MarkerFileName);
}
