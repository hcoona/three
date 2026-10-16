using System.Security.Cryptography;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Ecosystems;

// These concrete PNPM operations are shared; CI and Release own their own checks.
internal static class NpmPackageOperations
{
    internal static NativeCommand Pack(string checkout, string product, string archive) =>
        new("pnpm", checkout, ["--dir", Path.Combine(checkout, product), "pack", "--out",
            archive], 300);

    internal static async Task<(long Size, string Sha256)> OriginalAsync(string archive,
        CancellationToken token)
    {
        NodePackageArchive.RequireRegularFile(archive);
        using var file = File.OpenRead(archive);
        if (file.Length <= 0)
            throw new InvalidDataException("Native npm output is empty.");
        return (file.Length, Convert.ToHexStringLower(await SHA256.HashDataAsync(file, token)));
    }

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

    internal static async Task<NativeCommand> InstallAsync(string archive, string directory,
        CancellationToken token)
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
                "--npmrc-auth-file", auth, "install", archive, "--offline", "--ignore-scripts"],
            300);
    }

}
