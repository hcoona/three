using System.Text.Json;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record MsBuildDimension(string Configuration, string TargetFramework,
    string RuntimeIdentifier = "");
internal sealed record MsBuildResources(string Revision, string Project,
    MsBuildDimension Dimension, string[] Inputs);

// MSBuild owns evaluation. This projects only resources for one native project/dimension.
internal sealed class MsBuildResourceReader
{
    private readonly GitMaterialization checkout;
    private readonly Func<string[], CancellationToken, Task<string>> query;
    private readonly Dictionary<string, GitEntry> entries;

    internal MsBuildResourceReader(GitMaterialization checkout, string executable = "dotnet")
        : this(checkout, (arguments, token) => NativeProcess.RunAsync(executable,
            checkout.Root, arguments, token))
    { }

    internal MsBuildResourceReader(GitMaterialization checkout,
        Func<string[], CancellationToken, Task<string>> query)
    {
        this.checkout = checkout;
        this.query = query;
        entries = checkout.Revision.Entries.ToDictionary(entry => entry.Path,
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    internal async Task<MsBuildResources> ReadAsync(string project, MsBuildDimension dimension,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RequireCoordinate(project);
        string committedProject = RequireFile(project);
        RequireLiteral(dimension.Configuration, false);
        RequireLiteral(dimension.TargetFramework, false);
        RequireLiteral(dimension.RuntimeIdentifier, true);
        string output = await query(["msbuild", Path.Combine(checkout.Root, committedProject),
            "-nologo", "-noAutoResponse",
            "-getProperty:MSBuildProjectFullPath,Configuration,TargetFramework,RuntimeIdentifier",
            "-getItem:EmbeddedResource", "-property:Configuration=" + dimension.Configuration,
            "-property:TargetFramework=" + dimension.TargetFramework,
            "-property:RuntimeIdentifier=" + dimension.RuntimeIdentifier], token);
        token.ThrowIfCancellationRequested();
        using JsonDocument result = JsonDocument.Parse(output);
        JsonElement properties = Property(result.RootElement, "Properties");
        string nativeProject = SourcePath(Text(Property(properties, "MSBuildProjectFullPath")));
        var effective = new MsBuildDimension(Text(Property(properties, "Configuration")),
            Text(Property(properties, "TargetFramework")),
            Text(Property(properties, "RuntimeIdentifier"), allowEmpty: true));
        if (nativeProject != committedProject || effective != dimension)
            throw new InvalidDataException("MSBuild returned a different project or dimension.");
        JsonElement resources = Property(Property(result.RootElement, "Items"),
            "EmbeddedResource");
        if (resources.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("MSBuild resources must be an evaluated item array.");
        var inputs = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in resources.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            inputs.Add(SourcePath(Text(Property(item, "FullPath"))));
        }
        return new(checkout.Revision.Commit, committedProject, effective,
            inputs.Order(StringComparer.Ordinal).ToArray());
    }

    private string SourcePath(string nativePath)
    {
        if (!Path.IsPathFullyQualified(nativePath))
            throw new InvalidDataException("MSBuild must return an absolute source path.");
        string relative = Path.GetRelativePath(checkout.Root, Path.GetFullPath(nativePath))
            .Replace(Path.DirectorySeparatorChar, '/');
        RequireCoordinate(relative);
        return RequireFile(relative);
    }

    private string RequireFile(string path)
    {
        if (!entries.TryGetValue(path, out GitEntry? entry) || entry.ObjectType != "blob" ||
            entry.Mode is not ("100644" or "100755") ||
            !File.Exists(Path.Combine(checkout.Root, entry.Path)))
            throw new InvalidDataException("MSBuild requires a committed regular source file: "
                + path);
        return entry.Path;
    }

    private static void RequireCoordinate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') ||
            path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Unsupported MSBuild repository source coordinate.");
    }

    private static void RequireLiteral(string value, bool allowEmpty)
    {
        // Do not introduce an MSBuild property-expression or command-line escaping engine.
        if (value is null || (!allowEmpty && string.IsNullOrWhiteSpace(value)) ||
            value.Any(c => char.IsControl(c) || c is ';' or ',' or '%' or '"'))
            throw new InvalidDataException("MSBuild dimensions require single literal values.");
    }

    private static JsonElement Property(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("MSBuild returned an invalid native result object.");
        JsonProperty[] matches = value.EnumerateObject().Where(p => p.Name == name).ToArray();
        return matches.Length == 1 ? matches[0].Value
            : throw new InvalidDataException("MSBuild omitted or duplicated a required field.");
    }

    private static string Text(JsonElement value, bool allowEmpty = false) =>
        value.ValueKind == JsonValueKind.String &&
        (allowEmpty || !string.IsNullOrWhiteSpace(value.GetString()))
            ? value.GetString()!
            : throw new InvalidDataException("MSBuild returned an invalid native value.");
}
