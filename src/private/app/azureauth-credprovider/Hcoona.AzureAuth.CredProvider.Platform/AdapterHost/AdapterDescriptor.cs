using System.Collections.ObjectModel;
using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public sealed class AdapterDescriptor
{
    public AdapterDescriptor(
        string name,
        AdapterProtocol protocol,
        IEnumerable<AdapterEntrypointDescriptor> entrypoints,
        string? description = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(entrypoints);

        AdapterEntrypointDescriptor[] copiedEntrypoints = entrypoints.ToArray();
        if (copiedEntrypoints.Length == 0)
        {
            throw new ArgumentException(
                "Adapter descriptors require at least one entry point.",
                nameof(entrypoints)
            );
        }

        if (copiedEntrypoints.Any(static entrypoint => entrypoint is null))
        {
            throw new ArgumentException(
                "Adapter descriptors must not contain null entry points.",
                nameof(entrypoints)
            );
        }

        if (!Enum.IsDefined(protocol))
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocol),
                protocol,
                "Unknown adapter protocol."
            );
        }

        Name = name;
        Protocol = protocol;
        Entrypoints = Array.AsReadOnly(copiedEntrypoints);
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
        SupportedProtocols = ResolveSupportedProtocols(protocol, copiedEntrypoints);
        SupportsProtocolMode = copiedEntrypoints.Any(static entrypoint =>
            entrypoint.Mode == AdapterInvocationMode.Protocol
        );
        SupportsHumanCommandMode = copiedEntrypoints.Any(static entrypoint =>
            entrypoint.Mode == AdapterInvocationMode.HumanCommand
        );
    }

    public string Name { get; }

    public AdapterProtocol Protocol { get; }

    public IReadOnlyList<AdapterProtocol> SupportedProtocols { get; }

    public IReadOnlyList<AdapterEntrypointDescriptor> Entrypoints { get; }

    public string? Description { get; }

    public bool SupportsProtocolMode { get; }

    public bool SupportsHumanCommandMode { get; }

    private static ReadOnlyCollection<AdapterProtocol> ResolveSupportedProtocols(
        AdapterProtocol descriptorProtocol,
        IReadOnlyList<AdapterEntrypointDescriptor> entrypoints
    )
    {
        AdapterProtocol[] protocols = entrypoints
            .Where(static entrypoint => entrypoint.Mode == AdapterInvocationMode.Protocol)
            .Select(entrypoint => entrypoint.ResolveProtocol(descriptorProtocol))
            .Where(static protocol => protocol != AdapterProtocol.Unspecified)
            .Distinct()
            .ToArray();

        foreach (AdapterEntrypointDescriptor entrypoint in entrypoints)
        {
            if (entrypoint.Mode != AdapterInvocationMode.Protocol)
            {
                continue;
            }

            AdapterProtocol resolvedProtocol = entrypoint.ResolveProtocol(descriptorProtocol);
            if (resolvedProtocol == AdapterProtocol.Unspecified)
            {
                throw new ArgumentException(
                    "Protocol entry points require a concrete adapter protocol.",
                    nameof(entrypoints)
                );
            }

            if (
                descriptorProtocol != AdapterProtocol.Unspecified
                && entrypoint.Protocol != AdapterProtocol.Unspecified
                && entrypoint.Protocol != descriptorProtocol
            )
            {
                throw new ArgumentException(
                    "Protocol entry points must agree with the descriptor protocol.",
                    nameof(entrypoints)
                );
            }
        }

        if (descriptorProtocol != AdapterProtocol.Unspecified && protocols.Length == 0)
        {
            throw new ArgumentException(
                "A concrete descriptor protocol requires a protocol entry point.",
                nameof(entrypoints)
            );
        }

        return Array.AsReadOnly(protocols);
    }
}
