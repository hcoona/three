using System.Xml.Linq;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Release;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Ecosystems;

[TestClass]
public sealed class NuGetReleaseUnitsTests
{
    public static IEnumerable<object[]> Units => ReleaseUnitCatalog.NuGetUnits
        .Select(unit => new object[] { unit.Id });

    // CI discovery joins each committed declaration to its exact native project and package
    // identity.
    [TestMethod]
    [DynamicData(nameof(Units))]
    public void RegisteredDeclarationsResolveToTheirCommittedProjects(string id)
    {
        NuGetReleaseUnit registered = ReleaseUnitCatalog.NuGet(id);
        string path = Path.Combine(RepositoryToolSelection.SourceRoot, registered.Declaration);
        ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(File.ReadAllText(path),
            registered.Declaration);

        Assert.AreEqual(id, unit.Id);
        ReleaseBuild build = Assert.ContainsSingle(unit.Builds);
        Assert.AreEqual("dotnet/nuget-package-v1", build.Definition);
        Assert.AreEqual("nuget-package", Assert.ContainsSingle(build.Outputs).Kind);
        string project = Path.Combine(RepositoryToolSelection.SourceRoot,
            DotNetReleaseUnits.Entry(unit, build));
        Assert.IsTrue(File.Exists(project), project);
        if (!registered.Library) return;
        Assert.IsTrue(registered.Declaration.StartsWith("src/public/lib/",
            StringComparison.Ordinal));
        var properties = new XElement("Properties", XDocument.Load(project).Root!
            .Elements("PropertyGroup").SelectMany(group => group.Elements()));
        Assert.AreEqual(registered.PackageId, properties.Element("PackageId")?.Value);
        Assert.AreEqual(registered.AssemblyName, properties.Element("AssemblyName")?.Value ??
            Path.GetFileNameWithoutExtension(project));
        Assert.IsTrue(registered.Source.AllowsChannel("official", dryRun: false));
    }

    [TestMethod]
    public void AdmissionBindsUnitDeclarationChannelAndDestination()
    {
        NuGetReleaseUnit unit = ReleaseUnitCatalog.NuGet("memoization");
        var buddy = new ReleaseSubject(new string('a', 40), "refs/heads/feature", unit.Id,
            unit.Declaration, "buddy", "github-packages-nuget", true);
        var official = buddy with
        {
            SelectedRef = "refs/heads/main",
            Channel = "official",
            Destination = "nugetorg"
        };
        Assert.AreEqual(unit.Id, ReleaseUnitCatalog.AdmitNuGet(buddy).Id);
        Assert.AreEqual(unit.Id, ReleaseUnitCatalog.AdmitNuGet(official).Id);

        foreach (ReleaseSubject rejected in new[]
        {
            buddy with { Declaration = ReleaseUnitCatalog.NuGet("circular-list").Declaration },
            buddy with { Destination = "nugetorg" },
            official with { Destination = "github-packages-nuget" },
            buddy with { Unit = "Memoization.Net" },
            official with { Unit = "hcoona-release-smoke-github-packages",
                Declaration = ReleaseUnitCatalog.NuGet("hcoona-release-smoke-github-packages")
                    .Declaration },
        })
            Assert.ThrowsExactly<InvalidDataException>(() =>
                ReleaseUnitCatalog.AdmitNuGet(rejected));
    }

    [TestMethod]
    public void StaticPythonVersioningIsReservedForNbgvPython()
    {
        ReleaseUnitDeclaration invalid = ReleaseUnitDeclarationReader.Read("""
            schema: workflow-delivery/v3/release-unit
            release-unit: other-python-lib
            builds:
              - id: python-distributions
                definition: python/distribution-set-v1
                entry-point: pyproject.toml
                qualification:
                  static-version: true
                outputs:
                  - {id: wheel, role: primary-package, kind: python-wheel}
            """, "src/public/lib/other-python-lib/workflow-delivery.release-unit.yml");
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseUnitCatalog.ToPython(invalid));
    }

    [TestMethod]
    public void BrowserStoreRejectsLiveBuddyAndPythonResolvesNestedEntryPoints()
    {
        ReleaseUnitDeclaration omittedChannels = ReleaseUnitDeclarationReader.Read("""
            schema: workflow-delivery/v3/release-unit
            release-unit: sample-extension
            builds:
              - id: extension
                definition: node/browser-extension-v1
                entry-point: package.json
                qualification:
                  manifest-name: Sample
                outputs:
                  - {id: chrome-zip, role: primary-package, kind: chrome-extension-zip}
            """, "src/public/lib/sample-extension/workflow-delivery.release-unit.yml");
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseUnitCatalog.ToBrowserStore(omittedChannels));

        BrowserStoreUnit browser = ReleaseUnitCatalog.BrowserStore("steam-account-history-to-csv");
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseUnitCatalog.AdmitBrowserStore(
            new(new('a', 40), "refs/heads/feature", browser.Id, browser.Declaration,
                "buddy", "chrome-web-store", DryRun: false)));

        ReleaseUnitDeclaration nestedPython = ReleaseUnitDeclarationReader.Read("""
            schema: workflow-delivery/v3/release-unit
            release-unit: nested-pkg
            builds:
              - id: python-distributions
                definition: python/distribution-set-v1
                entry-point: nested/pyproject.toml
                outputs:
                  - {id: wheel, role: primary-package, kind: python-wheel}
            """, "src/public/lib/nested-pkg/workflow-delivery.release-unit.yml");
        Assert.AreEqual("src/public/lib/nested-pkg/nested",
            ReleaseUnitCatalog.ToPython(nestedPython).ProjectDirectory);

        ReleaseUnitDeclaration nestedNuGet = ReleaseUnitDeclarationReader.Read("""
            schema: workflow-delivery/v3/release-unit
            release-unit: nested-nuget
            builds:
              - id: nuget-package
                definition: dotnet/nuget-package-v1
                entry-point: nested/NestedLib.csproj
                outputs:
                  - {id: nuget-package, role: primary-package, kind: nuget-package}
            """, "src/public/lib/nested-nuget/workflow-delivery.release-unit.yml");
        var manifests = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/public/lib/nested-nuget/nested/NestedLib.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\..\SharedGen\SharedGen.csproj" />
                  </ItemGroup>
                </Project>
                """,
        };
        NuGetReleaseUnit resolvedNuGet = ReleaseUnitCatalog.ToNuGet(nestedNuGet, manifests);
        Assert.AreEqual("src/public/lib/nested-nuget/nested/NestedLib.csproj",
            resolvedNuGet.ProjectPath);
        Assert.AreEqual("src/public/lib/SharedGen",
            Assert.ContainsSingle(resolvedNuGet.CompanionDirectories));
    }
}
