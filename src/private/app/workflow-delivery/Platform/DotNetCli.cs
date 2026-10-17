namespace WorkflowDelivery.Platform;

// Persistent build servers would outlive a command, hold owned files and escape deadline
// cleanup. On the pinned SDK the CLI expands this one native option to -nodeReuse:false,
// UseSharedCompilation=false and UseRazorBuildServer=false, and it bypasses the MSBuild server.
internal static class DotNetCli
{
    internal const string DisableBuildServers = "--disable-build-servers";

    // The CLI option reaches only the command's own projects. A task that starts its own
    // BuildManager builds with fresh global properties, so its nodes read the compiler-server
    // choices from the inherited environment instead. Its BuildParameters own node reuse.
    internal static Dictionary<string, string?> NestedBuildEnvironment() =>
        new(StringComparer.Ordinal)
        {
            ["UseSharedCompilation"] = "false",
            ["UseRazorBuildServer"] = "false",
        };
}
