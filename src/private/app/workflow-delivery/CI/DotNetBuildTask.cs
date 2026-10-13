using System.Text.Json;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Graph;
using Microsoft.Build.Logging;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record DotNetBuildRequest(DotNetNodeIdentity[] Entries, string Destination);
internal sealed record DotNetBuildOutput(string Identity, string FullPath);
internal sealed record DotNetBuildEntryResult(DotNetNodeIdentity Identity, string Build,
    string GetTargetPath, DotNetBuildOutput[] Outputs);
internal sealed record DotNetBuildResponse(DotNetBuildEntryResult[] Entries);

// Loaded by the checkout's SDK, which owns graph evaluation and build scheduling.
public sealed class DotNetBuildTask : ITask
{
    public IBuildEngine BuildEngine { get; set; } = null!;
    public ITaskHost HostObject { get; set; } = null!;
    [Required]
    public string RequestFile { get; set; } = "";

    public bool Execute()
    {
        DotNetBuildRequest request = JsonSerializer.Deserialize(File.ReadAllText(RequestFile),
            TransferJson.Default.DotNetBuildRequest)
            ?? throw new InvalidDataException("Missing native build request.");
        using var collection = new ProjectCollection();
        var graph = new ProjectGraph(request.Entries.Select(entry =>
            new ProjectGraphEntryPoint(entry.Project, entry.Globals)), collection,
            (path, globals, projects) => new ProjectInstance(path, globals, null, projects));
        using var manager = new BuildManager();
        GraphBuildResult native = manager.Build(new BuildParameters(collection)
        {
            DisableInProcNode = true,
            EnableNodeReuse = false,
            MaxNodeCount = Environment.ProcessorCount,
            Loggers = [new ConsoleLogger(LoggerVerbosity.Minimal)],
        }, new GraphBuildRequestData(graph, ["Build", "GetTargetPath"]));
        DotNetBuildEntryResult[] entries = graph.EntryPointNodes.Select(node =>
        {
            var identity = new DotNetNodeIdentity(node.ProjectInstance.FullPath,
                new(node.ProjectInstance.GlobalProperties, StringComparer.Ordinal));
            native.ResultsByNode.TryGetValue(node, out BuildResult? result);
            TargetResult? build = null;
            TargetResult? path = null;
            result?.ResultsByTarget.TryGetValue("Build", out build);
            result?.ResultsByTarget.TryGetValue("GetTargetPath", out path);
            return new DotNetBuildEntryResult(identity, build?.ResultCode.ToString() ?? "Missing",
                path?.ResultCode.ToString() ?? "Missing", path?.Items.Select(item =>
                    new DotNetBuildOutput(item.ItemSpec, item.GetMetadata("FullPath"))).ToArray()
                    ?? []);
        }).ToArray();
        using var output = new FileStream(request.Destination, FileMode.CreateNew,
            FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, new DotNetBuildResponse(entries),
            TransferJson.Default.DotNetBuildResponse);
        return true;
    }
}
