using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Packaging;

namespace Hcoona.AzureAuth.CredProvider.Platform.Installer;

internal static class FakeAdapterDiscoveryScaffold
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );
    private static readonly UnixFileMode OwnerExecutableFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private static readonly UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const string ProductDirectoryName = "azureauth-credprovider";
    private const string GitHelperDirectoryName = "git-helper";
    private const string GitHelperFileName = "git-credential-azureauth-credprovider";
    private const string NuGetNetCorePluginEntrypointFileName = "azureauth-credprovider.dll";
    private const string WindowsProductCompanyDirectoryName = "AzureAuth";
    private const string WindowsProductNameDirectoryName = "CredProvider";
    private const string PythonKeyringDirectoryName = "python-keyring";
    private const string PythonEnvironmentDirectoryName = "python-environments";
    private const string FakePythonEnvironmentDirectoryName = "fake-environment";
    private const string PythonSitePackagesDirectoryName = "site-packages";
    private const string PythonKeyringBackendRegistrationDirectoryName =
        "azureauth_keyring_backend-1.0.dist-info";
    private const string PythonKeyringBackendRegistrationFileName = "entry_points.txt";
    private const string PythonKeyringHelperFileName = KeyringHelperV2.CommandName;

    public static IReadOnlyList<FakeAdapterPlacement> ProjectPlacements(
        ConfigurationLayoutProjectionContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        return
        [
            ProjectGitHelperPlacement(context),
            ProjectNuGetNetCorePluginPlacement(context),
            ProjectPythonKeyringBackendPlacement(context),
            ProjectPythonKeyringHelperPlacement(context),
            ProjectKeyringShimPlacement(context),
        ];
    }

    public static FakeAdapterPlacement ProjectPlacement(
        FakeAdapterSurface surface,
        ConfigurationLayoutProjectionContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateSurface(surface);

        return surface switch
        {
            FakeAdapterSurface.GitHelper => ProjectGitHelperPlacementForSingleSurface(context),
            FakeAdapterSurface.NuGetNetCorePlugin =>
                ProjectNuGetNetCorePluginPlacementForSingleSurface(context),
            FakeAdapterSurface.PythonKeyringBackend =>
                ProjectPythonKeyringBackendPlacementForSingleSurface(context),
            FakeAdapterSurface.PythonKeyringHelper =>
                ProjectPythonKeyringHelperPlacementForSingleSurface(context),
            FakeAdapterSurface.KeyringShim => ProjectKeyringShimPlacementForSingleSurface(context),
            _ => throw new InvalidOperationException("Unknown fake adapter surface."),
        };
    }

    public static IReadOnlyList<FakeAdapterPlacement> MaterializePlacements(
        FakeAdapterMaterializationContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Layout);
        ArgumentNullException.ThrowIfNull(context.FileSystem);

        EnsureRealFileSystemMaterializationIsRejected(context);
        EnsureLayoutRootsAreSafeForMaterialization(context.Layout);
        FakeAdapterPlacement[] placements = ProjectPlacements(context.Layout)
            .Select(placement => ValidatePlacementForMaterialization(placement, context.Layout))
            .ToArray();
        EnsurePlacementPathsAreFullyQualifiedForFileSystem(placements, context);
        EnsureMaterializationPathsCanBeSafelyMutated(placements, context);
        EnsureMaterializationPlacementsDoNotHaveWrongKindConflicts(placements, context);
        EnsureMaterializationPlacementsDoNotOverwriteUnexpectedExistingFiles(placements, context);
        EnsureExecutableMaterializationPlacementsHaveSafePreexistingTrustedParents(
            placements,
            context
        );
        foreach (FakeAdapterPlacement placement in placements)
        {
            MaterializePlacementCore(placement, context);
        }

        return placements;
    }

    public static IReadOnlyList<FakeAdapterPlacement> RemovePlacements(
        FakeAdapterMaterializationContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Layout);
        ArgumentNullException.ThrowIfNull(context.FileSystem);

        EnsureRealFileSystemMaterializationIsRejected(context);
        EnsureLayoutRootsAreSafeForMaterialization(context.Layout);
        FakeAdapterPlacement[] placements = ProjectPlacements(context.Layout)
            .Select(placement => ValidatePlacementForMaterialization(placement, context.Layout))
            .ToArray();
        EnsurePlacementPathsAreFullyQualifiedForFileSystem(placements, context);
        EnsureMaterializationPlacementsDoNotHaveWrongKindConflicts(placements, context);
        foreach (FakeAdapterPlacement placement in placements.Reverse())
        {
            RemovePlacementCore(placement, context);
        }

        return placements;
    }

    public static FakeAdapterPlacement MaterializePlacement(
        FakeAdapterSurface surface,
        FakeAdapterMaterializationContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Layout);
        ArgumentNullException.ThrowIfNull(context.FileSystem);
        ValidateSurface(surface);

        EnsureRealFileSystemMaterializationIsRejected(context);
        EnsureRelevantLayoutRootsAreSafeForMaterialization(surface, context.Layout);
        FakeAdapterPlacement placement = ProjectPlacement(surface, context.Layout);
        return MaterializePlacement(placement, context);
    }

    public static FakeAdapterPlacement MaterializePlacement(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context
    )
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Layout);
        ArgumentNullException.ThrowIfNull(context.FileSystem);

        EnsureRealFileSystemMaterializationIsRejected(context);
        EnsureRelevantLayoutRootsAreSafeForMaterialization(placement.Surface, context.Layout);
        FakeAdapterPlacement projectedPlacement = ValidatePlacementForMaterialization(
            placement,
            context.Layout
        );
        EnsurePlacementPathsAreFullyQualifiedForFileSystem([projectedPlacement], context);
        EnsureMaterializationPathCanBeSafelyMutated(projectedPlacement, context);
        EnsureMaterializationPlacementsDoNotHaveWrongKindConflicts([projectedPlacement], context);
        EnsureMaterializationPlacementsDoNotOverwriteUnexpectedExistingFiles(
            [projectedPlacement],
            context
        );
        MaterializePlacementCore(projectedPlacement, context);
        return projectedPlacement;
    }

    public static FakeAdapterPlacement RemovePlacement(
        FakeAdapterSurface surface,
        FakeAdapterMaterializationContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Layout);
        ArgumentNullException.ThrowIfNull(context.FileSystem);
        ValidateSurface(surface);

        EnsureRealFileSystemMaterializationIsRejected(context);
        EnsureRelevantLayoutRootsAreSafeForMaterialization(surface, context.Layout);
        FakeAdapterPlacement placement = ProjectPlacement(surface, context.Layout);
        return RemovePlacement(placement, context);
    }

    public static FakeAdapterPlacement RemovePlacement(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context
    )
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Layout);
        ArgumentNullException.ThrowIfNull(context.FileSystem);

        EnsureRealFileSystemMaterializationIsRejected(context);
        EnsureRelevantLayoutRootsAreSafeForMaterialization(placement.Surface, context.Layout);
        FakeAdapterPlacement projectedPlacement = ValidatePlacementForMaterialization(
            placement,
            context.Layout
        );
        EnsurePlacementPathsAreFullyQualifiedForFileSystem([projectedPlacement], context);
        EnsureMaterializationPlacementDoesNotHaveWrongKindConflicts(
            projectedPlacement,
            CreateMaterializationProbeContext(context),
            context.Layout.Platform
        );
        RemovePlacementCore(projectedPlacement, context);
        return projectedPlacement;
    }


    public static IReadOnlyList<FakeAdapterProbeResult> ProbePlacements(
        FakeAdapterDiscoveryContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Layout);

        return GetProbeablePlacements(context)
            .Select(placement => ProbePlacement(placement, context))
            .ToArray();
    }

    public static FakeAdapterProbeResult ProbePlacement(
        FakeAdapterSurface surface,
        FakeAdapterDiscoveryContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Layout);
        ValidateSurface(surface);

        return ProbePlacement(ProjectPlacementForSafeProbe(surface, context), context);
    }

    public static FakeAdapterProbeResult ProbePlacement(
        FakeAdapterPlacement placement,
        FakeAdapterDiscoveryContext context
    )
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(context);
        Func<string, bool>? fileExists = context.FileExists;
        Func<string, bool>? directoryExists = context.DirectoryExists;

        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(directoryExists);

        FakeAdapterArtifactKind actualKind = placement.ArtifactKind switch
        {
            FakeAdapterArtifactKind.File => fileExists(placement.ArtifactPath)
                ? FakeAdapterArtifactKind.File
                : directoryExists(placement.ArtifactPath)
                    ? FakeAdapterArtifactKind.Directory
                    : FakeAdapterArtifactKind.Missing,
            FakeAdapterArtifactKind.Directory => directoryExists(placement.ArtifactPath)
                ? FakeAdapterArtifactKind.Directory
                : fileExists(placement.ArtifactPath)
                    ? FakeAdapterArtifactKind.File
                    : FakeAdapterArtifactKind.Missing,
            FakeAdapterArtifactKind.Missing => throw new InvalidOperationException(
                "Fake adapter placements must probe file or directory artifacts."
            ),
            _ => throw new InvalidOperationException("Unknown fake adapter artifact kind."),
        };

        return new FakeAdapterProbeResult
        {
            Surface = placement.Surface,
            PlacementRoot = placement.PlacementRoot,
            ArtifactPath = placement.ArtifactPath,
            ExpectedKind = placement.ArtifactKind,
            ActualKind = actualKind,
            Status =
                actualKind == placement.ArtifactKind
                    ? FakeAdapterProbeStatus.Found
                    : actualKind == FakeAdapterArtifactKind.Missing
                        ? FakeAdapterProbeStatus.Missing
                        : FakeAdapterProbeStatus.WrongKind,
        };
    }

    private static void MaterializePlacementCore(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context
    )
    {
        IFakeAdapterScaffoldMaterializationFileSystem scaffoldFileSystem =
            GetFakeAdapterScaffoldMaterializationFileSystem(context);
        List<string> posixDirectoriesToRestrict = CapturePosixDirectoriesToRestrict(
            placement,
            context
        );
        bool requiresTrustedPosixParentValidation = TryGetUnixExecutableFileMode(
            placement.Surface,
            context.Layout.Platform,
            out UnixFileMode unixFileMode
        );
        switch (placement.ArtifactKind)
        {
            case FakeAdapterArtifactKind.File:
                EnsureArtifactPathIsWithinPlacementRoot(placement, context.Layout.Platform);
                EnsureMaterializationPathCanBeSafelyMutated(placement, context);
                string deterministicContents = BuildDeterministicArtifactContents(
                    placement,
                    context.Layout.Platform
                );
                FakeAdapterExistingFileMaterializationState existingFileState =
                    GetExistingFileMaterializationState(
                        placement,
                        context,
                        deterministicContents,
                        validateExecutableTrustedParentDirectories: false
                    );
                EnsureExistingFileMaterializationStateIsAllowed(placement, existingFileState);
                EnsurePreexistingPosixParentDirectoriesForExecutableMaterializationAreSafeToMutate(
                    placement,
                    context,
                    requiresTrustedPosixParentValidation
                );
                if (
                    existingFileState
                    == FakeAdapterExistingFileMaterializationState.MatchesExpectedFakeArtifact
                )
                {
                    EnsureMaterializationPathCanBeSafelyMutated(placement, context);
                    RestrictPosixDirectoryModes(context, posixDirectoriesToRestrict);
                    EnsureTrustedPosixParentDirectoriesForExecutableMaterialization(
                        placement,
                        context,
                        requiresTrustedPosixParentValidation
                    );
                    EnsureExistingFileStillMatchesExpectedFakeArtifactBeforeIdempotentReturn(
                        placement,
                        context,
                        deterministicContents
                    );
                    break;
                }

                EnsureConditionalFileMutationSupportForScaffoldCreation(placement, context);
                scaffoldFileSystem.CreateDirectoryNoFollow(placement.PlacementRoot);
                scaffoldFileSystem.CreateDirectoryNoFollow(
                    GetContainingDirectory(context.Layout.Platform, placement.ArtifactPath)
                );
                RestrictPosixDirectoryModes(context, posixDirectoriesToRestrict);
                EnsureTrustedPosixParentDirectoriesForExecutableMaterialization(
                    placement,
                    context,
                    requiresTrustedPosixParentValidation
                );
                FileIntegritySnapshot? createdFileSnapshotNoFollow =
                    requiresTrustedPosixParentValidation
                        ? scaffoldFileSystem.AtomicWriteAllTextAndCaptureSnapshotNoFollow(
                            placement.ArtifactPath,
                            deterministicContents,
                            Utf8NoBom,
                            AtomicWriteOptions.None,
                            FileMutationExpectation.Missing
                        )
                        : null;
                if (!requiresTrustedPosixParentValidation)
                {
                    context.FileSystem.AtomicWriteAllText(
                        placement.ArtifactPath,
                        deterministicContents,
                        Utf8NoBom,
                        AtomicWriteOptions.None,
                        FileMutationExpectation.Missing
                    );
                    EnsureExistingFileStillMatchesExpectedFakeArtifactBeforeIdempotentReturn(
                        placement,
                        context,
                        deterministicContents
                    );
                }

                if (requiresTrustedPosixParentValidation)
                {
                    try
                    {
                        scaffoldFileSystem.SetUnixFileModeNoFollow(
                            placement.ArtifactPath,
                            unixFileMode
                        );
                    }
                    catch (Exception exception)
                    {
                        if (
                            MaterializationPathContainsUnsupportedLinkOrReparsePoint(
                                placement,
                                context
                            )
                        )
                        {
                            throw;
                        }

                        try
                        {
                            scaffoldFileSystem.DeleteFileIfMatchesSnapshotNoFollow(
                                placement.ArtifactPath,
                                createdFileSnapshotNoFollow!
                            );
                        }
                        catch (Exception rollbackException)
                        {
                            throw new InvalidOperationException(
                                "Fake adapter scaffold materialization failed to apply the "
                                    + $"expected executable mode for '{placement.ArtifactPath}', "
                                    + "and rollback of the newly created scaffold file failed.",
                                new AggregateException(exception, rollbackException)
                            );
                        }

                        throw;
                    }

                    EnsureExistingFileStillMatchesExpectedFakeArtifactBeforeIdempotentReturn(
                        placement,
                        context,
                        deterministicContents
                    );
                }

                break;
            case FakeAdapterArtifactKind.Directory:
                EnsureArtifactPathIsWithinPlacementRoot(placement, context.Layout.Platform);
                EnsureMaterializationPathCanBeSafelyMutated(placement, context);
                scaffoldFileSystem.CreateDirectoryNoFollow(placement.PlacementRoot);
                scaffoldFileSystem.CreateDirectoryNoFollow(placement.ArtifactPath);
                RestrictPosixDirectoryModes(context, posixDirectoriesToRestrict);
                break;
            case FakeAdapterArtifactKind.Missing:
                throw new InvalidOperationException(
                    "Fake adapter placements must materialize file or directory artifacts."
                );
            default:
                throw new InvalidOperationException("Unknown fake adapter artifact kind.");
        }
    }

    private static void RemovePlacementCore(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context
    )
    {
        switch (placement.ArtifactKind)
        {
            case FakeAdapterArtifactKind.File:
                RemoveFilePlacementCore(placement, context);
                break;
            case FakeAdapterArtifactKind.Directory:
                throw new InvalidOperationException(
                    "Fake adapter scaffold removal only supports file artifacts."
                );
            case FakeAdapterArtifactKind.Missing:
                throw new InvalidOperationException(
                    "Fake adapter placements must remove file or directory artifacts."
                );
            default:
                throw new InvalidOperationException("Unknown fake adapter artifact kind.");
        }
    }

    private static void RemoveFilePlacementCore(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context
    )
    {
        string deterministicContents = BuildDeterministicArtifactContents(
            placement,
            context.Layout.Platform
        );
        FakeAdapterExistingFileMaterializationState existingFileState =
            GetExistingFileMaterializationState(
                placement,
                context,
                deterministicContents,
                validateExecutableTrustedParentDirectories: false
            );
        EnsureExistingFileRemovalStateIsAllowed(placement, existingFileState);
        if (existingFileState == FakeAdapterExistingFileMaterializationState.Missing)
        {
            return;
        }

        EnsureMaterializationPathCanBeSafelyMutated(placement, context);
        context.FileSystem.DeleteFile(
            placement.ArtifactPath,
            FileMutationExpectation.Existing(ComputeSha256(Utf8NoBom.GetBytes(
                deterministicContents
            )))
        );
    }

    private static FakeAdapterPlacement ProjectGitHelperPlacement(
        ConfigurationLayoutProjectionContext context
    ) => BuildGitHelperPlacement(context.Platform, GetProductDataRoot(context));

    private static FakeAdapterPlacement ProjectGitHelperPlacementForSingleSurface(
        ConfigurationLayoutProjectionContext context
    ) => BuildGitHelperPlacement(context.Platform, GetProductDataRootForSingleSurface(context));

    private static FakeAdapterPlacement BuildGitHelperPlacement(
        ConfigurationLayoutPlatform platform,
        string productDataRoot
    )
    {
        string placementRoot = Combine(platform, productDataRoot, GitHelperDirectoryName);
        return new FakeAdapterPlacement
        {
            Surface = FakeAdapterSurface.GitHelper,
            PlacementRoot = placementRoot,
            ArtifactPath = Combine(
                platform,
                placementRoot,
                GetExecutableFileName(platform, GitHelperFileName)
            ),
        };
    }

    private static FakeAdapterPlacement ProjectNuGetNetCorePluginPlacement(
        ConfigurationLayoutProjectionContext context
    )
    {
        ConfigurationTargetLayoutProjection nuGetPluginLayout =
            ConfigurationLayoutProjector.ProjectNuGetPluginLayout(context);
        return BuildNuGetNetCorePluginPlacement(context.Platform, nuGetPluginLayout.TargetPath);
    }

    private static FakeAdapterPlacement ProjectNuGetNetCorePluginPlacementForSingleSurface(
        ConfigurationLayoutProjectionContext context
    ) => BuildNuGetNetCorePluginPlacement(
        context.Platform,
        GetNuGetNetCorePluginPlacementRootForSingleSurface(context)
    );

    private static FakeAdapterPlacement BuildNuGetNetCorePluginPlacement(
        ConfigurationLayoutPlatform platform,
        string placementRoot
    )
    {
        return new FakeAdapterPlacement
        {
            Surface = FakeAdapterSurface.NuGetNetCorePlugin,
            PlacementRoot = placementRoot,
            ArtifactPath = Combine(
                platform,
                placementRoot,
                NuGetNetCorePluginEntrypointFileName
            ),
        };
    }

    private static FakeAdapterPlacement ProjectPythonKeyringBackendPlacement(
        ConfigurationLayoutProjectionContext context
    ) => BuildPythonKeyringBackendPlacement(context.Platform, GetProductDataRoot(context));

    private static FakeAdapterPlacement ProjectPythonKeyringBackendPlacementForSingleSurface(
        ConfigurationLayoutProjectionContext context
    ) => BuildPythonKeyringBackendPlacement(
        context.Platform,
        GetProductDataRootForSingleSurface(context)
    );

    private static FakeAdapterPlacement BuildPythonKeyringBackendPlacement(
        ConfigurationLayoutPlatform platform,
        string productDataRoot
    )
    {
        string placementRoot = Combine(
            platform,
            productDataRoot,
            PythonEnvironmentDirectoryName,
            FakePythonEnvironmentDirectoryName,
            GetPythonSitePackagesDirectoryName(platform),
            PythonKeyringBackendRegistrationDirectoryName
        );
        return new FakeAdapterPlacement
        {
            Surface = FakeAdapterSurface.PythonKeyringBackend,
            PlacementRoot = placementRoot,
            ArtifactPath = Combine(
                platform,
                placementRoot,
                PythonKeyringBackendRegistrationFileName
            ),
        };
    }

    private static FakeAdapterPlacement ProjectPythonKeyringHelperPlacement(
        ConfigurationLayoutProjectionContext context
    ) => BuildPythonKeyringHelperPlacement(context.Platform, GetProductDataRoot(context));

    private static FakeAdapterPlacement ProjectPythonKeyringHelperPlacementForSingleSurface(
        ConfigurationLayoutProjectionContext context
    ) => BuildPythonKeyringHelperPlacement(
        context.Platform,
        GetProductDataRootForSingleSurface(context)
    );

    private static FakeAdapterPlacement BuildPythonKeyringHelperPlacement(
        ConfigurationLayoutPlatform platform,
        string productDataRoot
    )
    {
        string placementRoot = Combine(platform, productDataRoot, PythonKeyringDirectoryName);
        return new FakeAdapterPlacement
        {
            Surface = FakeAdapterSurface.PythonKeyringHelper,
            PlacementRoot = placementRoot,
            ArtifactPath = Combine(
                platform,
                placementRoot,
                GetExecutableFileName(platform, PythonKeyringHelperFileName)
            ),
        };
    }

    private static FakeAdapterPlacement ProjectKeyringShimPlacement(
        ConfigurationLayoutProjectionContext context
    ) => BuildKeyringShimPlacement(context.Platform, GetProductDataRoot(context));

    private static FakeAdapterPlacement ProjectKeyringShimPlacementForSingleSurface(
        ConfigurationLayoutProjectionContext context
    ) => BuildKeyringShimPlacement(context.Platform, GetProductDataRootForSingleSurface(context));

    private static FakeAdapterPlacement BuildKeyringShimPlacement(
        ConfigurationLayoutPlatform platform,
        string productDataRoot
    )
    {
        string placementRoot = Combine(platform, productDataRoot, "keyring-shim");
        return new FakeAdapterPlacement
        {
            Surface = FakeAdapterSurface.KeyringShim,
            PlacementRoot = placementRoot,
            ArtifactPath = Combine(
                platform,
                placementRoot,
                GetExecutableFileName(platform, "keyring")
            ),
        };
    }

    private static string GetProductDataRoot(ConfigurationLayoutProjectionContext context) =>
        ConfigurationLayoutProjector.ProjectKeyringShim(context).ProductDataRoot;

    private static List<string> CapturePosixDirectoriesToRestrict(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context
    )
    {
        if (context.Layout.Platform == ConfigurationLayoutPlatform.Windows)
        {
            return [];
        }

        bool restrictManagedDirectories = TryGetUnixExecutableFileMode(
            placement.Surface,
            context.Layout.Platform,
            out _
        );
        string targetDirectory =
            placement.ArtifactKind == FakeAdapterArtifactKind.Directory
                ? placement.ArtifactPath
                : GetContainingDirectory(context.Layout.Platform, placement.ArtifactPath);
        HashSet<string> managedDirectories = restrictManagedDirectories
            ? GetManagedPosixDirectoriesToRestrict(
                placement,
                context.Layout.Platform,
                targetDirectory
            )
            : new HashSet<string>(GetPathComparer(context.Layout.Platform));
        string? sharedProductDataRawLayoutRoot = IsSharedProductDataSurface(placement.Surface)
            ? GetProductDataRawLayoutRoot(context.Layout)
            : null;
        var directoriesToRestrict = new List<string>();
        var seen = new HashSet<string>(GetPathComparer(context.Layout.Platform));
        FileSystemOwner currentOwner = context.FileSystem.GetCurrentOwner();
        foreach (string directory in EnumeratePathChain(context.Layout.Platform, targetDirectory))
        {
            if (IsRootPath(context.Layout.Platform, directory))
            {
                continue;
            }

            bool directoryExists = context.FileSystem.DirectoryExists(directory);
            bool shouldRestrictManagedDirectory =
                managedDirectories.Contains(directory)
                && (!directoryExists || context.FileSystem.GetOwner(directory) == currentOwner);
            bool shouldRestrictNewSharedDirectory =
                !directoryExists
                && IsPathOnOrWithinRootPathChain(
                    context.Layout.Platform,
                    sharedProductDataRawLayoutRoot,
                    directory
                );
            if (
                shouldRestrictManagedDirectory || shouldRestrictNewSharedDirectory
            )
            {
                AppendUniquePath(directoriesToRestrict, seen, directory);
            }
        }

        return directoriesToRestrict;
    }

    private static void EnsureTrustedPosixParentDirectoriesForExecutableMaterialization(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context,
        bool requiresTrustedPosixParentValidation
    )
    {
        if (!requiresTrustedPosixParentValidation)
        {
            return;
        }

        _ = context.FileSystem.CaptureTrustedParentDirectorySnapshots(placement.ArtifactPath);
    }

    private static void
    EnsurePreexistingPosixParentDirectoriesForExecutableMaterializationAreSafeToMutate(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context,
        bool requiresTrustedPosixParentValidation
    )
    {
        if (!requiresTrustedPosixParentValidation)
        {
            return;
        }

        string targetDirectory =
            placement.ArtifactKind == FakeAdapterArtifactKind.Directory
                ? placement.ArtifactPath
                : GetContainingDirectory(context.Layout.Platform, placement.ArtifactPath);
        HashSet<string> selfHealableManagedDirectories = GetManagedPosixDirectoriesToRestrict(
            placement,
            context.Layout.Platform,
            targetDirectory
        );
        FileSystemOwner currentOwner = context.FileSystem.GetCurrentOwner();
        foreach (string directory in EnumeratePathChain(context.Layout.Platform, targetDirectory))
        {
            if (!context.FileSystem.DirectoryExists(directory))
            {
                continue;
            }

            FileSystemOwner owner = context.FileSystem.GetOwner(directory);
            bool currentUserOwned = owner == currentOwner;
            if (!IsTrustedPosixExecutableParentOwner(owner, currentOwner))
            {
                throw new UnauthorizedAccessException(
                    $"The trusted parent directory '{directory}' must be owned by a trusted owner."
                );
            }

            UnixFileMode unixFileMode = context.FileSystem.GetUnixFileMode(directory);
            if (!HasUnsafeTrustedPosixParentDirectoryUnixFileMode(unixFileMode))
            {
                continue;
            }

            if (currentUserOwned && selfHealableManagedDirectories.Contains(directory))
            {
                continue;
            }

            throw new UnauthorizedAccessException(
                "The helper parent directory "
                    + $"'{directory}' must not be writable by group or other users."
            );
        }
    }

    private static bool IsTrustedPosixExecutableParentOwner(
        FileSystemOwner owner,
        FileSystemOwner currentOwner
    ) =>
        owner == currentOwner
        || string.Equals(owner.Id, "unix:0", StringComparison.Ordinal)
        || string.Equals(owner.Id, "fake:root", StringComparison.Ordinal)
        || string.Equals(owner.Id, "fake:system", StringComparison.Ordinal);

    private static bool HasUnsafeTrustedPosixParentDirectoryUnixFileMode(UnixFileMode mode)
    {
        const UnixFileMode unsafeWriteBits = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
        return (mode & unsafeWriteBits) != 0;
    }

    private static HashSet<string> GetManagedPosixDirectoriesToRestrict(
        FakeAdapterPlacement placement,
        ConfigurationLayoutPlatform platform,
        string targetDirectory
    )
    {
        var directories = new HashSet<string>(GetPathComparer(platform));
        string managedRoot = GetContainingDirectory(platform, placement.PlacementRoot);
        foreach (string directory in EnumeratePathChain(platform, targetDirectory))
        {
            if (IsRootPath(platform, directory))
            {
                continue;
            }

            if (IsPathWithinRoot(platform, managedRoot, directory))
            {
                directories.Add(directory);
            }
        }

        return directories;
    }

    private static bool IsPathOnOrWithinRootPathChain(
        ConfigurationLayoutPlatform platform,
        string? rootPath,
        string candidatePath
    ) =>
        !string.IsNullOrWhiteSpace(rootPath)
        && (
            IsPathWithinRoot(platform, rootPath, candidatePath)
            || IsPathWithinRoot(platform, candidatePath, rootPath)
        );

    private static void RestrictPosixDirectoryModes(
        FakeAdapterMaterializationContext context,
        IEnumerable<string> directories
    )
    {
        if (context.Layout.Platform == ConfigurationLayoutPlatform.Windows)
        {
            return;
        }

        IFakeAdapterScaffoldMaterializationFileSystem scaffoldFileSystem =
            GetFakeAdapterScaffoldMaterializationFileSystem(context);
        foreach (string directory in directories)
        {
            scaffoldFileSystem.SetUnixFileModeNoFollow(directory, OwnerOnlyDirectoryMode);
        }
    }

    private static FakeAdapterPlacement ValidatePlacementForMaterialization(
        FakeAdapterPlacement placement,
        ConfigurationLayoutProjectionContext context
    )
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(context);

        FakeAdapterPlacement projectedPlacement = ProjectPlacement(placement.Surface, context);
        EnsurePlacementPathsDoNotContainUnsupportedPosixBackslashes(
            placement,
            context.Platform
        );
        EnsurePlacementPathsAreSafeForMaterialization(projectedPlacement, context.Platform);
        if (
            placement.ArtifactKind != projectedPlacement.ArtifactKind
            || !PathsEqual(
                context.Platform,
                placement.PlacementRoot,
                projectedPlacement.PlacementRoot
            )
            || !PathsEqual(
                context.Platform,
                placement.ArtifactPath,
                projectedPlacement.ArtifactPath
            )
        )
        {
            throw new ArgumentException(
                "Fake adapter materialization requires the projected placement for the requested "
                    + "surface and layout.",
                nameof(placement)
            );
        }

        EnsureArtifactPathIsWithinPlacementRoot(projectedPlacement, context.Platform);
        return projectedPlacement;
    }

    private static void EnsureRelevantLayoutRootsAreSafeForMaterialization(
        FakeAdapterSurface surface,
        ConfigurationLayoutProjectionContext context
    ) =>
        EnsureLayoutRootIsSafeForMaterialization(
            context.Platform,
            GetRelevantRawLayoutRoot(surface, context)
        );

    private static string? GetRelevantRawLayoutRoot(
        FakeAdapterSurface surface,
        ConfigurationLayoutProjectionContext context
    ) =>
        surface switch
        {
            FakeAdapterSurface.NuGetNetCorePlugin => context.HomeDirectory,
            FakeAdapterSurface.GitHelper
            or FakeAdapterSurface.PythonKeyringBackend
            or FakeAdapterSurface.PythonKeyringHelper
            or FakeAdapterSurface.KeyringShim => GetProductDataRawLayoutRoot(context),
            _ => throw new InvalidOperationException("Unknown fake adapter surface."),
        };

    private static FakeAdapterPlacement[] GetProbeablePlacements(
        FakeAdapterDiscoveryContext context
    )
    {
        var placements = new List<FakeAdapterPlacement>();

        foreach (FakeAdapterSurface surface in Enum.GetValues<FakeAdapterSurface>())
        {
            try
            {
                placements.Add(ProjectPlacementForSafeProbe(surface, context));
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        return [.. placements];
    }

    private static FakeAdapterPlacement ProjectPlacementForSafeProbe(
        FakeAdapterSurface surface,
        FakeAdapterDiscoveryContext context
    )
    {
        EnsureRelevantLayoutRootsAreSafeForMaterialization(surface, context.Layout);
        FakeAdapterPlacement placement = ProjectPlacement(surface, context.Layout);
        EnsurePlacementPathsAreSafeForMaterialization(placement, context.Layout.Platform);
        EnsureArtifactPathIsWithinPlacementRoot(placement, context.Layout.Platform);
        EnsurePlacementPathsAreSafeForProbe(placement, context);
        return placement;
    }

    private static void EnsurePlacementPathsAreSafeForProbe(
        FakeAdapterPlacement placement,
        FakeAdapterDiscoveryContext context
    )
    {
        EnsurePlacementPathsAreFullyQualifiedForProbe(placement, context);
        EnsureSafeProbeExistenceSupport(context);
        EnsureSafeProbeTopologySupport(context);

        foreach (
            string path in EnumerateMaterializationSafetyPaths(placement, context.Layout.Platform)
        )
        {
            if (IsUnsupportedLinkOrReparsePoint(context, path))
            {
                throw new NotSupportedException(
                    "Fake adapter discovery rejects symbolic-link or reparse-point placement "
                        + "paths."
                );
            }
        }
    }

    private static void EnsurePlacementPathsAreFullyQualifiedForProbe(
        FakeAdapterPlacement placement,
        FakeAdapterDiscoveryContext context
    )
    {
        EnsurePathIsFullyQualifiedForProbe(context, placement.PlacementRoot);
        EnsurePathIsFullyQualifiedForProbe(context, placement.ArtifactPath);
    }

    private static void EnsurePathIsFullyQualifiedForProbe(
        FakeAdapterDiscoveryContext context,
        string path
    )
    {
        Func<string, bool>? isPathFullyQualified = context.IsPathFullyQualified;
        if (isPathFullyQualified is null)
        {
            throw new NotSupportedException(
                "Fake adapter discovery requires file-system path semantics probe support."
            );
        }

        try
        {
            if (isPathFullyQualified(path))
            {
                return;
            }
        }
        catch (NotSupportedException)
        {
            // Treat unsupported roots under the active probe path semantics as an unsafe
            // host/layout mismatch and fail closed before topology or existence probes.
        }

        throw new NotSupportedException(
            "Fake adapter discovery requires file-system path semantics that match the "
                + "requested layout platform before topology or existence probes."
        );
    }

    private static void EnsureSafeProbeExistenceSupport(FakeAdapterDiscoveryContext context)
    {
        if (context.FileExists is null)
        {
            throw new NotSupportedException(
                "Fake adapter discovery requires file-existence probe support."
            );
        }

        if (context.DirectoryExists is null)
        {
            throw new NotSupportedException(
                "Fake adapter discovery requires directory-existence probe support."
            );
        }
    }

    private static void EnsureSafeProbeTopologySupport(FakeAdapterDiscoveryContext context)
    {
        if (context.IsSymbolicLink is null)
        {
            throw new NotSupportedException(
                "Fake adapter discovery requires symbolic-link topology probe support."
            );
        }

        if (
            context.Layout.Platform == ConfigurationLayoutPlatform.Windows
            && context.IsReparsePoint is null
        )
        {
            throw new NotSupportedException(
                "Fake adapter discovery on Windows requires reparse-point topology probe "
                    + "support."
            );
        }
    }

    private static string? GetProductDataRawLayoutRoot(
        ConfigurationLayoutProjectionContext context
    ) =>
        context.Platform switch
        {
            ConfigurationLayoutPlatform.Windows => context.LocalAppDataDirectory,
            ConfigurationLayoutPlatform.Linux => string.IsNullOrWhiteSpace(
                context.XdgDataHomeDirectory
            )
                ? context.HomeDirectory
                : context.XdgDataHomeDirectory,
            ConfigurationLayoutPlatform.MacOs => context.HomeDirectory,
            _ => throw new ArgumentOutOfRangeException(
                nameof(context),
                context.Platform,
                "Unsupported layout projection platform."
            ),
        };

    private static bool IsSharedProductDataSurface(FakeAdapterSurface surface) =>
        surface
            is FakeAdapterSurface.NuGetNetCorePlugin
                or FakeAdapterSurface.GitHelper
                or FakeAdapterSurface.PythonKeyringBackend
                or FakeAdapterSurface.PythonKeyringHelper
                or FakeAdapterSurface.KeyringShim;

    private static void EnsureLayoutRootIsSafeForMaterialization(
        ConfigurationLayoutPlatform platform,
        string? path
    )
    {
        if (platform == ConfigurationLayoutPlatform.Windows)
        {
            EnsureOptionalWindowsLayoutRootIsSafe(path);
            return;
        }

        EnsureOptionalPathDoesNotContainUnsupportedPosixBackslashes(platform, path);
    }

    private static void EnsureRealFileSystemMaterializationIsRejected(
        FakeAdapterMaterializationContext context
    )
    {
        if (context.FileSystem is IFakeAdapterScaffoldMaterializationFileSystem)
        {
            return;
        }

        string fileSystemTypeName =
            context.FileSystem.GetType().FullName ?? context.FileSystem.GetType().Name;
        throw new NotSupportedException(
            "Fake adapter scaffold materialization only allows explicitly opted-in fake file "
                + $"systems; '{fileSystemTypeName}' is not permitted."
        );
    }

    private static IFakeAdapterScaffoldMaterializationFileSystem
        GetFakeAdapterScaffoldMaterializationFileSystem(
            FakeAdapterMaterializationContext context
        )
    {
        EnsureRealFileSystemMaterializationIsRejected(context);
        return (IFakeAdapterScaffoldMaterializationFileSystem)context.FileSystem;
    }

    private static void EnsureConditionalFileMutationSupportForScaffoldCreation(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context
    )
    {
        if (context.FileSystem.SupportsConditionalFileMutations)
        {
            return;
        }

        throw new NotSupportedException(
            "Fake adapter scaffold materialization requires conditional file mutation support "
                + $"to safely create projected placement '{placement.Surface}'."
        );
    }

    private static void EnsureLayoutRootsDoNotContainUnsupportedPosixBackslashes(
        ConfigurationLayoutProjectionContext context
    )
    {
        if (context.Platform == ConfigurationLayoutPlatform.Windows)
        {
            return;
        }

        EnsureOptionalPathDoesNotContainUnsupportedPosixBackslashes(
            context.Platform,
            context.HomeDirectory
        );
        if (context.Platform == ConfigurationLayoutPlatform.Linux)
        {
            EnsureOptionalPathDoesNotContainUnsupportedPosixBackslashes(
                context.Platform,
                context.XdgDataHomeDirectory
            );
        }
    }

    private static void EnsureLayoutRootsAreSafeForMaterialization(
        ConfigurationLayoutProjectionContext context
    )
    {
        EnsureLayoutRootsDoNotContainUnsupportedPosixBackslashes(context);
        if (context.Platform != ConfigurationLayoutPlatform.Windows)
        {
            return;
        }

        EnsureOptionalWindowsLayoutRootIsSafe(context.HomeDirectory);
        EnsureOptionalWindowsLayoutRootIsSafe(context.LocalAppDataDirectory);
    }

    private static void EnsureOptionalWindowsLayoutRootIsSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        EnsurePathIsSafeForMaterialization(ConfigurationLayoutPlatform.Windows, path);
    }

    private static void EnsureOptionalPathDoesNotContainUnsupportedPosixBackslashes(
        ConfigurationLayoutPlatform platform,
        string? path
    )
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            EnsurePathDoesNotContainUnsupportedPosixBackslashes(platform, path);
        }
    }

    private static void EnsurePlacementPathsAreSafeForMaterialization(
        FakeAdapterPlacement placement,
        ConfigurationLayoutPlatform platform
    )
    {
        EnsurePathIsSafeForMaterialization(platform, placement.PlacementRoot);
        EnsurePathIsSafeForMaterialization(platform, placement.ArtifactPath);
    }

    private static void EnsurePlacementPathsDoNotContainUnsupportedPosixBackslashes(
        FakeAdapterPlacement placement,
        ConfigurationLayoutPlatform platform
    )
    {
        EnsurePathDoesNotContainUnsupportedPosixBackslashes(platform, placement.PlacementRoot);
        EnsurePathDoesNotContainUnsupportedPosixBackslashes(platform, placement.ArtifactPath);
    }

    private static void EnsurePlacementPathsAreFullyQualifiedForFileSystem(
        IEnumerable<FakeAdapterPlacement> placements,
        FakeAdapterMaterializationContext context
    )
    {
        foreach (FakeAdapterPlacement placement in placements)
        {
            EnsurePathIsFullyQualifiedForFileSystem(context, placement.PlacementRoot);
            EnsurePathIsFullyQualifiedForFileSystem(context, placement.ArtifactPath);
        }
    }

    private static void EnsurePathIsFullyQualifiedForFileSystem(
        FakeAdapterMaterializationContext context,
        string path
    )
    {
        try
        {
            if (context.FileSystem.IsPathFullyQualified(path))
            {
                return;
            }
        }
        catch (NotSupportedException)
        {
            // Treat unsupported roots under the active file-system semantics as an unsafe
            // host/layout mismatch and fail closed before mutation.
        }

        throw new NotSupportedException(
            "Fake adapter materialization requires file-system path semantics that match the "
                + "requested layout platform before real filesystem mutation."
        );
    }

    private static void EnsurePathIsSafeForMaterialization(
        ConfigurationLayoutPlatform platform,
        string path
    )
    {
        EnsurePathDoesNotContainUnsupportedPosixBackslashes(platform, path);

        string normalizedPath = NormalizePathForComparison(platform, path);
        if (platform == ConfigurationLayoutPlatform.Windows)
        {
            EnsureWindowsPathHasSupportedFullyQualifiedDriveRoot(
                normalizedPath,
                "Fake adapter materialization on Windows requires drive-qualified absolute paths "
                    + "and rejects UNC or other non-drive-qualified roots.",
                "Fake adapter materialization on Windows requires fully qualified drive-rooted "
                    + "absolute paths and rejects drive-relative or bare-drive roots."
            );
        }
        else if (GetPathRoot(platform, normalizedPath).Length == 0)
        {
            throw new NotSupportedException(
                "Fake adapter materialization requires rooted placement paths."
            );
        }

        if (PathContainsUnsafeMaterializationComponent(platform, normalizedPath))
        {
            throw new NotSupportedException(
                platform == ConfigurationLayoutPlatform.Windows
                    ? "Fake adapter materialization rejects placement paths containing '.' or '..' "
                        + "path components or unsafe Windows path components such as trailing "
                        + "spaces or periods, reserved DOS device names, or colons outside the "
                        + "drive specifier."
                    : "Fake adapter materialization rejects placement paths containing '.' or '..' "
                        + "path components."
            );
        }
    }

    private static void EnsureWindowsPathHasSupportedFullyQualifiedDriveRoot(
        string normalizedPath,
        string unsupportedRootMessage,
        string nonFullyQualifiedMessage
    )
    {
        if (HasUnsupportedWindowsRoot(normalizedPath))
        {
            throw new NotSupportedException(unsupportedRootMessage);
        }

        if (GetPathRoot(ConfigurationLayoutPlatform.Windows, normalizedPath).Length == 0)
        {
            throw new NotSupportedException(nonFullyQualifiedMessage);
        }
    }

    private static void EnsurePathDoesNotContainUnsupportedPosixBackslashes(
        ConfigurationLayoutPlatform platform,
        string path
    )
    {
        if (platform != ConfigurationLayoutPlatform.Windows && path.Contains('\\'))
        {
            throw new NotSupportedException(
                "Fake adapter materialization on POSIX rejects placement paths containing "
                    + "backslashes because '\\' is a valid file-name character."
            );
        }
    }

    private static void EnsureMaterializationPathCanBeSafelyMutated(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context
    )
    {
        if (
            context.Layout.Platform == ConfigurationLayoutPlatform.Windows
            && context.FileSystem is not IFileSystemReparsePointSafety
        )
        {
            throw new NotSupportedException(
                "Fake adapter materialization on Windows requires reparse-point safety support."
            );
        }

        foreach (
            string path in EnumerateMaterializationSafetyPaths(placement, context.Layout.Platform)
        )
        {
            if (IsUnsupportedLinkOrReparsePoint(context.FileSystem, path))
            {
                throw new NotSupportedException(
                    "Fake adapter materialization rejects symbolic-link or reparse-point "
                        + "placement paths."
                );
            }
        }
    }

    private static void EnsureExecutableMaterializationPlacementsHaveSafePreexistingTrustedParents(
        IEnumerable<FakeAdapterPlacement> placements,
        FakeAdapterMaterializationContext context
    )
    {
        foreach (FakeAdapterPlacement placement in placements)
        {
            EnsurePreexistingPosixParentDirectoriesForExecutableMaterializationAreSafeToMutate(
                placement,
                context,
                TryGetUnixExecutableFileMode(
                    placement.Surface,
                    context.Layout.Platform,
                    out _
                )
            );
        }
    }

    private static bool MaterializationPathContainsUnsupportedLinkOrReparsePoint(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context
    ) =>
        EnumerateMaterializationSafetyPaths(placement, context.Layout.Platform).Any(path =>
            IsUnsupportedLinkOrReparsePoint(context.FileSystem, path)
        );

    private static void EnsureMaterializationPathsCanBeSafelyMutated(
        IEnumerable<FakeAdapterPlacement> placements,
        FakeAdapterMaterializationContext context
    )
    {
        foreach (FakeAdapterPlacement placement in placements)
        {
            EnsureMaterializationPathCanBeSafelyMutated(placement, context);
        }
    }

    private static void EnsureMaterializationPlacementsDoNotHaveWrongKindConflicts(
        IEnumerable<FakeAdapterPlacement> placements,
        FakeAdapterMaterializationContext context
    )
    {
        FakeAdapterDiscoveryContext probeContext = CreateMaterializationProbeContext(context);

        foreach (FakeAdapterPlacement placement in placements)
        {
            EnsureMaterializationPlacementDoesNotHaveWrongKindConflicts(
                placement,
                probeContext,
                context.Layout.Platform
            );
        }
    }

    private static void EnsureMaterializationPlacementsDoNotOverwriteUnexpectedExistingFiles(
        IEnumerable<FakeAdapterPlacement> placements,
        FakeAdapterMaterializationContext context
    )
    {
        foreach (FakeAdapterPlacement placement in placements)
        {
            string deterministicContents = BuildDeterministicArtifactContents(
                placement,
                context.Layout.Platform
            );
            FakeAdapterExistingFileMaterializationState existingFileState =
                GetExistingFileMaterializationState(
                    placement,
                    context,
                    deterministicContents,
                    validateExecutableTrustedParentDirectories: false
                );
            EnsureExistingFileMaterializationStateIsAllowed(placement, existingFileState);
            if (
                placement.ArtifactKind == FakeAdapterArtifactKind.File
                && existingFileState == FakeAdapterExistingFileMaterializationState.Missing
            )
            {
                EnsureConditionalFileMutationSupportForScaffoldCreation(placement, context);
            }
        }
    }

    private static FakeAdapterDiscoveryContext CreateMaterializationProbeContext(
        FakeAdapterMaterializationContext context
    )
    {
        return new FakeAdapterDiscoveryContext
        {
            Layout = context.Layout,
            IsPathFullyQualified = context.FileSystem.IsPathFullyQualified,
            FileExists = context.FileSystem.FileExists,
            DirectoryExists = context.FileSystem.DirectoryExists,
        };
    }

    private static FakeAdapterExistingFileMaterializationState GetExistingFileMaterializationState(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context,
        string deterministicContents,
        bool validateExecutableTrustedParentDirectories = true
    )
    {
        if (
            placement.ArtifactKind != FakeAdapterArtifactKind.File
            || !context.FileSystem.FileExists(placement.ArtifactPath)
        )
        {
            return FakeAdapterExistingFileMaterializationState.Missing;
        }

        byte[] expectedContents = Utf8NoBom.GetBytes(deterministicContents);
        byte[] actualContents = context.FileSystem.ReadAllBytes(placement.ArtifactPath);
        if (!actualContents.SequenceEqual(expectedContents))
        {
            return FakeAdapterExistingFileMaterializationState.MismatchedContents;
        }

        if (
            TryGetUnixExecutableFileMode(
                placement.Surface,
                context.Layout.Platform,
                out UnixFileMode expectedMode
            )
        )
        {
            if (context.FileSystem.GetUnixFileMode(placement.ArtifactPath) != expectedMode)
            {
                return FakeAdapterExistingFileMaterializationState.MismatchedExecutableMode;
            }

            return GetExistingExactFakeArtifactIntegrityState(
                placement,
                context,
                expectedContents,
                expectedMode,
                validateExecutableTrustedParentDirectories
            );
        }

        return FakeAdapterExistingFileMaterializationState.MatchesExpectedFakeArtifact;
    }

    private static FakeAdapterExistingFileMaterializationState
        GetExistingExactFakeArtifactIntegrityState(
            FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context,
        byte[] expectedContents,
        UnixFileMode expectedMode,
        bool validateTrustedParentDirectories
    )
    {
        FileIntegritySnapshot integritySnapshot = validateTrustedParentDirectories
            ? context.FileSystem.CaptureFileIntegritySnapshot(placement.ArtifactPath)
            : GetFakeAdapterScaffoldMaterializationFileSystem(context)
                .CaptureFileIntegritySnapshotWithoutTrustedParents(placement.ArtifactPath);
        if (!integritySnapshot.Sha256Hash.SequenceEqual(SHA256.HashData(expectedContents)))
        {
            return FakeAdapterExistingFileMaterializationState.MismatchedContents;
        }

        return integritySnapshot.UnixFileMode == expectedMode
            ? FakeAdapterExistingFileMaterializationState.MatchesExpectedFakeArtifact
            : FakeAdapterExistingFileMaterializationState.MismatchedExecutableMode;
    }

    private static void EnsureExistingFileStillMatchesExpectedFakeArtifactBeforeIdempotentReturn(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context,
        string deterministicContents
    )
    {
        FakeAdapterExistingFileMaterializationState existingFileState =
            GetExistingFileMaterializationState(placement, context, deterministicContents);
        if (
            existingFileState
            == FakeAdapterExistingFileMaterializationState.MatchesExpectedFakeArtifact
        )
        {
            EnsureNonSnapshotExistingFileStillHasSafeNoFollowMaterializationPath(
                placement,
                context,
                deterministicContents
            );
            return;
        }

        ThrowIdempotentExistingFileRevalidationFailure(placement, context, existingFileState);
    }

    private static void EnsureNonSnapshotExistingFileStillHasSafeNoFollowMaterializationPath(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context,
        string deterministicContents
    )
    {
        if (
            placement.ArtifactKind != FakeAdapterArtifactKind.File
            || TryGetUnixExecutableFileMode(placement.Surface, context.Layout.Platform, out _)
        )
        {
            return;
        }

        for (int validationReadPass = 0; validationReadPass < 3; validationReadPass++)
        {
            EnsureMaterializationPathCanBeSafelyMutated(placement, context);
            FakeAdapterExistingFileMaterializationState existingFileState =
                GetExistingFileMaterializationState(placement, context, deterministicContents);
            if (
                existingFileState
                != FakeAdapterExistingFileMaterializationState.MatchesExpectedFakeArtifact
            )
            {
                ThrowIdempotentExistingFileRevalidationFailure(
                    placement,
                    context,
                    existingFileState
                );
            }
        }

        EnsureMaterializationPathCanBeSafelyMutated(placement, context);
    }

    private static void ThrowIdempotentExistingFileRevalidationFailure(
        FakeAdapterPlacement placement,
        FakeAdapterMaterializationContext context,
        FakeAdapterExistingFileMaterializationState existingFileState
    )
    {
        if (existingFileState == FakeAdapterExistingFileMaterializationState.Missing)
        {
            if (context.FileSystem.DirectoryExists(placement.ArtifactPath))
            {
                throw new InvalidOperationException(
                    "Fake adapter materialization rejects projected placement "
                        + $"'{placement.Surface}' "
                        + $"because file '{placement.ArtifactPath}' no longer has the expected "
                        + "file kind after idempotent validation."
                );
            }

            throw new InvalidOperationException(
                $"Fake adapter materialization rejects projected placement '{placement.Surface}' "
                    + $"because file '{placement.ArtifactPath}' no longer exists after idempotent "
                    + "validation."
            );
        }

        EnsureExistingFileMaterializationStateIsAllowed(placement, existingFileState);
        throw new InvalidOperationException(
            "Fake adapter materialization failed to revalidate the existing exact fake artifact."
        );
    }

    private static void EnsureExistingFileMaterializationStateIsAllowed(
        FakeAdapterPlacement placement,
        FakeAdapterExistingFileMaterializationState existingFileState
    )
    {
        switch (existingFileState)
        {
            case FakeAdapterExistingFileMaterializationState.Missing:
            case FakeAdapterExistingFileMaterializationState.MatchesExpectedFakeArtifact:
                return;
            case FakeAdapterExistingFileMaterializationState.MismatchedContents:
                throw new InvalidOperationException(
                    "Fake adapter materialization rejects projected placement "
                        + $"'{placement.Surface}' "
                        + $"because file '{placement.ArtifactPath}' already exists with non-"
                        + "scaffold contents."
                );
            case FakeAdapterExistingFileMaterializationState.MismatchedExecutableMode:
                throw new InvalidOperationException(
                    "Fake adapter materialization rejects projected placement "
                        + $"'{placement.Surface}' "
                        + $"because file '{placement.ArtifactPath}' already exists but does not "
                        + "have the expected scaffold executable mode."
                );
            default:
                throw new InvalidOperationException(
                    "Unknown fake adapter existing-file materialization state."
                );
        }
    }

    private static void EnsureExistingFileRemovalStateIsAllowed(
        FakeAdapterPlacement placement,
        FakeAdapterExistingFileMaterializationState existingFileState
    )
    {
        switch (existingFileState)
        {
            case FakeAdapterExistingFileMaterializationState.Missing:
            case FakeAdapterExistingFileMaterializationState.MatchesExpectedFakeArtifact:
                return;
            case FakeAdapterExistingFileMaterializationState.MismatchedContents:
                throw new InvalidOperationException(
                    "Fake adapter removal rejects projected placement "
                        + $"'{placement.Surface}' "
                        + $"because file '{placement.ArtifactPath}' has non-scaffold contents."
                );
            case FakeAdapterExistingFileMaterializationState.MismatchedExecutableMode:
                throw new InvalidOperationException(
                    "Fake adapter removal rejects projected placement "
                        + $"'{placement.Surface}' "
                        + $"because file '{placement.ArtifactPath}' does not have the expected "
                        + "scaffold executable mode."
                );
            default:
                throw new InvalidOperationException(
                    "Unknown fake adapter existing-file materialization state."
                );
        }
    }

    private static void EnsureMaterializationPlacementDoesNotHaveWrongKindConflicts(
        FakeAdapterPlacement placement,
        FakeAdapterDiscoveryContext probeContext,
        ConfigurationLayoutPlatform platform
    )
    {
        foreach (string path in EnumerateMaterializationSafetyPaths(placement, platform))
        {
            FakeAdapterArtifactKind expectedKind =
                placement.ArtifactKind == FakeAdapterArtifactKind.File
                    && PathsEqual(platform, path, placement.ArtifactPath)
                    ? FakeAdapterArtifactKind.File
                    : FakeAdapterArtifactKind.Directory;
            FakeAdapterProbeResult probeResult = ProbePlacement(
                new FakeAdapterPlacement
                {
                    Surface = placement.Surface,
                    PlacementRoot = placement.PlacementRoot,
                    ArtifactPath = path,
                    ArtifactKind = expectedKind,
                },
                probeContext
            );

            if (probeResult.Status != FakeAdapterProbeStatus.WrongKind)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Fake adapter materialization rejects projected placement '{placement.Surface}' "
                    + $"because path '{probeResult.ArtifactPath}' has the wrong kind for "
                    + $"materialization (expected {probeResult.ExpectedKind}, actual "
                    + $"{probeResult.ActualKind})."
            );
        }
    }

    private static List<string> EnumerateMaterializationSafetyPaths(
        FakeAdapterPlacement placement,
        ConfigurationLayoutPlatform platform
    )
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(GetPathComparer(platform));

        AppendPathChain(paths, seen, placement.PlacementRoot, platform);
        switch (placement.ArtifactKind)
        {
            case FakeAdapterArtifactKind.File:
                AppendPathChain(
                    paths,
                    seen,
                    GetContainingDirectory(platform, placement.ArtifactPath),
                    platform
                );
                AppendUniquePath(
                    paths,
                    seen,
                    NormalizePathForComparison(platform, placement.ArtifactPath)
                );
                break;
            case FakeAdapterArtifactKind.Directory:
                AppendPathChain(paths, seen, placement.ArtifactPath, platform);
                break;
        }

        return paths;
    }

    private static void AppendPathChain(
        List<string> paths,
        HashSet<string> seen,
        string path,
        ConfigurationLayoutPlatform platform
    )
    {
        foreach (string directory in EnumeratePathChain(platform, path))
        {
            AppendUniquePath(paths, seen, directory);
        }
    }

    private static void AppendUniquePath(
        List<string> paths,
        HashSet<string> seen,
        string path
    )
    {
        if (seen.Add(path))
        {
            paths.Add(path);
        }
    }

    private static IEnumerable<string> EnumeratePathChain(
        ConfigurationLayoutPlatform platform,
        string path
    )
    {
        string normalizedPath = NormalizePathForComparison(platform, path);
        string root = GetPathRoot(platform, normalizedPath);
        if (root.Length == 0)
        {
            yield break;
        }

        yield return root;
        if (PathsEqual(platform, normalizedPath, root))
        {
            yield break;
        }

        char separator = platform == ConfigurationLayoutPlatform.Windows ? '\\' : '/';
        int componentStart = root.Length;
        while (componentStart < normalizedPath.Length)
        {
            int separatorIndex = normalizedPath.IndexOf(separator, componentStart);
            if (separatorIndex < 0)
            {
                yield return normalizedPath;
                yield break;
            }

            yield return normalizedPath[..separatorIndex];
            componentStart = separatorIndex + 1;
        }
    }

    private static bool IsUnsupportedLinkOrReparsePoint(IFileSystem fileSystem, string path)
    {
        try
        {
            if (fileSystem.IsSymbolicLink(path))
            {
                return true;
            }

            return fileSystem is IFileSystemReparsePointSafety reparsePointSafety
                && reparsePointSafety.IsReparsePoint(path);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static bool IsUnsupportedLinkOrReparsePoint(
        FakeAdapterDiscoveryContext context,
        string path
    )
    {
        try
        {
            if ((context.IsSymbolicLink?.Invoke(path)).GetValueOrDefault())
            {
                return true;
            }

            return (context.IsReparsePoint?.Invoke(path)).GetValueOrDefault();
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new NotSupportedException(
                "Fake adapter discovery rejects symbolic-link or reparse-point placement "
                    + "paths.",
                exception
            );
        }
        catch (IOException exception)
        {
            throw new NotSupportedException(
                "Fake adapter discovery rejects symbolic-link or reparse-point placement "
                    + "paths.",
                exception
            );
        }
    }

    private static void EnsureArtifactPathIsWithinPlacementRoot(
        FakeAdapterPlacement placement,
        ConfigurationLayoutPlatform platform
    )
    {
        if (!IsPathWithinRoot(platform, placement.PlacementRoot, placement.ArtifactPath))
        {
            throw new InvalidOperationException(
                "Fake adapter artifact paths must remain within their placement root."
            );
        }
    }

    private static bool IsPathWithinRoot(
        ConfigurationLayoutPlatform platform,
        string rootPath,
        string candidatePath
    )
    {
        string normalizedRoot = NormalizePathForComparison(platform, rootPath);
        string normalizedCandidate = NormalizePathForComparison(platform, candidatePath);
        if (PathsEqual(platform, normalizedRoot, normalizedCandidate))
        {
            return true;
        }

        char separator = platform == ConfigurationLayoutPlatform.Windows ? '\\' : '/';
        string prefix = IsRootPath(platform, normalizedRoot)
            ? normalizedRoot
            : string.Concat(normalizedRoot, separator);
        return normalizedCandidate.StartsWith(prefix, GetPathComparison(platform));
    }

    private static bool IsRootPath(ConfigurationLayoutPlatform platform, string path)
    {
        return platform == ConfigurationLayoutPlatform.Windows
            ? path.Length == 3
                && IsWindowsDriveLetter(path[0])
                && path[1] == ':'
                && (path[2] == '\\' || path[2] == '/')
            : path == "/";
    }

    private static bool PathsEqual(
        ConfigurationLayoutPlatform platform,
        string left,
        string right
    ) =>
        string.Equals(
            NormalizePathForComparison(platform, left),
            NormalizePathForComparison(platform, right),
            GetPathComparison(platform)
        );

    private static StringComparer GetPathComparer(ConfigurationLayoutPlatform platform) =>
        platform == ConfigurationLayoutPlatform.Windows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static StringComparison GetPathComparison(ConfigurationLayoutPlatform platform) =>
        platform == ConfigurationLayoutPlatform.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string NormalizePathForComparison(
        ConfigurationLayoutPlatform platform,
        string path
    )
    {
        char separator = platform == ConfigurationLayoutPlatform.Windows ? '\\' : '/';
        string normalizedPath = path.Replace(AlternateSeparator(separator), separator);
        if (
            platform == ConfigurationLayoutPlatform.Windows
            && normalizedPath.Length >= 2
            && IsWindowsDriveLetter(normalizedPath[0])
            && normalizedPath[1] == ':'
        )
        {
            normalizedPath = CollapseRepeatedWindowsSeparatorsAfterDriveSpecifier(normalizedPath);
        }
        else if (platform != ConfigurationLayoutPlatform.Windows)
        {
            normalizedPath = CollapseRepeatedPosixSeparators(normalizedPath);
        }

        return IsRootPath(platform, normalizedPath)
            ? normalizedPath
            : TrimTrailingSeparators(normalizedPath, separator);
    }

    private static string CollapseRepeatedWindowsSeparatorsAfterDriveSpecifier(string path)
    {
        var normalizedPath = new StringBuilder(path.Length);
        normalizedPath.Append(char.ToUpperInvariant(path[0]));
        normalizedPath.Append(':');

        bool previousWasSeparator = false;
        for (int index = 2; index < path.Length; index++)
        {
            char currentCharacter = path[index];
            if (currentCharacter == '\\')
            {
                if (previousWasSeparator)
                {
                    continue;
                }

                previousWasSeparator = true;
                normalizedPath.Append(currentCharacter);
                continue;
            }

            previousWasSeparator = false;
            normalizedPath.Append(currentCharacter);
        }

        return normalizedPath.ToString();
    }

    private static string CollapseRepeatedPosixSeparators(string path)
    {
        var normalizedPath = new StringBuilder(path.Length);
        bool previousWasSeparator = false;
        foreach (char currentCharacter in path)
        {
            if (currentCharacter == '/')
            {
                if (previousWasSeparator)
                {
                    continue;
                }

                previousWasSeparator = true;
                normalizedPath.Append(currentCharacter);
                continue;
            }

            previousWasSeparator = false;
            normalizedPath.Append(currentCharacter);
        }

        return normalizedPath.ToString();
    }

    private static bool PathContainsUnsafeMaterializationComponent(
        ConfigurationLayoutPlatform platform,
        string path
    )
    {
        int componentStart = GetPathRoot(platform, path).Length;
        for (int index = componentStart; index <= path.Length; index++)
        {
            if (index < path.Length && !IsDirectorySeparator(platform, path[index]))
            {
                continue;
            }

            int componentLength = index - componentStart;
            if (IsUnsafeMaterializationComponent(platform, path, componentStart, componentLength))
            {
                return true;
            }

            componentStart = index + 1;
        }

        return false;
    }

    private static bool IsUnsafeMaterializationComponent(
        ConfigurationLayoutPlatform platform,
        string path,
        int componentStart,
        int componentLength
    )
    {
        if (componentLength == 0)
        {
            return false;
        }

        if (IsCurrentOrParentDirectoryComponent(path, componentStart, componentLength))
        {
            return true;
        }

        return platform == ConfigurationLayoutPlatform.Windows
            && !FoundationArtifactPath.IsSafeWindowsPathSegment(
                path.Substring(componentStart, componentLength)
            );
    }

    private static bool IsCurrentOrParentDirectoryComponent(
        string path,
        int componentStart,
        int componentLength
    ) =>
        componentLength == 1 && path[componentStart] == '.'
        || componentLength == 2
            && path[componentStart] == '.'
            && path[componentStart + 1] == '.';

    private static bool IsDirectorySeparator(ConfigurationLayoutPlatform platform, char value) =>
        platform == ConfigurationLayoutPlatform.Windows ? value is '\\' or '/' : value == '/';

    private static string GetPathRoot(ConfigurationLayoutPlatform platform, string path)
    {
        if (platform == ConfigurationLayoutPlatform.Windows)
        {
            return path.Length >= 3
                    && IsWindowsDriveLetter(path[0])
                    && path[1] == ':'
                    && path[2] == '\\'
                ? string.Concat(char.ToUpperInvariant(path[0]), ":\\")
                : string.Empty;
        }

        return path.Length > 0 && path[0] == '/' ? "/" : string.Empty;
    }

    private static bool IsWindowsDriveLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool HasUnsupportedWindowsRoot(string path) =>
        path.Length > 0 && path[0] == '\\';

    private static string BuildDeterministicArtifactContents(
        FakeAdapterPlacement placement,
        ConfigurationLayoutPlatform platform
    ) =>
        string.Join(
            '\n',
            [
                "fake-adapter-scaffold-version=1",
                $"surface={placement.Surface}",
                $"platform={platform}",
                $"placement-root={
                    NormalizePathForDeterministicArtifactContents(
                        platform,
                        placement.PlacementRoot
                    )
                }",
                $"artifact-path={
                    NormalizePathForDeterministicArtifactContents(
                        platform,
                        placement.ArtifactPath
                    )
                }",
                $"artifact-kind={placement.ArtifactKind}",
                $"unix-executable-intent={
                    TryGetUnixExecutableFileMode(
                        placement.Surface,
                        platform,
                        out _
                    )
                }",
            ]
        ) + "\n";

    private static string ComputeSha256(byte[] value)
    {
        byte[] hash = SHA256.HashData(value);
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }

    private static string NormalizePathForDeterministicArtifactContents(
        ConfigurationLayoutPlatform platform,
        string path
    )
    {
        string normalizedPath = NormalizePathForComparison(platform, path);
        return platform == ConfigurationLayoutPlatform.Windows && normalizedPath.Length > 0
            ? string.Concat(normalizedPath[0], normalizedPath[1..].ToLowerInvariant())
            : normalizedPath;
    }

    private static string Combine(
        ConfigurationLayoutPlatform platform,
        string firstSegment,
        params string[] additionalSegments
    )
    {
        char separator = platform == ConfigurationLayoutPlatform.Windows ? '\\' : '/';
        string result = TrimTrailingSeparators(firstSegment, separator);
        foreach (string segment in additionalSegments)
        {
            string trimmedSegment = TrimSeparators(segment, separator);
            result =
                result.Length == 1 && result[0] == separator
                    ? string.Concat(result, trimmedSegment)
                    : string.Concat(result, separator, trimmedSegment);
        }

        return result;
    }

    private static string GetContainingDirectory(
        ConfigurationLayoutPlatform platform,
        string path
    )
    {
        char separator = platform == ConfigurationLayoutPlatform.Windows ? '\\' : '/';
        int separatorIndex = path.LastIndexOf(separator);
        return separatorIndex < 0
            ? path
            : separatorIndex == 0
                ? path[..1]
                : path[..separatorIndex];
    }

    private static string GetExecutableFileName(
        ConfigurationLayoutPlatform platform,
        string baseFileName
    ) =>
        platform == ConfigurationLayoutPlatform.Windows
            ? string.Concat(baseFileName, ".exe")
            : baseFileName;

    private static bool TryGetUnixExecutableFileMode(
        FakeAdapterSurface surface,
        ConfigurationLayoutPlatform platform,
        out UnixFileMode mode
    )
    {
        if (
            platform != ConfigurationLayoutPlatform.Windows
            && surface
                is FakeAdapterSurface.GitHelper
                    or FakeAdapterSurface.PythonKeyringHelper
                    or FakeAdapterSurface.KeyringShim
        )
        {
            mode = OwnerExecutableFileMode;
            return true;
        }

        mode = default;
        return false;
    }

    private static string GetPythonSitePackagesDirectoryName(
        ConfigurationLayoutPlatform platform
    ) =>
        platform == ConfigurationLayoutPlatform.Windows
            ? Combine(platform, "Lib", PythonSitePackagesDirectoryName)
            : Combine(platform, "lib", PythonSitePackagesDirectoryName);

    private static string GetProductDataRootForSingleSurface(
        ConfigurationLayoutProjectionContext context
    ) =>
        context.Platform switch
        {
            ConfigurationLayoutPlatform.Windows => Combine(
                context.Platform,
                RequireNonEmpty(
                    context.LocalAppDataDirectory,
                    nameof(context.LocalAppDataDirectory)
                ),
                WindowsProductCompanyDirectoryName,
                WindowsProductNameDirectoryName
            ),
            ConfigurationLayoutPlatform.Linux => Combine(
                context.Platform,
                GetXdgDataHomeForSingleSurface(context),
                ProductDirectoryName
            ),
            ConfigurationLayoutPlatform.MacOs => Combine(
                context.Platform,
                RequireNonEmpty(context.HomeDirectory, nameof(context.HomeDirectory)),
                "Library",
                "Application Support",
                WindowsProductCompanyDirectoryName,
                WindowsProductNameDirectoryName
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(context),
                context.Platform,
                "Unsupported layout projection platform."
            ),
        };

    private static string GetNuGetNetCorePluginPlacementRootForSingleSurface(
        ConfigurationLayoutProjectionContext context
    ) =>
        Combine(
            context.Platform,
            RequireNonEmpty(context.HomeDirectory, nameof(context.HomeDirectory)),
            ".nuget",
            "plugins",
            "netcore",
            ProductDirectoryName
        );

    private static string GetXdgDataHomeForSingleSurface(
        ConfigurationLayoutProjectionContext context
    ) =>
        NullIfWhiteSpace(context.XdgDataHomeDirectory)
        ?? Combine(
            context.Platform,
            RequireNonEmpty(context.HomeDirectory, nameof(context.HomeDirectory)),
            ".local",
            "share"
        );

    private static string TrimSeparators(string value, char separator) =>
        value.Trim(separator, AlternateSeparator(separator));

    private static string TrimTrailingSeparators(string value, char separator)
    {
        string trimmed = value.TrimEnd(separator, AlternateSeparator(separator));
        return trimmed.Length == 0 ? value : trimmed;
    }

    private static char AlternateSeparator(char separator) => separator == '\\' ? '/' : '\\';

    private static string RequireNonEmpty(string? value, string parameterName) =>
        NullIfWhiteSpace(value)
        ?? throw new ArgumentException(
            "Layout projection path inputs must be non-empty.",
            parameterName
        );

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static void ValidateSurface(FakeAdapterSurface surface)
    {
        if (!Enum.IsDefined(surface))
        {
            throw new ArgumentOutOfRangeException(nameof(surface), surface, "Unknown surface.");
        }
    }

    private enum FakeAdapterExistingFileMaterializationState
    {
        Missing,
        MatchesExpectedFakeArtifact,
        MismatchedContents,
        MismatchedExecutableMode,
    }
}

