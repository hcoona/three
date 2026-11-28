using WorkflowDelivery.Core;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Ecosystems;

internal static class BrowserExtensionPackage
{
    // WXT's Firefox script emits both the extension and the source archive.
    private static readonly (
        UnitOutput Output,
        string Script,
        string Suffix,
        int Manifest)[] Targets =
    [
        (new("chrome-zip", "primary-package", "chrome-extension-zip"), "zip", "chrome", 3),
        (new("firefox-zip", "primary-package", "firefox-extension-zip"),
            "zip:firefox", "firefox", 2),
        (new("firefox-sources", "source-package", "firefox-source-zip"), "", "sources", 0),
        (new("edge-zip", "primary-package", "edge-extension-zip"), "zip:edge", "edge", 3),
    ];

    internal static IEnumerable<UnitOutput> Outputs => Targets.Select(target => target.Output);

    internal static IEnumerable<(UnitOutput Output, string Name)> Archives(
        string unit, string version) =>
        Targets.Select(target => (target.Output, ArchiveName(unit, version, target.Suffix)));

    private static string ArchiveName(string unit, string version, string suffix) =>
        $"{unit}-{version}-{suffix}.zip";

    internal static async Task<QualifiedUnitOriginals?> QualifyAsync(UnitBuildSubject unit,
        string checkout, string scratch, string productDirectory,
        BrowserExtensionExpectation version,
        RecordNativeCheck check, Func<NativeCommand, Task<NativeCommandResult>> run,
        CancellationToken token)
    {
        string product = Path.Combine(checkout, productDirectory);
        string originals = Directory.CreateDirectory(Path.Combine(scratch, "original")).FullName;
        var files = new List<UnitOriginal>();
        QualifiedUnitOriginals? result = null;
        await check("build", async () =>
        {
            foreach (var target in Targets.Where(target => target.Script.Length != 0))
                await run(new("pnpm", checkout, ["--dir", product,
                    "--config.verify-deps-before-run=false", "run", target.Script], 600));
            foreach (var target in Targets)
            {
                string name = Name(target.Suffix);
                string source = Path.Combine(product, ".output", name);
                OwnedFile.RequireExists(
                    source, "Required npm output is missing or not a file: " + source);
                File.Copy(source, Path.Combine(originals, name), overwrite: false);
            }
        });
        await check("contents", async () =>
        {
            foreach (var target in Targets)
            {
                string path = Path.Combine(originals, Name(target.Suffix));
                (long size, string digest) = await OwnedFile.MeasureAsync(path, token,
                    emptyMessage: "Native browser extension output is empty.");
                if (target.Manifest == 0)
                    BrowserExtensionArchive.ValidateSources(path, productDirectory);
                else BrowserExtensionArchive.ValidateMembers(path);
                files.Add(new(target.Output, unit.Unit, version.Version, path, size, digest));
            }
        });
        await check("consumer", () =>
        {
            foreach (var target in Targets.Where(target => target.Manifest != 0))
                BrowserExtensionArchive.ValidateExtension(
                    Path.Combine(originals, Name(target.Suffix)),
                    target.Manifest, version);
            return Task.CompletedTask;
        });
        await check("original-bytes", async () =>
        {
            foreach (UnitOriginal file in files)
                await OwnedFile.VerifyAsync(file.Path, file.Size, file.Sha256,
                    "Qualified original changed during checks.", token);
            result = unit.Complete([.. files]);
        });
        return result;

        string Name(string suffix) => ArchiveName(unit.Unit, version.Label, suffix);
    }
}
