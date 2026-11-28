using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Ecosystems;

internal sealed record NpmOriginal(string Path, long Size, string Sha256)
{
    internal static async Task<NpmOriginal> ReadAsync(string path, CancellationToken token)
    {
        (long size, string digest) = await NpmPackageOperations.OriginalAsync(path, token);
        return new(path, size, digest);
    }

    internal Task RequireOriginalAsync(CancellationToken token) =>
        OwnedFile.VerifyAsync(Path, Size, Sha256, "Qualified original changed during consumption.",
            token);
}

// These concrete PNPM operations are shared; CI and Release own their own checks.
internal static class NpmPackageOperations
{
    internal static NativeCommand Import(string directory, string package, string probe,
        int deadlineSeconds) => new("node", Path.Combine(directory, "consumer"),
            ["--input-type=module", "--eval",
                probe.Replace("@@PACKAGE@@", package, StringComparison.Ordinal)], deadlineSeconds);

    internal static NativeCommand Pack(string checkout, string product, string archive) =>
        new("pnpm", checkout, ["--dir", Path.Combine(checkout, product), "pack", "--out",
            archive], 300);

    internal static Task<(long Size, string Sha256)> OriginalAsync(string archive,
        CancellationToken token) =>
        OwnedFile.MeasureAsync(archive, token,
            "Required npm output is missing or not a file: " + archive,
            "Native npm output is empty.");

    internal static async Task<NativeCommand> MetadataAsync(string archive, string directory,
        CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        await NodePackageArchive.ReadAsync(archive, directory, token);
        string auth = Path.Combine(directory, "auth.npmrc");
        await File.WriteAllTextAsync(auth, "", token);
        return new("pnpm", directory,
            ["--dir", directory, "--ignore-workspace", "--npmrc-auth-file", auth, "pkg",
                "get", "--json", "name", "version", "type", "main", "dependencies",
                "optionalDependencies", "peerDependencies", "peerDependenciesMeta",
                "bundledDependencies", "bundleDependencies", "devDependencies", "scripts",
                "engines.runtime", "gypfile"], 30);
    }

    internal static Task<NativeCommand> InstallAsync(string archive, string directory,
        CancellationToken token) => InstallAsync(archive, directory, offline: true, token);

    // An online consumer resolves the original's registry dependencies from the public
    // registry with the same isolated state and no credentials.
    internal static async Task<NativeCommand> InstallAsync(string archive, string directory,
        bool offline, CancellationToken token)
    {
        string consumer = Path.Combine(directory, "consumer");
        string store = Path.Combine(directory, "store");
        string state = Path.Combine(directory, "state");
        string config = Path.Combine(directory, "config");
        foreach (string path in new[] { consumer, store, state, config })
        {
            Directory.CreateDirectory(path);
            if (Directory.EnumerateFileSystemEntries(path).Any())
                throw new InvalidDataException("Native consumer requires fresh isolated state.");
        }
        string auth = Path.Combine(config, "auth.npmrc");
        await File.WriteAllTextAsync(auth, "", token);
        return new("pnpm", consumer,
            ["--dir", consumer, "--ignore-workspace", "--store-dir", store, "--state-dir", state,
                "--npmrc-auth-file", auth, "install", archive,
                .. (string[])(offline ? ["--offline"] : []), "--ignore-scripts"],
            300);
    }
}
