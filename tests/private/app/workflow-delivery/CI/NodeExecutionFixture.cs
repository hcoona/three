using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

internal sealed class NodeExecutionFixture(GitFixture repository, CiPlan plan) : IDisposable
{
    internal const string Source = "src/product";
    internal const string Build = "node/project-build-v1";
    internal const string Test = "node/project-test-v1";
    internal const string Pack = "node/npm-artifact-v1";
    internal const string Contents = "node/npm-artifact-contents-v1";
    internal const string Consumer = "node/npm-install-import-v1";
    internal const string Metadata = """
        {"name":"@hcoona/hcoona-release-smoke-npm","version":"1.2.3",
         "type":"module","main":"./dist/index.js"}
        """;
    internal GitFixture Repository { get; } = repository;
    internal CiPlan Plan { get; } = plan;
    internal string Scratch { get; } = Directory.CreateTempSubdirectory(
        "workflow-node-execution-").FullName;
    internal string Runtime { get; set; } = "node24";
    internal string NodeVersion { get; set; } = "24.21.0";
    internal string PnpmVersion { get; set; } = "12.8.2";
    internal CiPlan SelectedPlan => Plan with
    {
        Checks = Plan.Checks.Where(item =>
        item.Work.Key.Variant == Runtime).ToArray()
    };
    internal NodeRunRequest Request => new(Repository.Directory, Scratch, Runtime);
    internal List<NativeCommand> Commands { get; } = [];
    internal string NativeMetadata { get; set; } = Metadata;
    internal Func<NativeCommand, NativeCommandResult?>? Override { get; set; }
    internal Action<string>? WriteArchive { get; set; }

    internal static async Task<NodeExecutionFixture> CreateAsync(CancellationToken token,
        params string[] builds)
    {
        GitFixture repository = await GitFixture.CreateAsync(token);
        try
        {
            await repository.SetAsync(Source + "/package.json", Metadata);
            await repository.SetAsync(Source + "/index.js", "export const source = true;");
            string candidate = await repository.CommitAsync();
            await repository.GitAsync("reset", "--hard", candidate);
            return new(repository, CreatePlan(candidate, builds));
        }
        catch
        {
            repository.Dispose();
            throw;
        }
    }

    internal static CiPlan CreatePlan(string candidate, params string[] builds)
    {
        if (builds.Length == 0)
            builds = ["main"];
        var project = NodeScenario.Project(Source);
        CheckSpec[] source = NodeQualityChecks.Expand(new(NodeScenario.Preset,
            Source + "/workflow-delivery.quality.yml"), NodeScenario.Scripts(project));
        CheckSpec[] packages = NodePackageChecks.Expand(NodeScenario.Unit("product",
            builds.Select(build => (build, Source)).ToArray()),
            new Dictionary<string, PnpmProject> { [Source] = project },
            new Dictionary<string, string> { [Source] = "1.2.3" });
        CiPlan original = new(candidate, candidate, NodeFactsAssembler.Scope,
            source.Concat(packages).Select(check => new PlannedCheck(check,
                [NodeScenario.Preset], [new(Source + "/index.js", candidate, Source)])).ToArray());
        return new NodeRuntimeInputs(candidate,
            [new("node22", "22.x", null), new("node24", "24.x", "24.21.0")],
            "12.8.2", []).Expand(original);
    }

    internal Task<NodeRunResult> RunAsync(CancellationToken token, CiPlan? plan = null,
        NodeRunRequest? request = null) => NodeExecution.RunAsync(plan ?? Plan,
            request ?? Request, ExecuteAsync, token);

    internal Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Commands.Add(command);
        if (Override?.Invoke(command) is { } result)
            return Task.FromResult(result);
        if (command.Arguments.Contains("pack"))
        {
            string path = command.Arguments[^1];
            if (WriteArchive is { } write)
                write(path);
            else
                PackageArchiveFixture.Write(path, PackageArchiveFixture.Required());
        }
        return Task.FromResult(Success(command.Arguments.Contains("--version")
            ? command.Executable == "node" ? "v" + NodeVersion + "\n" : PnpmVersion + "\n"
            : command.Arguments.Contains("pkg") ? NativeMetadata : ""));
    }

    internal static NativeCommandResult Success(string stdout = "") =>
        new(NativeTermination.Exited, 0, stdout, "", 0.25, null);

    internal static NativeCommandResult Failure(NativeTermination termination =
        NativeTermination.Exited, int? code = 7) =>
        new(termination, code, "partial stdout", "visible failure", 0.5, "native failure");

    public void Dispose()
    {
        Repository.Dispose();
        Directory.Delete(Scratch, recursive: true);
    }
}

internal sealed record ArchiveMember(string Name, byte[] Bytes,
    TarEntryType Kind = TarEntryType.RegularFile);

internal static class PackageArchiveFixture
{
    internal static ArchiveMember[] Required() =>
        [new("package/package.json", Encoding.UTF8.GetBytes(NodeExecutionFixture.Metadata)),
            new("package/dist/index.js", Encoding.UTF8.GetBytes(
                "export const smokeMessage = () => 'hcoona-release-smoke-npm';"))];

    internal static void Write(string path, IEnumerable<ArchiveMember> members,
        TarEntryFormat format = TarEntryFormat.Pax, long trailingExpandedBytes = 0)
    {
        using var output = File.Create(path);
        using var gzip = new GZipStream(output, CompressionLevel.Fastest);
        using (var writer = new TarWriter(gzip, format, leaveOpen: true))
        {
            foreach (ArchiveMember member in members)
            {
                TarEntryType kind = format == TarEntryFormat.V7 &&
                    member.Kind == TarEntryType.RegularFile
                    ? TarEntryType.V7RegularFile : member.Kind;
                TarEntry entry = format switch
                {
                    TarEntryFormat.V7 => new V7TarEntry(kind, member.Name),
                    TarEntryFormat.Ustar => new UstarTarEntry(kind, member.Name),
                    _ => new PaxTarEntry(kind, member.Name),
                };
                using var data = new MemoryStream(member.Bytes, writable: false);
                if (kind is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                    entry.DataStream = data;
                else if (kind is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                    entry.LinkName = "../../outside";
                writer.WriteEntry(entry);
            }
        }
        byte[] trailing = new byte[64 * 1024];
        while (trailingExpandedBytes > 0)
        {
            int count = (int)Math.Min(trailing.Length, trailingExpandedBytes);
            gzip.Write(trailing, 0, count);
            trailingExpandedBytes -= count;
        }
    }
}
