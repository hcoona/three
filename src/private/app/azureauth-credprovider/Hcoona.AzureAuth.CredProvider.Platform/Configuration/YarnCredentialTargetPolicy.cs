using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal static class YarnCredentialTargetPolicy
{
    internal const string RepositoryLocalCredentialMessage =
        "Repository-local Yarn configuration cannot store credential material.";

    internal static string ResolveAuthoritativeWritePath(
        IFileSystem fileSystem,
        string targetYarnrcPath
    ) =>
        fileSystem is IFileSystemLinkResolver linkResolver
            ? linkResolver.ResolveFilePathForWrite(targetYarnrcPath)
            : targetYarnrcPath;

    internal static bool IsRepositoryLocalPath(
        IFileSystem fileSystem,
        string authoritativeWritePath
    )
    {
        for (
            string? directory = FileSystemPathSemantics.GetParentDirectory(
                fileSystem,
                authoritativeWritePath
            );
            directory is not null;
            directory = FileSystemPathSemantics.GetParentDirectory(fileSystem, directory)
        )
        {
            string gitMarkerPath = FileSystemPathSemantics.Combine(
                fileSystem,
                directory,
                ".git"
            );
            if (
                fileSystem.DirectoryExists(gitMarkerPath)
                || fileSystem.FileExists(gitMarkerPath)
            )
            {
                return true;
            }
        }

        return false;
    }

    internal static void ThrowIfRepositoryLocal(
        IFileSystem fileSystem,
        string authoritativeWritePath
    )
    {
        if (IsRepositoryLocalPath(fileSystem, authoritativeWritePath))
        {
            throw new InvalidOperationException(RepositoryLocalCredentialMessage);
        }
    }
}
