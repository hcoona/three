using System.Xml.Linq;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Release;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Ecosystems;

[TestClass]
public sealed class NuGetReleaseUnitsTests
{
    public static IEnumerable<object[]> Units => NuGetReleaseUnits.All
        .Select(unit => new object[] { unit.Id });

    // CI discovery joins each committed declaration to its exact native project, and the
    // shared recipe relies on the project's static package identity and framework set.
    [TestMethod]
    [DynamicData(nameof(Units))]
    public void RegisteredDeclarationsResolveToTheirCommittedProjects(string id)
    {
        NuGetReleaseUnit registered = NuGetReleaseUnits.Named(id);
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
        Assert.AreEqual(string.Join(';', registered.Frameworks),
            properties.Element("TargetFrameworks")?.Value);
        Assert.AreEqual(registered.AssemblyName, properties.Element("AssemblyName")?.Value ??
            Path.GetFileNameWithoutExtension(project));
        Assert.IsTrue(registered.Official);
    }

    [TestMethod]
    public void AdmissionBindsUnitDeclarationChannelAndDestination()
    {
        NuGetReleaseUnit unit = NuGetReleaseUnits.Named("memoization");
        var buddy = new ReleaseSubject(new string('a', 40), "refs/heads/feature", unit.Id,
            unit.Declaration, "buddy", "github-packages-nuget", true);
        var official = buddy with { SelectedRef = "refs/heads/main", Channel = "official",
            Destination = "nugetorg" };
        Assert.AreEqual(unit, NuGetReleaseUnits.Admit(buddy));
        Assert.AreEqual(unit, NuGetReleaseUnits.Admit(official));

        foreach (ReleaseSubject rejected in new[]
        {
            buddy with { Declaration = NuGetReleaseUnits.Named("circular-list").Declaration },
            buddy with { Destination = "nugetorg" },
            official with { Destination = "github-packages-nuget" },
            buddy with { Unit = "Memoization.Net" },
            official with { Unit = NuGetReleaseUnits.Smoke,
                Declaration = NuGetReleaseUnits.Named(NuGetReleaseUnits.Smoke).Declaration },
        })
            Assert.ThrowsExactly<InvalidDataException>(() => NuGetReleaseUnits.Admit(rejected));
    }
}
