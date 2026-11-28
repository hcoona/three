using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;

namespace WorkflowDelivery.Ecosystems;

// Construction API facts from the embedded source manifest, not an evaluated project.
internal sealed record DotNetProjectManifest(string? PackageId, string? AssemblyName,
    string[] ProjectReferences)
{
    internal static DotNetProjectManifest Read(string xml)
    {
        using var collection = new ProjectCollection();
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text);
        ProjectRootElement root = ProjectRootElement.Create(reader, collection);
        return new(Property("PackageId"), Property("AssemblyName"), [.. root.Items
            .Where(item => item.ItemType == "ProjectReference" &&
                !string.IsNullOrEmpty(item.Include))
            .Select(item => item.Include)]);

        string? Property(string name) =>
            root.Properties.FirstOrDefault(property => property.Name == name &&
                string.IsNullOrEmpty(property.Condition))?.Value.Trim() is { Length: > 0 } value
                    ? value : null;
    }
}
