using System.Text.Json;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Ecosystems;

// Native MSBuild response projection shared by CI and Release.
internal static class DotNetPackageResults
{
    internal static async Task<JsonDocument> ResponseAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 32 * 1024 * 1024)
            throw new InvalidDataException("Missing regular native package result file.");
        await using var file = File.OpenRead(path);
        return await JsonDocument.ParseAsync(file, cancellationToken: token);
    }

    internal static JsonElement Target(JsonElement root, string target)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("TargetResults", out JsonElement targets) ||
            targets.ValueKind != JsonValueKind.Object ||
            !targets.TryGetProperty(target, out JsonElement result) ||
            result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("Result", out JsonElement verdict) ||
            verdict.ValueKind != JsonValueKind.String ||
            verdict.GetString() != "Success")
            throw new InvalidDataException("Missing or failed native package target: " + target);
        return result;
    }

    internal static Dictionary<string, string> Properties(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("Properties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Missing native package properties.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in properties.EnumerateObject())
            if (property.Value.ValueKind != JsonValueKind.String ||
                !values.TryAdd(property.Name, property.Value.GetString()!))
                throw new InvalidDataException("Invalid or duplicate native package property.");
        return values;
    }
    internal static readonly string[] NativeProperties =
    [
        "MSBuildProjectFullPath", "TargetFramework", "RuntimeIdentifier", "NETCoreSdkVersion",
        "IsPackable", "AssemblyName", "DocumentationFile", "GenerateDocumentationFile",
        "PackageReadmeFile", "IncludeSymbols", "IncludeSource", "PublishRepositoryUrl",
        "RepositoryUrl", "RepositoryType", "RepositoryCommit", "PackageId", "NuGetPackageVersion",
        "PackageVersion", "AssemblyVersion", "AssemblyFileVersion", "AssemblyInformationalVersion",
    ];
    private static readonly string[] RequiredText = ["AssemblyName", "DocumentationFile",
        "PackageReadmeFile", "NETCoreSdkVersion", "PackageId", "AssemblyFileVersion",
        "AssemblyInformationalVersion"];

    internal static void Validate(string projectPath, string framework, string runtime,
        Dictionary<string, string> values)
    {
        if (!values.TryGetValue("MSBuildProjectFullPath", out string? project) ||
            string.IsNullOrWhiteSpace(project) || !Path.IsPathFullyQualified(project) ||
            !Path.GetFullPath(project).Equals(
                Path.GetFullPath(projectPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                    StringComparison.Ordinal))
            throw new InvalidDataException("Different native package project identity.");
        var expectations = new Dictionary<string, string>(values, StringComparer.Ordinal);
        expectations.Remove("MSBuildProjectFullPath");
        ValidateExpectations(framework, runtime, expectations);
    }

    internal static void ValidateExpectations(string framework, string runtime,
        Dictionary<string, string> values)
    {
        if (values.Count != NativeProperties.Length - 1 ||
            NativeProperties.Where(name => name != "MSBuildProjectFullPath")
                .Any(name => !values.ContainsKey(name)) ||
            values.Any(pair => pair.Value is null) ||
            values["TargetFramework"] != framework ||
            values["RuntimeIdentifier"] != runtime ||
            !values["IsPackable"].Equals("true", StringComparison.OrdinalIgnoreCase) ||
            values["IncludeSymbols"].Equals("true", StringComparison.OrdinalIgnoreCase) ||
            values["IncludeSource"].Equals("true", StringComparison.OrdinalIgnoreCase) ||
            !values["GenerateDocumentationFile"].Equals("true",
                StringComparison.OrdinalIgnoreCase) ||
            RequiredText.Any(name => string.IsNullOrWhiteSpace(values[name])) ||
            !Version.TryParse(values["AssemblyVersion"], out _) ||
            !NuGetVersion.TryParse(values["NuGetPackageVersion"], out NuGetVersion? version) ||
            !NuGetVersion.TryParse(values["PackageVersion"], out NuGetVersion? packed) ||
            !PackageIdentityComparer.Default.Equals(new(values["PackageId"], version),
                new(values["PackageId"], packed)))
            throw new InvalidDataException(
                "Unresolved or inconsistent native package projections.");
    }
    internal static DotNetOriginalPackage Archive(string unit, string build, string output, string
        role, string kind, JsonElement root,
        string destination, Dictionary<string, string> expected)
    {
        JsonElement receipt = DotNetPackageResults.Target(root, "_GetOutputItemsFromPack");
        if (!receipt.TryGetProperty("Items", out JsonElement items) ||
            items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Missing native package output items.");
        var archives = new HashSet<string>(OperatingSystem.IsWindows() ?
            StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("FullPath", out JsonElement fullPath) ||
                fullPath.ValueKind != JsonValueKind.String ||
                !Path.IsPathFullyQualified(fullPath.GetString()!))
                throw new InvalidDataException("Invalid native package output coordinate.");
            string path = Path.GetFullPath(fullPath.GetString()!);
            string extension = Path.GetExtension(path);
            if (extension.Equals(".nuspec", StringComparison.OrdinalIgnoreCase)) continue;
            if (!extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                !OwnedPath.Within(path, destination) || !archives.Add(path))
                throw new InvalidDataException("Undeclared or ambiguous native package output.");
            DotNetPackageConsumer.RequireFile(path);
        }
        var actual = new HashSet<string>(archives.Comparer);
        foreach (string path in Directory.EnumerateFileSystemEntries(destination))
        {
            DotNetPackageConsumer.RequireFile(path);
            if (Path.GetExtension(path).Equals(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".snupkg", StringComparison.OrdinalIgnoreCase))
                actual.Add(Path.GetFullPath(path));
        }
        if (archives.Count != 1 || !actual.SetEquals(archives))
            throw new InvalidDataException(
                "The complete original native archive set is missing or undeclared.");
        string archive = archives.Single();
        DotNetPackageArchive.RequireIdentity(DotNetPackageArchive.Identity(archive), expected);
        return new(unit, build, output, role, kind, archive,
            new FileInfo(archive).Length, DotNetPackageArchive.Digest(archive));
    }
}
