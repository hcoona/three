using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

internal static class NodeScenario
{
    internal const string Basis = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string Candidate = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    internal const string Preset = "node/hcoona-release-smoke-npm-v1";
    internal const string Declaration = "workflow-delivery.quality.yml";

    internal static NodeRevisionInputs Inputs(string revision, params PnpmProject[] projects)
    {
        string[] directories = projects.Select(project => project.Directory).ToArray();
        string[] files = ["package.json", "pnpm-lock.yaml",
            .. directories.SelectMany(directory => new[] { directory + "/package.json",
                directory + "/index.js", directory + "/version.json",
                directory + "/" + Declaration })];
        return new(new(revision, files.Select(File).ToArray()),
            new([new(".", null, []), .. projects], []),
            directories.Select(directory => new NbgvInputs(revision, directory,
                [directory + "/version.json"], [directory + "/index.js"])).ToArray(),
            directories.ToDictionary(directory => directory,
                directory => (QualitySelection?)new(Preset, directory + "/" + Declaration)),
            [], []);
    }

    internal static PnpmProject Project(string directory, params string[] dependencies) =>
        new(directory, null, dependencies);

    internal static GitEntry File(string path) => new(path, "100644", "blob");

    internal static NodeRevisionInputs Units(NodeRevisionInputs inputs,
        params ReleaseUnitDeclaration[] units) => inputs with
        {
            Units = units,
            Revision = inputs.Revision with
            {
                Entries = inputs.Revision.Entries.Concat(
                    units.Select(unit => File(unit.SourcePath)))
                    .DistinctBy(entry => entry.Path).ToArray(),
            },
        };

    internal static ReleaseUnitDeclaration Unit(string id,
        params (string Build, string Directory)[] builds) =>
        new("workflow-delivery.release-unit.yml", id, builds.Select(build =>
            new ReleaseBuild(build.Build, "node/npm-package-v1", build.Directory + "/package.json",
                [new(build.Build + "-package", "primary-package", "npm-tarball")])).ToArray());

    internal static PnpmScripts Scripts(PnpmProject project) =>
        new(project.Directory, new Dictionary<string, string>
        {
            ["build"] = "opaque build",
            ["test"] = "opaque source test",
        });

    internal static Task<CiPlan> Plan(NodeRevisionInputs basis, NodeRevisionInputs candidate,
        params string[] paths) => NodeFactsAssembler.PlanAsync(basis, candidate, paths, false,
            (project, _) => Task.FromResult(Scripts(project)),
            (_, _) => Task.FromResult("1.2.3"), CancellationToken.None);
}
