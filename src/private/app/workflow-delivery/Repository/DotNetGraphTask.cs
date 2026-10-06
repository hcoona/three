using System.Text.Json;
using Microsoft.Build.Framework;

namespace WorkflowDelivery.Repository;

// The existing application supplies this entry; ordinary MSBuild loads its dependencies.
public sealed class DotNetGraphTask : ITask
{
    public IBuildEngine BuildEngine { get; set; } = null!;
    public ITaskHost HostObject { get; set; } = null!;

    [Required]
    public string RequestFile { get; set; } = "";

    public bool Execute()
    {
        DotNetGraphRequest request = JsonSerializer.Deserialize(File.ReadAllText(RequestFile),
            TransferJson.Default.DotNetGraphRequest)
            ?? throw new InvalidDataException("Missing native graph request.");
        DotNetGraphResponse response = DotNetGraph.Read(request);
        using var output = new FileStream(request.Destination, FileMode.CreateNew,
            FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, response, TransferJson.Default.DotNetGraphResponse);
        return true;
    }
}
