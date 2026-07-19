using System.Xml.Linq;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class ProjectBoundaryTests
{
    private static readonly string[] ExpectedLibraryFiles =
    [
        "AtlasDiscovery.cs",
        "AtlasIntakeContracts.cs",
        "EmptyAtlasSurvey.cs",
        "Hcoona.CelesphoniaModifier.Atlas.csproj",
        "LocatorSegmentRedactor.cs",
        "PrivateArtifactLifecycle.cs",
        "packages.lock.json",
        "TrustedLocalCopy.cs",
    ];

    private static readonly string[] ExpectedCliFiles =
    [
        "AtlasCliApplication.cs",
        "AtlasCliOperations.cs",
        "Hcoona.CelesphoniaModifier.Atlas.Cli.csproj",
        "packages.lock.json",
        "Program.cs",
    ];

    private static readonly string[] ExpectedTestFiles =
    [
        "AtlasCliApplicationTests.cs",
        "AtlasDiscoveryTests.cs",
        "AtlasIntakeContractTests.cs",
        "AtlasProcessSmokeTests.cs",
        "EmptyAtlasSurveyTests.cs",
        "Hcoona.CelesphoniaModifier.Atlas.Tests.csproj",
        "LocatorSegmentRedactorTests.cs",
        "packages.lock.json",
        "PrivateArtifactLifecycleTests.cs",
        "ProjectBoundaryTests.cs",
        "TrustedLocalCopyTests.cs",
    ];

    private static readonly string[] ExpectedSchemaFiles =
    [
        "agent-egress-envelope.schema.json",
        "cleanup-preflight-report.schema.json",
        "copy-plan.schema.json",
        "copy-receipt.schema.json",
        "corpus-intake-manifest.schema.json",
        "intake-state.schema.json",
        "private-artifact-inventory.schema.json",
        "preservation-snapshot-manifest.schema.json",
        "source-root-map.schema.json",
        "test-data/",
        "test-data/agent-egress-envelope.invalid-attestation.json",
        "test-data/agent-egress-envelope.invalid-literal-key.json",
        "test-data/agent-egress-envelope.invalid-private-field.json",
        "test-data/agent-egress-envelope.invalid-survey-alias.json",
        "test-data/agent-egress-envelope.valid.json",
    ];

    private static readonly string[] ExpectedTestPackages =
    [
        "coverlet.collector",
        "Microsoft.NET.Test.Sdk",
        "Microsoft.Testing.Extensions.CodeCoverage",
        "Microsoft.Testing.Extensions.TrxReport",
        "xunit.runner.visualstudio",
        "xunit.v3.mtp-v2",
    ];

    private const string ExpectedCliProjectReference =
        @"$(AtlasCliRoot)\Hcoona.CelesphoniaModifier.Atlas.Cli.csproj";

    private const string ExpectedLibraryProjectReference =
        @"$(AtlasRoot)\Hcoona.CelesphoniaModifier.Atlas.csproj";

    private const string TelemetryHookId = "98058041-B5B6-4A75-9834-58E6DF796A22";

    [Fact]
    public void ProductionProjectDependenciesAreClosed()
    {
        ProjectPaths paths = ProjectPaths.Create();
        XDocument library = XDocument.Load(paths.LibraryProject);
        XDocument cli = XDocument.Load(paths.CliProject);

        Assert.Empty(GetIncludes(library, "PackageReference"));
        Assert.Empty(GetIncludes(library, "ProjectReference"));
        Assert.Empty(GetIncludes(cli, "PackageReference"));
        Assert.Equal(
            [@"$(AtlasProject)\Hcoona.CelesphoniaModifier.Atlas.csproj"],
            GetIncludes(cli, "ProjectReference"));
        Assert.Equal(
            @"..\Hcoona.CelesphoniaModifier.Atlas",
            GetPropertyValue(cli, "AtlasProject"));
    }

    [Fact]
    public void TestProjectDependenciesAreExact()
    {
        ProjectPaths paths = ProjectPaths.Create();
        XDocument tests = XDocument.Load(paths.TestProject);

        Assert.Equal(
            ExpectedTestPackages.Order(StringComparer.Ordinal),
            GetIncludes(tests, "PackageReference"));
        Assert.Equal(
            [
                ExpectedCliProjectReference,
                ExpectedLibraryProjectReference,
            ],
            GetIncludes(tests, "ProjectReference"));
        Assert.Equal(
            @"$(MSBuildThisFileDirectory)..\..\..\..\..",
            GetPropertyValue(tests, "TestProjectRoot"));
        Assert.Equal(
            @"$(TestProjectRoot)\src\private\app\celesphonia-modifier",
            GetPropertyValue(tests, "A1SourceRoot"));
        Assert.Equal(
            @"$(A1SourceRoot)\Hcoona.CelesphoniaModifier.Atlas",
            GetPropertyValue(tests, "AtlasRoot"));
        Assert.Equal(
            @"$(A1SourceRoot)\Hcoona.CelesphoniaModifier.Atlas.Cli",
            GetPropertyValue(tests, "AtlasCliRoot"));
        AssertPackageAssets(tests, "coverlet.collector");
        AssertPackageAssets(tests, "xunit.runner.visualstudio");
    }

    [Fact]
    public void ProjectFileManifestIsExact()
    {
        ProjectPaths paths = ProjectPaths.Create();

        Assert.Equal(
            ExpectedLibraryFiles.Order(StringComparer.Ordinal),
            GetBoundaryEntries(paths.LibraryDirectory));
        Assert.Equal(
            ExpectedCliFiles.Order(StringComparer.Ordinal),
            GetBoundaryEntries(paths.CliDirectory));
        Assert.Equal(
            ExpectedTestFiles.Order(StringComparer.Ordinal),
            GetBoundaryEntries(paths.TestDirectory));
        Assert.Equal(
            ExpectedSchemaFiles.Order(StringComparer.Ordinal),
            GetBoundaryEntries(paths.SchemaDirectory));
    }

    [Fact]
    public void BoundaryHelperIncludesNestedUnauthorizedEntries()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"{nameof(ProjectBoundaryTests)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "allowed.cs"), "// allowed");
            string nestedDirectory = Path.Combine(root, "nested");
            Directory.CreateDirectory(nestedDirectory);
            File.WriteAllText(Path.Combine(nestedDirectory, "blocked.cs"), "// blocked");

            Assert.Equal(
                ["allowed.cs", "nested/", "nested/blocked.cs"],
                GetBoundaryEntries(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void TestProjectRejectsTestingPlatformTelemetry()
    {
        XDocument tests = XDocument.Load(ProjectPaths.Create().TestProject);
        XElement removal = tests
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TestingPlatformBuilderHook"
                && element.Attribute("Remove") is not null);
        XElement guard = tests
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Target"
                && element.Attribute("Name")?.Value == "RejectTestingPlatformTelemetry");

        Assert.Equal(TelemetryHookId, removal.Attribute("Remove")?.Value);
        Assert.Equal("CoreCompile", guard.Attribute("BeforeTargets")?.Value);
        Assert.Contains(
            "Microsoft.Testing.Extensions.Telemetry",
            guard.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReadmeDescribesTheScaffoldedFoundation()
    {
        string readme = File.ReadAllText(ProjectPaths.Create().Readme);

        Assert.DoesNotContain(
            "No application project has been scaffolded here yet.",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "The A2 C# intake and safety harness extends the existing reusable library, thin CLI, "
            + "and test",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "copy qualification, lifecycle preflight, and synthetic validation",
            readme,
            StringComparison.Ordinal);
    }

    private static void AssertPackageAssets(XDocument project, string packageName)
    {
        XElement package = project
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "PackageReference"
                && StringComparer.Ordinal.Equals(
                    element.Attribute("Include")?.Value,
                    packageName));
        Assert.Equal("all", GetChildValue(package, "PrivateAssets"));
        Assert.Equal(
            "runtime; build; native; contentfiles; analyzers; buildtransitive",
            GetChildValue(package, "IncludeAssets"));
    }

    private static string GetChildValue(XElement element, string childName) =>
        element
            .Elements()
            .Single(child => child.Name.LocalName == childName)
            .Value;

    private static string GetPropertyValue(XDocument project, string propertyName) =>
        project
            .Descendants()
            .Single(element => element.Name.LocalName == propertyName)
            .Value;

    private static string[] GetBoundaryEntries(string directory)
    {
        List<string> entries = [];
        EnumerateBoundaryEntries(directory, directory, entries);
        return [.. entries.Order(StringComparer.Ordinal)];
    }

    private static void EnumerateBoundaryEntries(
        string root,
        string directory,
        List<string> entries)
    {
        foreach (string childDirectory in Directory.EnumerateDirectories(directory))
        {
            string directoryName = Path.GetFileName(childDirectory);
            if (StringComparer.OrdinalIgnoreCase.Equals(directoryName, "bin")
                || StringComparer.OrdinalIgnoreCase.Equals(directoryName, "obj"))
            {
                continue;
            }

            entries.Add(ToRepoRelativePath(root, childDirectory) + "/");
            EnumerateBoundaryEntries(root, childDirectory, entries);
        }

        foreach (string file in Directory.EnumerateFiles(directory))
        {
            entries.Add(ToRepoRelativePath(root, file));
        }
    }

    private static string ToRepoRelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string[] GetIncludes(XDocument project, string itemName) =>
        project
            .Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Order(StringComparer.Ordinal)
            .ToArray()!;

    private sealed record ProjectPaths(
        string LibraryDirectory,
        string CliDirectory,
        string TestDirectory,
        string LibraryProject,
        string CliProject,
        string TestProject,
        string Readme,
        string SchemaDirectory)
    {
        public static ProjectPaths Create()
        {
            string repositoryRoot = FindRepositoryRoot();
            string projectRoot = Path.Combine(
                repositoryRoot,
                "src",
                "private",
                "app",
                "celesphonia-modifier");
            string testRoot = Path.Combine(
                repositoryRoot,
                "tests",
                "private",
                "app",
                "celesphonia-modifier");
            string libraryDirectory = Path.Combine(
                projectRoot,
                "Hcoona.CelesphoniaModifier.Atlas");
            string cliDirectory = Path.Combine(
                projectRoot,
                "Hcoona.CelesphoniaModifier.Atlas.Cli");
            string testDirectory = Path.Combine(
                testRoot,
                "Hcoona.CelesphoniaModifier.Atlas.Tests");
            return new ProjectPaths(
                libraryDirectory,
                cliDirectory,
                testDirectory,
                Path.Combine(
                    libraryDirectory,
                    "Hcoona.CelesphoniaModifier.Atlas.csproj"),
                Path.Combine(
                    cliDirectory,
                    "Hcoona.CelesphoniaModifier.Atlas.Cli.csproj"),
                Path.Combine(
                    testDirectory,
                    "Hcoona.CelesphoniaModifier.Atlas.Tests.csproj"),
                Path.Combine(projectRoot, "docs", ".copilot", "README.md"),
                Path.Combine(projectRoot, "docs", ".copilot", "schemas", "atlas-v0"));
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "dirs.proj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Repository root was not found.");
        }
    }
}
