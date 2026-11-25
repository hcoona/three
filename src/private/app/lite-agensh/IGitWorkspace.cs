using System.Text.Json.Nodes;

namespace LiteAgensh;

internal interface IGitWorkspace
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    string GetWorkerPath(string worker);

    Task ProcessPendingAsync(CancellationToken cancellationToken = default);

    Task<JsonObject> InvokeAsync(
        string actor,
        string name,
        JsonObject arguments,
        CancellationToken cancellationToken = default
    );

    Task<JsonObject> FinalizeAsync(CancellationToken cancellationToken = default);

    Task<JsonObject> CleanupAsync(CancellationToken cancellationToken = default);
}
