using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;

namespace Hcoona.AzureAuth.CredProvider.Platform.Installer;

internal static class FakeAdapterDiscoveryScaffold
{
    private const string GitHelperDirectoryName = "git-helper";
    private const string GitHelperFileName = "git-credential-azureauth-credprovider";
    private const string NuGetNetCorePluginEntrypointFileName = "azureauth-credprovider.dll";
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

        ConfigurationTargetLayoutProjection nuGetPluginLayout =
            ConfigurationLayoutProjector.ProjectNuGetPluginLayout(context);
        ConfigurationTargetLayoutProjection keyringShimLayout =
            ConfigurationLayoutProjector.ProjectKeyringShim(context);

        string productDataRoot = keyringShimLayout.ProductDataRoot;
        string pythonKeyringDataRoot = Combine(
            context.Platform,
            productDataRoot,
            PythonKeyringDirectoryName
        );
        string pythonKeyringBackendPlacementRoot = Combine(
            context.Platform,
            productDataRoot,
            PythonEnvironmentDirectoryName,
            FakePythonEnvironmentDirectoryName,
            GetPythonSitePackagesDirectoryName(context.Platform),
            PythonKeyringBackendRegistrationDirectoryName
        );

        return
        [
            new FakeAdapterPlacement
            {
                Surface = FakeAdapterSurface.GitHelper,
                PlacementRoot = Combine(context.Platform, productDataRoot, GitHelperDirectoryName),
                ArtifactPath = Combine(
                    context.Platform,
                    productDataRoot,
                    GitHelperDirectoryName,
                    GetExecutableFileName(context.Platform, GitHelperFileName)
                ),
            },
            new FakeAdapterPlacement
            {
                Surface = FakeAdapterSurface.NuGetNetCorePlugin,
                PlacementRoot = nuGetPluginLayout.TargetPath,
                ArtifactPath = Combine(
                    context.Platform,
                    nuGetPluginLayout.TargetPath,
                    NuGetNetCorePluginEntrypointFileName
                ),
            },
            new FakeAdapterPlacement
            {
                Surface = FakeAdapterSurface.PythonKeyringBackend,
                PlacementRoot = pythonKeyringBackendPlacementRoot,
                ArtifactPath = Combine(
                    context.Platform,
                    pythonKeyringBackendPlacementRoot,
                    PythonKeyringBackendRegistrationFileName
                ),
            },
            new FakeAdapterPlacement
            {
                Surface = FakeAdapterSurface.PythonKeyringHelper,
                PlacementRoot = pythonKeyringDataRoot,
                ArtifactPath = Combine(
                    context.Platform,
                    pythonKeyringDataRoot,
                    GetExecutableFileName(context.Platform, PythonKeyringHelperFileName)
                ),
            },
            new FakeAdapterPlacement
            {
                Surface = FakeAdapterSurface.KeyringShim,
                PlacementRoot = GetContainingDirectory(
                    context.Platform,
                    keyringShimLayout.TargetPath
                ),
                ArtifactPath = keyringShimLayout.TargetPath,
            },
        ];
    }

    public static FakeAdapterPlacement ProjectPlacement(
        FakeAdapterSurface surface,
        ConfigurationLayoutProjectionContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateSurface(surface);

        return ProjectPlacements(context).Single(placement => placement.Surface == surface);
    }

    public static IReadOnlyList<FakeAdapterProbeResult> ProbePlacements(
        FakeAdapterDiscoveryContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Layout);

        return ProjectPlacements(context.Layout)
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

        return ProbePlacement(ProjectPlacement(surface, context.Layout), context);
    }

    public static FakeAdapterProbeResult ProbePlacement(
        FakeAdapterPlacement placement,
        FakeAdapterDiscoveryContext context
    )
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.FileExists);
        ArgumentNullException.ThrowIfNull(context.DirectoryExists);

        FakeAdapterArtifactKind actualKind = placement.ArtifactKind switch
        {
            FakeAdapterArtifactKind.File => context.FileExists(placement.ArtifactPath)
                ? FakeAdapterArtifactKind.File
                : context.DirectoryExists(placement.ArtifactPath)
                    ? FakeAdapterArtifactKind.Directory
                    : FakeAdapterArtifactKind.Missing,
            FakeAdapterArtifactKind.Directory => context.DirectoryExists(placement.ArtifactPath)
                ? FakeAdapterArtifactKind.Directory
                : context.FileExists(placement.ArtifactPath)
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

    private static string GetPythonSitePackagesDirectoryName(
        ConfigurationLayoutPlatform platform
    ) =>
        platform == ConfigurationLayoutPlatform.Windows
            ? Combine(platform, "Lib", PythonSitePackagesDirectoryName)
            : Combine(platform, "lib", PythonSitePackagesDirectoryName);

    private static string TrimSeparators(string value, char separator) =>
        value.Trim(separator, AlternateSeparator(separator));

    private static string TrimTrailingSeparators(string value, char separator)
    {
        string trimmed = value.TrimEnd(separator, AlternateSeparator(separator));
        return trimmed.Length == 0 ? value : trimmed;
    }

    private static char AlternateSeparator(char separator) => separator == '\\' ? '/' : '\\';

    private static void ValidateSurface(FakeAdapterSurface surface)
    {
        if (!Enum.IsDefined(surface))
        {
            throw new ArgumentOutOfRangeException(nameof(surface), surface, "Unknown surface.");
        }
    }
}

internal sealed record FakeAdapterDiscoveryContext
{
    public required ConfigurationLayoutProjectionContext Layout { get; init; }
    public Func<string, bool> FileExists { get; init; } = static _ => false;
    public Func<string, bool> DirectoryExists { get; init; } = static _ => false;
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
