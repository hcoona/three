using System.Text.Json;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record ControlBuildContext(string Revision, string Project,
    MsBuildDimension Dimension, string SdkVersion, string SdkDirectory, string DotNetDirectory,
    string PackageDirectory, string OutputDirectory, string IntermediateDirectory,
    string BaseIntermediateDirectory, string AssetsFile, string TargetFile,
    string[] EditorConfigCandidates, string[] GlobalConfigCandidates,
    IReadOnlyDictionary<string, string> Properties)
{
    internal void RequireSameOperation(ControlBuildLog log)
    {
        if (log.Project != Project)
            throw new InvalidDataException("The native build has a different control project.");
        foreach (string name in ControlBuildContextReader.PropertyNames.Split(','))
        {
            if (!Properties.TryGetValue(name, out string? expected))
                throw new InvalidDataException("Native query context is incomplete: " + name);
            string actual = log.Properties.GetValueOrDefault(name, "");
            bool same = name switch
            {
                "MSBuildProjectFullPath" or "MSBuildToolsPath" or "NetCoreRoot" or
                    "NuGetPackageRoot" or "OutputPath" or "IntermediateOutputPath" or
                    "BaseIntermediateOutputPath" or "ProjectAssetsFile" or "TargetPath" or
                    "MSBuildStartupDirectory" => !string.IsNullOrWhiteSpace(actual) &&
                    NativePath(actual) == NativePath(expected),
                "ContinuousIntegrationBuild" or "RestoreLockedMode" or
                    "MSBuildLogVerboseTaskParameters" or "NuGetInteractive" =>
                    actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
                _ => actual == expected,
            };
            if (!same)
                throw new InvalidDataException("Native build context differs from query: " + name);
        }
        if (log.GlobalProperties.Count != ControlBuildContextReader.GlobalPropertyNames.Length)
            throw new InvalidDataException(
                "Native control global property set differs from query.");
        foreach (string name in ControlBuildContextReader.GlobalPropertyNames)
        {
            if (!log.GlobalProperties.TryGetValue(name, out string? actual))
                throw new InvalidDataException("Missing native control global property: " + name);
            if (!actual.Equals(Properties[name], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Native control global differs from query: " + name);
        }

        string NativePath(string value) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            value.Replace('\\', Path.DirectorySeparatorChar), Path.GetDirectoryName(Project)!));
    }
}

// Native evaluation supplies the context and finite optional candidates for this build.
internal sealed class ControlBuildContextReader
{
    internal static readonly string[] GlobalPropertyNames = ["Configuration",
        "ContinuousIntegrationBuild", "RestoreLockedMode", "MSBuildLogVerboseTaskParameters",
        "NuGetInteractive"];
    internal const string PropertyNames = "MSBuildProjectFullPath,Configuration,TargetFramework,"
        + "TargetFrameworks,RuntimeIdentifier,RuntimeIdentifiers,NETCoreSdkVersion,"
        + "MSBuildToolsPath,NetCoreRoot,NuGetPackageRoot,OutputPath,IntermediateOutputPath,"
        + "BaseIntermediateOutputPath,"
        + "ProjectAssetsFile,TargetPath,ContinuousIntegrationBuild,RestoreLockedMode,"
        + "MSBuildStartupDirectory,MSBuildLogVerboseTaskParameters,NuGetInteractive";
    private readonly GitMaterialization checkout;
    private readonly Func<string[], CancellationToken, Task<string>> query;

    internal ControlBuildContextReader(GitMaterialization checkout, string executable = "dotnet")
        : this(checkout, (arguments, token) => NativeProcess.RunAsync(executable,
            checkout.Root, arguments, token))
    { }

    internal ControlBuildContextReader(GitMaterialization checkout,
        Func<string[], CancellationToken, Task<string>> query)
    {
        this.checkout = checkout;
        this.query = query;
    }

