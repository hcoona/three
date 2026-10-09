using System.Text.Json.Nodes;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.CI;

// Controlled native response; production continues to use the existing UV reader/parser.
internal static class PythonExecutionMetadata
{
    internal static NativeCommandResult Result(string checkout, PythonMetadataMember[] members)
    {
        var nodes = new JsonObject
        {
            ["workspace-context"] = new JsonObject
            { ["kind"] = "workspace", ["path"] = checkout, ["dependencies"] = new JsonArray() },
        };
        var membership = new JsonArray();
        foreach (PythonMetadataMember member in members)
        {
            string path = Path.GetFullPath(Path.Combine(checkout, member.Directory));
            nodes[member.Id] = new JsonObject
            {
                ["kind"] = "package", ["name"] = member.Name,
                ["source"] = new JsonObject { ["editable"] = path },
                ["dependencies"] = new JsonArray(),
            };
            membership.Add(new JsonObject
            { ["name"] = member.Name, ["path"] = path, ["id"] = member.Id });
        }
        var document = new JsonObject
        {
            ["schema"] = new JsonObject { ["version"] = "preview" },
            ["workspace_root"] = checkout,
            ["workspace"] = new JsonObject { ["id"] = "workspace-context", ["path"] = checkout },
            ["requires_python"] = ">=3.14",
            ["conflicts"] = new JsonObject { ["sets"] = new JsonArray() },
            ["resolution"] = nodes, ["members"] = membership,
        };
        return new(NativeTermination.Exited, 0, document.ToJsonString(), "", 0.1, null);
    }
}
