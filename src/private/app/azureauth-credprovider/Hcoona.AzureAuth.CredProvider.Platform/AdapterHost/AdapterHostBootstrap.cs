using System.Collections.ObjectModel;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public static class AdapterHostBootstrap
{
    public static AdapterInvocationContext ResolveInvocation(
        AdapterDescriptor descriptor,
        string? executablePath,
        IEnumerable<string>? arguments = null
    )
    {
        if (
            TryResolveInvocation(descriptor, executablePath, arguments, out var context)
            && context is not null
        )
        {
            return context;
        }

        throw new InvalidOperationException(
            $"Adapter descriptor '{descriptor.Name}' does not match the current invocation."
        );
    }

    public static bool TryResolveInvocation(
        AdapterDescriptor descriptor,
        string? executablePath,
        IEnumerable<string>? arguments,
        out AdapterInvocationContext? context
    )
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        ReadOnlyCollection<string> copiedArguments = CopyArguments(arguments);
        string? executableName = GetExecutableName(executablePath);

        IEnumerable<AdapterEntrypointDescriptor> orderedEntrypoints =
            descriptor.Entrypoints.OrderBy(static entrypoint =>
                entrypoint.Mode == AdapterInvocationMode.Protocol ? 0 : 1
            );
        foreach (AdapterEntrypointDescriptor entrypoint in orderedEntrypoints)
        {
            if (
                !entrypoint.TryMatch(
                    executableName,
                    copiedArguments,
                    out IReadOnlyList<string> matchedArguments,
                    out IReadOnlyList<string> payloadArguments
                )
            )
            {
                continue;
            }

            context = new AdapterInvocationContext(
                descriptor,
                entrypoint,
                executablePath,
                executableName,
                copiedArguments,
                matchedArguments,
                payloadArguments
            );
            return true;
        }

        context = null;
        return false;
    }

    internal static string? GetExecutableName(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        string trimmedPath = Path.TrimEndingDirectorySeparator(executablePath);
        string fileName = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".." ? null : fileName;
    }

    private static ReadOnlyCollection<string> CopyArguments(IEnumerable<string>? arguments)
    {
        if (arguments is null)
        {
            return ReadOnlyCollection<string>.Empty;
        }

        string[] copied = arguments.ToArray();
        if (copied.Any(static argument => argument is null))
        {
            throw new ArgumentException(
                "Adapter arguments must not contain null values.",
                nameof(arguments)
            );
        }

        return copied.Length == 0 ? ReadOnlyCollection<string>.Empty : Array.AsReadOnly(copied);
    }
}
