using Hcoona.CelesphoniaModifier.Atlas;

namespace Hcoona.CelesphoniaModifier.Atlas.Cli;

internal class AtlasCliOperations
{
    public static AtlasCliOperations Default { get; } = new();

    public virtual ValueTask WriteEmptySurveyAsync(
        Stream standardOutput,
        CancellationToken cancellationToken) =>
        EmptyAtlasSurvey.WriteAsync(standardOutput, cancellationToken);

    public virtual ValueTask RunIntakeDiscoverAsync(
        string requestFilePath,
        CancellationToken cancellationToken) =>
        AtlasDiscovery.DiscoverAsync(requestFilePath, cancellationToken);

    public virtual ValueTask RunIntakeConfirmAsync(
        string requestFilePath,
        CancellationToken cancellationToken) =>
        AtlasDiscovery.ConfirmAsync(requestFilePath, cancellationToken);

    public virtual ValueTask RunIntakeCopyAsync(
        string requestFilePath,
        CancellationToken cancellationToken) =>
        TrustedLocalCopy.CopyAsync(requestFilePath, cancellationToken);

    public virtual ValueTask RunCleanupPreflightAsync(
        string requestFilePath,
        CancellationToken cancellationToken) =>
        PrivateArtifactLifecycle.CleanupPreflightAsync(requestFilePath, cancellationToken);
}
