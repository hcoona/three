using System.Reflection;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Graph;

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
        using var diagnostics = new StreamWriter(new FileStream(
            request.Destination + ".host.log", FileMode.CreateNew, FileAccess.Write,
            FileShare.None));
        diagnostics.WriteLine($"Revision: {response.Revision}");
        diagnostics.WriteLine($"Root: {response.Root}");
        diagnostics.WriteLine($"Entry: {response.Entry}");
        foreach (Assembly assembly in new[] { typeof(ProjectGraph).Assembly,
            typeof(ITask).Assembly, typeof(Microsoft.Build.Utilities.Task).Assembly })
            diagnostics.WriteLine($"Assembly: {assembly.FullName}; FileVersion: " +
                $"{assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version}; " +
                $"Location: {assembly.Location}");
        return true;
    }
}
