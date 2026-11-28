using System.IO.Compression;
using WorkflowDelivery.Core;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Ecosystems;

internal sealed record WindowsAssetsRecipe(string AssetPrefix, string Executable, string Editor)
{
    private static readonly string[] Pwsh = ["-NoLogo", "-NoProfile", "-NonInteractive",
        "-ExecutionPolicy", "Bypass", "-File"];
    private static readonly DateTimeOffset EntryTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal string Asset(string kind, string version) => kind switch
    {
        "inno-installer" => $"{AssetPrefix}-{version}-win-x64-setup.exe",
        "portable-zip" => $"{AssetPrefix}-{version}-win-x64-portable.zip",
        _ => throw new InvalidDataException("Unsupported Windows application asset kind."),
    };

    internal async Task<QualifiedUnitOriginals?> QualifyAsync(UnitBuildSubject subject,
        string product, string scratch, string version, RecordNativeCheck check,
        Func<NativeCommand, Task<NativeCommandResult>> run)
    {
        string publishRoot = Path.Combine(scratch, "publish");
        string installerRoot = Path.Combine(scratch, "installer");
        string originals = Path.Combine(scratch, "originals");
        string consumers = Path.Combine(scratch, "consumers");
        string installer = Path.Combine(originals, Asset("inno-installer", version));
        string portable = Path.Combine(originals, Asset("portable-zip", version));
        string publish = "";
        var files = new List<UnitOriginal>();
        QualifiedUnitOriginals? result = null;
        await check("build", async () =>
        {
            string entryDir = Path.GetDirectoryName(subject.EntryPoint)!.Replace('/',
                Path.DirectorySeparatorChar);
            await run(new("pwsh", Path.Combine(product, entryDir),
                [.. Pwsh, Path.Combine(product, entryDir, "Update-LocalSvgEditor.ps1"), "-Force"],
                600));
            await run(new("pwsh", product, [.. Pwsh, Path.Combine(product,
                "script/Publish-ImageOcclusionEditor.ps1"), "-Configuration", "Release",
                "-OutputRoot", publishRoot], 2700));
            await run(new("pwsh", product, [.. Pwsh, Path.Combine(product,
                "script/Build-InnoInstaller.ps1"), "-Configuration", "Release",
                "-PublishOutputRoot", publishRoot, "-InstallerOutputPath", installerRoot], 900));
            string[] executables = Directory.Exists(publishRoot) ? Directory.GetFiles(
                publishRoot, Executable, SearchOption.AllDirectories) : [];
            if (executables is not [string executable])
                throw new InvalidDataException("Native publish produced no unique application.");
            publish = Path.GetDirectoryName(executable)!;
            string[] setups =
                Directory.Exists(installerRoot) ? Directory.GetFiles(installerRoot, "*.exe") : [];
            if (setups is not [string setup])
                throw new InvalidDataException("Native installer build produced no unique setup.");
            Directory.CreateDirectory(originals);
            File.Copy(setup, installer, overwrite: false);
            CreatePortable(publish, portable);
        });
        await check("contents", () =>
        {
            RequirePortableExecutable(installer);
            using ZipArchive archive = ZipFile.OpenRead(portable);
            var expected = Tree(publish);
            var actual = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                RelativeEntry(entry.FullName);
                if (!actual.TryAdd(entry.FullName, entry.Length))
                    throw new InvalidDataException("Portable package repeats an entry.");
            }
            if (actual.Count != expected.Count || expected.Any(pair =>
                    !actual.TryGetValue(pair.Key, out long length) || length != pair.Value) ||
                !actual.ContainsKey(Executable) || !actual.ContainsKey(Editor))
                throw new InvalidDataException("Portable package differs from the publish tree.");
            foreach (UnitOutput output in subject.Outputs)
            {
                string path = output.Kind == "inno-installer" ? installer : portable;
                (long size, string digest) = Original(path);
                files.Add(new(output, Path.GetFileName(path), version, path, size, digest));
            }
            return Task.CompletedTask;
        });
        await check("installer-consumer", async () =>
        {
            string target = Path.Combine(consumers, "installed");
            Directory.CreateDirectory(consumers);
            await run(new(installer, consumers, ["/VERYSILENT", "/SUPPRESSMSGBOXES",
                "/NORESTART", "/SP-", "/NOICONS", "/DIR=" + target,
                "/LOG=" + Path.Combine(consumers, "install.log")], 600));
            RequireConsumed(target, publish);
        });
        await check("portable-consumer", () =>
        {
            string target = Path.Combine(consumers, "portable");
            ZipFile.ExtractToDirectory(portable, target);
            RequireConsumed(target, publish);
            return Task.CompletedTask;
        });
        await check("original-bytes", () =>
        {
            foreach (UnitOriginal file in files)
                OwnedFile.Verify(
                    file.Path, file.Size, file.Sha256, "Qualified Windows asset changed.");
            result = subject.Complete([.. files]);
            return Task.CompletedTask;
        });
        return result;
    }

    private static void CreatePortable(string directory, string archive)
    {
        using FileStream stream = new(archive, FileMode.CreateNew, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (string name in Tree(directory).Keys.Order(StringComparer.Ordinal))
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = EntryTime;
            using Stream target = entry.Open();
            using FileStream source = File.OpenRead(Path.Combine(directory, name));
            source.CopyTo(target);
        }
    }

    private static (long Size, string Sha256) Original(string path) =>
        OwnedFile.Measure(path, emptyMessage: "Empty original GitHub Release asset.");

    private static Dictionary<string, long> Tree(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
            file => Path.GetRelativePath(directory, file).Replace('\\', '/'),
            file => new FileInfo(file).Length, StringComparer.Ordinal);

    private void RequireConsumed(string target, string publish)
    {
        string executable = Path.Combine(target, Executable);
        if (!File.Exists(executable) || !File.Exists(Path.Combine(target,
                Editor.Replace('/', Path.DirectorySeparatorChar))) ||
            Original(executable) != Original(Path.Combine(publish, Executable)))
            throw new InvalidDataException("Consumed package lacks the published application.");
        RequirePortableExecutable(executable);
    }

    private static void RequirePortableExecutable(string path)
    {
        using FileStream stream = File.OpenRead(path);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException("Original is not a Windows executable.");
    }

    private static void RelativeEntry(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':') ||
            name.StartsWith('/') || name.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Portable package entry leaves its root.");
    }
}