    internal async Task<ControlBuildContext> ReadAsync(string project, CancellationToken token)
    {
        if (Path.IsPathRooted(project) || project.Contains('\\') ||
            project.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Unsupported control project coordinate.");
        GitEntry? entry = checkout.Revision.Entries.SingleOrDefault(e => e.Path == project);
        if (entry is null || entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755"))
            throw new InvalidDataException("The control project must be a committed regular file.");
        string absolute = Path.GetFullPath(Path.Combine(checkout.Root, project));
        string output = await query(["msbuild", absolute, "-nologo", "-noAutoResponse",
            "-property:Configuration=Debug", "-property:ContinuousIntegrationBuild=true",
            "-property:RestoreLockedMode=true", "-property:MSBuildLogVerboseTaskParameters=true",
            "-property:NuGetInteractive=false",
            "-getProperty:" + PropertyNames,
            "-getItem:ProjectReference,PotentialEditorConfigFiles,GlobalAnalyzerConfigFiles"],
            token);
        token.ThrowIfCancellationRequested();
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement nativeProperties = Property(document.RootElement, "Properties");
        var properties = PropertyNames.Split(',').ToDictionary(name => name,
            name => Text(Property(nativeProperties, name)), StringComparer.Ordinal);
        string Read(string name) => properties[name];
        string PathProperty(string name) =>
            NativePath(Read(name), Path.GetDirectoryName(absolute)!);
        if (!Path.IsPathFullyQualified(Read("MSBuildProjectFullPath")) ||
            PathProperty("MSBuildProjectFullPath") != absolute ||
            Read("Configuration") != "Debug" ||
            !Literal(Read("TargetFramework")) || Read("TargetFrameworks") != "" ||
            Read("RuntimeIdentifier") != "" || Read("RuntimeIdentifiers") != "" ||
            !Read("ContinuousIntegrationBuild").Equals("true",
                StringComparison.OrdinalIgnoreCase) ||
            !Read("RestoreLockedMode").Equals("true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported native control operation context.");
        if (!Path.IsPathFullyQualified(Read("MSBuildStartupDirectory")) ||
            PathProperty("MSBuildStartupDirectory") != checkout.Root ||
            !Read("MSBuildLogVerboseTaskParameters").Equals("true",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native control working directory or logging differs.");
        JsonElement items = Property(document.RootElement, "Items");
        JsonElement references = Property(items, "ProjectReference");
        if (references.ValueKind != JsonValueKind.Array || references.GetArrayLength() != 0)
            throw new InvalidDataException("Control build project references require an adapter.");
        foreach (string name in new[] { "MSBuildToolsPath", "NetCoreRoot", "NuGetPackageRoot" })
            if (!Path.IsPathFullyQualified(Read(name)))
                throw new InvalidDataException(
                    "Native tool and dependency roots must be absolute.");
        if (!Literal(Read("NETCoreSdkVersion")))
            throw new InvalidDataException("The selected native SDK identity is unavailable.");
        return new(checkout.Revision.Commit, absolute, new("Debug", Read("TargetFramework")),
            Read("NETCoreSdkVersion"), PathProperty("MSBuildToolsPath"),
            PathProperty("NetCoreRoot"),
            PathProperty("NuGetPackageRoot"), PathProperty("OutputPath"),
            PathProperty("IntermediateOutputPath"), PathProperty("BaseIntermediateOutputPath"),
            PathProperty("ProjectAssetsFile"), PathProperty("TargetPath"),
            Candidates(Property(items, "PotentialEditorConfigFiles")),
            Candidates(Property(items, "GlobalAnalyzerConfigFiles")), properties);
    }

    private static string[] Candidates(JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(
                "Native configuration candidates must be an item array.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in items.EnumerateArray())
        {
            string fullPath = Text(Property(item, "FullPath"));
            if (!Path.IsPathFullyQualified(fullPath))
                throw new InvalidDataException("Native configuration candidates must be absolute.");
            paths.Add(Path.GetFullPath(fullPath));
        }
        return paths.Order(StringComparer.Ordinal).ToArray();
    }

    private static string NativePath(string value, string directory)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("Native operation path is unavailable.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            value.Replace('\\', Path.DirectorySeparatorChar), directory));
    }

    private static bool Literal(string value) => !string.IsNullOrWhiteSpace(value) &&
        !value.Any(c => char.IsControl(c) || c is ';' or ',' or '%' or '"');

    private static JsonElement Property(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid native control context object.");
        JsonProperty[] matches = value.EnumerateObject().Where(p => p.Name == name).ToArray();
        return matches.Length == 1 ? matches[0].Value
            : throw new InvalidDataException("Missing or duplicate native context field: " + name);
    }

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new InvalidDataException("Invalid native context value.");
}
