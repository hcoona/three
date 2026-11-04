using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace LiteAgensh;

public static class ToolCatalog
{
    public static IReadOnlyList<Tool> Definitions { get; } = CreateDefinitions();

    public static bool IsGitOperation(string name) =>
        name is "change_write" or "change_read" or "change_list" or "change_review" or "check_read";

    private static List<Tool> CreateDefinitions()
    {
        List<Tool> tools =
        [
            Define("run_info", "Read this local run's goal, base, lifecycle and capabilities."),
            Define(
                "agent_info",
                "Read a known local run member.",
                new Parameter("id", Required: true)
            ),
            Define("agent_list", "List known local run members.", new Parameter("id")),
            Define(
                "task_write",
                "Create, update, comment on or close a local task. "
                    + "Claims are advisory, not task locks.",
                new("action", Required: true, Values: ["create", "update", "comment", "close"]),
                new("id", "integer"),
                new("title"),
                new("body")
            ),
            Define(
                "task_read",
                "Read a local task, comments and advisory claimants.",
                new Parameter("id", "integer", true)
            ),
            Define(
                "task_list",
                "List local tasks. State omission includes both states.",
                new("state", Values: ["open", "closed"]),
                new("cursor", "integer"),
                new("limit", "integer")
            ),
            Define(
                "work_search",
                "Search local tasks and changes, retaining their kind.",
                new("query", Required: true),
                new("cursor", "integer"),
                new("limit", "integer")
            ),
            Define(
                "message_send",
                "Send a team/direct message or reply. recipient is team or a known member ID; "
                    + "threadId is a visible root in that channel.",
                new("recipient", Required: true),
                new("body", Required: true),
                new("threadId", "integer"),
                new("mentions", "string[]"),
                new("urgent", "boolean")
            ),
            Define(
                "message_read",
                "Read visible messages in a local channel.",
                new("channelId", Required: true),
                new("cursor", "integer"),
                new("limit", "integer")
            ),
            Define(
                "message_thread",
                "Read a visible root message and replies.",
                new("threadId", "integer", true),
                new("cursor", "integer"),
                new("limit", "integer")
            ),
            Define(
                "message_search",
                "Search team history and your visible direct messages.",
                new("query", Required: true),
                new("cursor", "integer"),
                new("limit", "integer")
            ),
            Define(
                "channel_read",
                "Read a visible local team/direct channel.",
                new Parameter("id", Required: true)
            ),
            Define("channel_list", "List local channels visible to the bound member."),
            Define(
                "board_publish",
                "Publish a typed finding or advisory claim. Admission validates reported text; "
                    + "it does not execute checks.",
                new(
                    "type",
                    Required: true,
                    Values: ["FACT", "OBSERVED", "FAIL", "CLAIM", "PATCH_SUMMARY"]
                ),
                new("content", Required: true),
                new("detail"),
                new("workLabel"),
                new("ttlSeconds", "integer")
            ),
            Define(
                "board_recent",
                "Read the live recent board view, not all admitted history.",
                new Parameter("limit", "integer")
            ),
            Define(
                "board_search",
                "Search retained board history using the source boolean/literal query grammar.",
                new("query", Required: true),
                new("cursor", "integer"),
                new("limit", "integer")
            ),
            Define(
                "board_expand",
                "Expand board detail labels with unknown/truncated label information.",
                new Parameter("labels", "string[]", true)
            ),
            Define(
                "change_write",
                "Submit your worker head, update/comment, or queue exact-head integration. "
                    + "Create requires title/body; existing actions require id. "
                    + "Updates preserve omitted fields. integrate requires head "
                    + "and returns pending, not a merge.",
                new("action", Required: true, Values: ["submit", "update", "comment", "integrate"]),
                new("id", "integer"),
                new("title"),
                new("body"),
                new("taskIds", "integer[]"),
                new("head")
            ),
            Define(
                "change_read",
                "Read the exact candidate, diff, reviews, checks and integration request outcomes.",
                new Parameter("id", "integer", true)
            ),
            Define(
                "change_list",
                "List local changes, optionally filtered by state.",
                new("state", Values: ["open", "integrated"]),
                new("cursor", "integer"),
                new("limit", "integer")
            ),
            Define(
                "change_review",
                "Review an exact submitted head. "
                    + "A change author cannot supply their own peer approval.",
                new("id", "integer", true),
                new("head", Required: true),
                new("decision", Required: true, Values: ["approve", "request-changes", "comment"]),
                new("body", Required: true)
            ),
            Define(
                "check_read",
                "Read retained exact-commit check results/log references. "
                    + "Specify exactly one of id or changeId.",
                new("id", "integer"),
                new("changeId", "integer")
            ),
        ];
        return tools;
    }

    private static Tool Define(string name, string description, params Parameter[] parameters)
    {
        JsonObject properties = [];
        JsonArray required = [];
        foreach (Parameter parameter in parameters)
        {
            JsonObject schema = [];
            if (parameter.Type.EndsWith("[]", StringComparison.Ordinal))
            {
                schema["type"] = "array";
                schema["items"] = new JsonObject { ["type"] = parameter.Type[..^2] };
            }
            else
            {
                schema["type"] = parameter.Type;
            }

            if (parameter.Values is not null)
            {
                schema["enum"] = JsonSerializer.SerializeToNode(parameter.Values, Json.Options);
            }

            if (parameter.Name == "limit")
            {
                schema["minimum"] = 1;
                schema["maximum"] = 200;
            }
            else if (parameter.Type == "integer")
            {
                schema["minimum"] = parameter.Name == "cursor" ? 0 : 1;
            }

            properties[parameter.Name] = schema;
            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        JsonObject inputSchema = new()
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
        };
        return new Tool
        {
            Name = name,
            Description = description,
            InputSchema = JsonSerializer.SerializeToElement(inputSchema, Json.Options),
        };
    }

    private sealed record Parameter(
        string Name,
        string Type = "string",
        bool Required = false,
        string[]? Values = null
    );
}
