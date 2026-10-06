using Microsoft.Build.Execution;

namespace WorkflowDelivery.Repository;

// These concrete repository operations are adapted; SDK/package targets remain native.
internal sealed class DotNetRepositoryTargets
{
    private static readonly StringComparer PhysicalPaths = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly (string Origin, string Name)[] Supported =
    [
        ("src/public/lib/CircularList/CircularList.csproj", "SetPackageReleaseNotes"),
        ("src/public/lib/Hjg.Pngcs/Hjg.Pngcs.csproj", "SetPackageReleaseNotes"),
        ("src/public/lib/Memoization/Memoization.csproj", "SetPackageReleaseNotes"),
        ("src/public/lib/MicrosoftExtensions.Logging.MSTest/" +
            "MicrosoftExtensions.Logging.MSTest.csproj",
            "SetPackageReleaseNotes"),
        ("src/public/lib/MicrosoftExtensions.Logging.Xunit/" +
            "MicrosoftExtensions.Logging.Xunit.csproj",
            "SetPackageReleaseNotes"),
        ("src/public/lib/MicrosoftExtensions.Options.DedupChangeExtensions/" +
            "MicrosoftExtensions.Options.DedupChangeExtensions.csproj",
            "SetPackageReleaseNotes"),
        ("src/public/lib/PhiFailureDetector/PhiFailureDetector.csproj", "SetPackageReleaseNotes"),
        ("src/public/lib/WebHdfs.Extensions.FileProviders/WebHdfs.Extensions.FileProviders.csproj",
            "SetPackageReleaseNotes"),
        ("src/private/app/workflow-delivery/WorkflowDelivery.csproj", "ReferenceOfficialNbgv"),
        ("tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj",
            "ReferenceOfficialNbgv"),
        ("tests/public/lib/WebHdfs.Extensions.FileProviders.UnitTest/" +
            "WebHdfs.Extensions.FileProviders.UnitTest.csproj",
            "PreBuild"),
        ("dirs.proj", "RestoreWindowsOnlyProjectsOnNonWindows"),
        ("src/public/lib/hcoona-release-smoke-github-packages/" +
            "hcoona-release-smoke-github-packages.csproj",
            "ValidateWorkflowDeliveryFrozenInputs"),
    ];
    private readonly HashSet<string> _committed;
    private readonly (string Origin, string Name)[] _supported;

    internal DotNetRepositoryTargets(string root, string[] committedPaths)
    {
        _committed = new(committedPaths.Select(Path.GetFullPath), PhysicalPaths);
        _supported = Supported.Select(pair => (Path.GetFullPath(Path.Combine(root,
            pair.Origin.Replace('/', Path.DirectorySeparatorChar))), pair.Name)).ToArray();
    }

    internal void Validate(ProjectInstance project,
        Func<string, InvalidDataException> unavailable)
    {
        foreach (ProjectTargetInstance target in project.Targets.Values)
        {
            string origin = target.Location.File;
            if (!Path.IsPathFullyQualified(origin))
                throw unavailable(
                    $"Target {target.Name} has no absolute native definition origin.");
            origin = Path.GetFullPath(origin);
            // Being under the endpoint (including obj) does not imply repository ownership.
            if (!_committed.Contains(origin)) continue;
            if (!_supported.Any(pair => PhysicalPaths.Equals(pair.Origin, origin) &&
                StringComparer.OrdinalIgnoreCase.Equals(pair.Name, target.Name)))
                throw unavailable($"Unsupported effective repository producer " +
                    $"{target.Name}, origin {origin}; a reviewed operation adapter is required.");
        }
    }
}
