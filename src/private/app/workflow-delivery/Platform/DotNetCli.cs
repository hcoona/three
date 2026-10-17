namespace WorkflowDelivery.Platform;

// Every SDK command that runs MSBuild is confined to its own process tree. On the pinned SDK
// the CLI expands this one native option to -nodeReuse:false, UseSharedCompilation=false and
// UseRazorBuildServer=false, and it bypasses the MSBuild server. Persistent workers would
// otherwise outlive the command, hold owned files and escape deadline cleanup.
internal static class DotNetCli
{
    internal const string DisableBuildServers = "--disable-build-servers";
}
