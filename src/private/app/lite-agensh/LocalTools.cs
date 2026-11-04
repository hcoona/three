using System.Text.Json.Nodes;

namespace LiteAgensh;

public sealed class LocalTools
{
    private readonly RunDatabase database;

    public LocalTools(RunDatabase database)
    {
        this.database = database;
        Collaboration = new(database);
        Git = new(database, Collaboration);
    }

    public CollaborationOperations Collaboration { get; }

    public GitOperations Git { get; }

    public async Task<JsonObject> InvokeAsync(string actor, string name, JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        if (!database.Members.Contains(actor, StringComparer.Ordinal))
        {
            return Operation.Failure("unknown_actor", "The helper is not bound to a known run member.");
        }

        JsonObject result = ToolCatalog.IsGitOperation(name)
            ? await Git.InvokeAsync(actor, name, arguments, cancellationToken).ConfigureAwait(false)
            : Collaboration.Invoke(actor, name, arguments);
        return Collaboration.AddNotices(actor, result);
    }
}
