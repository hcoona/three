using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record RubyBundleTools(RubyFactTools Facts, string Gem, string Bundle,
    string ProbeVersion);
internal sealed record RubyPreparationCommand(NativeCommand Command, NativeCommandResult Result);
internal sealed record RubyPreparedBundles(RubyBundleContext[] Contexts,
    RubyPreparationCommand[] Commands);
internal sealed class RubyBundlePreparationException(RubyPreparationCommand[] commands)
    : IOException("Native Ruby dependency preparation failed: " +
        $"{commands[^1].Result.Termination}, exit {commands[^1].Result.ExitCode}. " +
        $"{commands[^1].Result.Error} {commands[^1].Result.Stderr}")
{
    internal RubyPreparationCommand[] Commands { get; } = commands;
}

// Native gem/Bundler own installation. The caller owns tools, environment and disposable state.
internal static class RubyBundlePreparation
{
    internal static Task<RubyPreparedBundles> PrepareAsync(GitMaterialization checkout,
        RubyBundleTools tools, CancellationToken token) =>
        PrepareAsync(checkout, tools, NativeProcess.ExecuteAsync, token);

    internal static async Task<RubyPreparedBundles> PrepareAsync(GitMaterialization checkout,
        RubyBundleTools tools,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(tools.Gem) || !Path.IsPathFullyQualified(tools.Bundle) ||
            string.IsNullOrWhiteSpace(tools.ProbeVersion))
            throw new InvalidDataException("Ruby dependency preparation requires native tools.");
        var collector = new RubyNativeFactCollector(tools.Facts, execute);
        RubyRepositoryInventory inventory = RubyRepositoryInventory.Discover(checkout.Revision);
        if (inventory.Bundles.Length == 0) return new([], []);
        var commands = new List<RubyPreparationCommand>();
        var versions = new HashSet<string>(StringComparer.Ordinal);
        var contexts = new List<RubyBundleContext>();
        await InstallAsync(tools.ProbeVersion);
        foreach (RubyBundleLocation bundle in inventory.Bundles)
        {
            string gemfile = Path.Combine(checkout.Root, bundle.Gemfile);
            RubyBundleContext context = await collector.ContextAsync(gemfile, tools.ProbeVersion,
                token);
            string expectedLock = Path.Combine(checkout.Root, bundle.Directory,
                Path.GetFileName(bundle.Gemfile) == "gems.rb" ? "gems.locked" : "Gemfile.lock");
            if (!Path.GetFullPath(expectedLock).Equals(context.Lockfile, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Unsupported native Ruby lockfile coordinate.");
            // Require the actual original lock input before installation; never create a lock.
            if (await checkout.ReadOptionalTextAsync(Path.GetRelativePath(checkout.Root,
                    context.Lockfile).Replace('\\', '/'), token) is null)
                throw new InvalidDataException("Native Ruby dependency lock is not committed.");
            await InstallAsync(context.LockedBundlerVersion);
            var environment = new Dictionary<string, string?>(tools.Facts.Environment,
                StringComparer.Ordinal)
            {
                ["BUNDLE_GEMFILE"] = gemfile,
                ["BUNDLE_FROZEN"] = "1",
            };
            await RunAsync(new(tools.Facts.Ruby, Path.GetDirectoryName(gemfile)!,
                [tools.Bundle, "_" + context.LockedBundlerVersion + "_", "install"],
                tools.Facts.DeadlineSeconds, environment, InheritEnvironment: false));
            contexts.Add(context);
        }
        return new(contexts.ToArray(), commands.ToArray());

        async Task InstallAsync(string version)
        {
            if (!versions.Add(version)) return;
            await RunAsync(new(tools.Facts.Ruby, checkout.Root,
                [tools.Gem, "install", "bundler", "--version", version, "--no-document"],
                tools.Facts.DeadlineSeconds, tools.Facts.Environment, InheritEnvironment: false));
        }

        async Task RunAsync(NativeCommand command)
        {
            token.ThrowIfCancellationRequested();
            NativeCommandResult result = await execute(command, token);
            commands.Add(new(command, result));
            token.ThrowIfCancellationRequested();
            if (!result.Succeeded || result.Error is not null)
                throw new RubyBundlePreparationException(commands.ToArray());
        }
    }
}
