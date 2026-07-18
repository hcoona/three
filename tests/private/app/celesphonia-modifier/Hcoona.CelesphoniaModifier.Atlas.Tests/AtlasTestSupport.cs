using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hcoona.CelesphoniaModifier.Atlas;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

internal static class AtlasTestSupport
{
    public static AtlasIoSeams CreateIo(
        Func<string, CancellationToken, ValueTask<byte[]>>? readAllBytesAsync = null,
        Func<string, string>? readAllText = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null,
        Func<string, FileAttributes>? getAttributes = null,
        Func<string, AtlasDriveInfo>? getDriveInfo = null,
        Func<string, SearchOption, IEnumerable<string>>? enumerateFileSystemEntries = null,
        Func<string, FileMode, FileAccess, FileShare, FileOptions, Stream>? openFile = null,
        Action<string>? createDirectory = null,
        Action<string, string>? moveFile = null,
        Action<string, string>? moveDirectory = null,
        Action<string, string, string?>? replaceFile = null,
        Action<string, bool>? deleteDirectory = null,
        Action<string, FileAttributes>? setAttributes = null,
        Func<string, long>? getLength = null,
        Func<string, DateTimeOffset>? getLastWriteTimeUtc = null) =>
        new()
        {
            ReadAllBytesAsync = readAllBytesAsync ?? AtlasIoSeams.Default.ReadAllBytesAsync,
            ReadAllText = readAllText ?? AtlasIoSeams.Default.ReadAllText,
            FileExists = fileExists ?? AtlasIoSeams.Default.FileExists,
            DirectoryExists = directoryExists ?? AtlasIoSeams.Default.DirectoryExists,
            GetAttributes = getAttributes ?? AtlasIoSeams.Default.GetAttributes,
            GetDriveInfo = getDriveInfo ?? AtlasIoSeams.Default.GetDriveInfo,
            EnumerateFileSystemEntries =
                enumerateFileSystemEntries ?? AtlasIoSeams.Default.EnumerateFileSystemEntries,
            OpenFile = openFile ?? AtlasIoSeams.Default.OpenFile,
            CreateDirectory = createDirectory ?? AtlasIoSeams.Default.CreateDirectory,
            MoveFile = moveFile ?? AtlasIoSeams.Default.MoveFile,
            MoveDirectory = moveDirectory ?? AtlasIoSeams.Default.MoveDirectory,
            ReplaceFile = replaceFile ?? AtlasIoSeams.Default.ReplaceFile,
            DeleteDirectory = deleteDirectory ?? AtlasIoSeams.Default.DeleteDirectory,
            SetAttributes = setAttributes ?? AtlasIoSeams.Default.SetAttributes,
            GetLength = getLength ?? AtlasIoSeams.Default.GetLength,
            GetLastWriteTimeUtc = getLastWriteTimeUtc ?? AtlasIoSeams.Default.GetLastWriteTimeUtc,
        };

    public static AtlasIoSeams CreateSourceReadCountingIo(
        AtlasSyntheticWorkspace workspace,
        Action onTrackedSourceOpen) =>
        CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                if (mode == FileMode.Open
                    && access == FileAccess.Read
                    && (AtlasDiscovery.ContainsPath(workspace.SaveRootPath, path)
                        || AtlasDiscovery.ContainsPath(workspace.DefinitionRootPath, path)))
                {
                    onTrackedSourceOpen();
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            });

    public static async Task<JsonObject> LoadJsonObjectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        JsonNode? node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
        return node as JsonObject
            ?? throw new InvalidOperationException("Expected a JSON object.");
    }

    public static Task WriteJsonAsync(
        string path,
        JsonNode node,
        CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(
            path,
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken);
}