internal sealed record FakeAdapterDiscoveryContext
{
    public required ConfigurationLayoutProjectionContext Layout { get; init; }
    public Func<string, bool>? IsPathFullyQualified { get; init; }
    public Func<string, bool>? FileExists { get; init; }
    public Func<string, bool>? DirectoryExists { get; init; }
    public Func<string, bool>? IsSymbolicLink { get; init; }
    public Func<string, bool>? IsReparsePoint { get; init; }
}

internal sealed record FakeAdapterMaterializationContext
{
    public required ConfigurationLayoutProjectionContext Layout { get; init; }
    public required IFileSystem FileSystem { get; init; }
}

internal sealed record FakeAdapterPlacement
{
    public required FakeAdapterSurface Surface { get; init; }
    public required string PlacementRoot { get; init; }
    public required string ArtifactPath { get; init; }
    public FakeAdapterArtifactKind ArtifactKind { get; init; } = FakeAdapterArtifactKind.File;
}

internal sealed record FakeAdapterProbeResult
{
    public required FakeAdapterSurface Surface { get; init; }
    public required string PlacementRoot { get; init; }
    public required string ArtifactPath { get; init; }
    public required FakeAdapterArtifactKind ExpectedKind { get; init; }
    public required FakeAdapterArtifactKind ActualKind { get; init; }
    public required FakeAdapterProbeStatus Status { get; init; }
}

internal enum FakeAdapterSurface
{
    GitHelper,
    NuGetNetCorePlugin,
    PythonKeyringBackend,
    PythonKeyringHelper,
    KeyringShim,
}

internal enum FakeAdapterArtifactKind
{
    Missing = 0,
    File = 1,
    Directory = 2,
}

internal enum FakeAdapterProbeStatus
{
    Missing,
    Found,
    WrongKind,
}
