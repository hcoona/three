using WorkflowDelivery.Core;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Ecosystems;

internal sealed record NpmPackRecipe(string SourceName, string Main, bool RegistryDependencies,
    string Probe, bool TestBeforeBuild)
{
    internal string[] Required => ["native-input", .. Scripts, "pack", "contents", "consumer",
        "original-bytes"];
    private string[] Scripts => TestBeforeBuild ? ["test", "build"] : ["build", "test"];

    internal async Task<QualifiedUnitOriginals?> QualifyAsync(UnitBuildSubject subject,
        string checkout, string scratch, string product, string package, string version,
        RecordNativeCheck check, Func<string, NativeCommand, Task<NativeCommandResult>> run,
        CancellationToken token)
    {
        string originals = Directory.CreateDirectory(Path.Combine(scratch, "original")).FullName;
        string archive = Path.Combine(originals, "package.tgz");
        NpmOriginal? original = null;
        QualifiedUnitOriginals? result = null;
        foreach (string script in Scripts)
            await check(script, async () => await run(script, new("pnpm", checkout,
                ["--dir", Path.Combine(checkout, product),
                    "--config.verify-deps-before-run=false", "run", script], 300)));
        await check("pack", async () =>
        {
            // Some native package identities differ by channel, independently of registry routing.
            if (package != SourceName)
                await run("pack", new("pnpm", checkout, ["--dir", Path.Combine(checkout, product),
                    "pkg", "set", "name=" + package], 30));
            await run("pack", NpmPackageOperations.Pack(checkout, product, archive));
            original = await NpmOriginal.ReadAsync(archive, token);
        });
        await check("contents", async () =>
        {
            NativeCommand command = await NpmPackageOperations.MetadataAsync(archive,
                Path.Combine(scratch, "metadata"), token);
            NativeCommandResult observed = await run("contents", command);
            AdoptedNpmPackage.ValidateMetadata(observed.Stdout, version, package, Main,
                RegistryDependencies);
        });
        await check("consumer", async () =>
        {
            await run("consumer", await NpmPackageOperations.InstallAsync(archive, scratch,
                offline: !RegistryDependencies, token));
            await run("consumer", NpmPackageOperations.Import(scratch, package, Probe, 60));
        });
        await check("original-bytes", async () =>
        {
            await original!.RequireOriginalAsync(token);
            result = subject.Complete(new UnitOriginal(subject.ForKind("npm-tarball"), package,
                version, original.Path, original.Size, original.Sha256));
        });
        return result;
    }
}
