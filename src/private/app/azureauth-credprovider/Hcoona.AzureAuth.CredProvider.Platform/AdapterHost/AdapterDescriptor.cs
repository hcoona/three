using System.Collections.ObjectModel;
using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public sealed class AdapterDescriptor
{
    public AdapterDescriptor(
        string name,
        AdapterProtocol protocol,
        IEnumerable<AdapterEntrypointDescriptor> entrypoints,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ReadOnlyCollection<AdapterEntrypointDescriptor> copiedEntrypoints =
            CopyEntrypoints(entrypoints);
        ValidateProtocol(protocol, nameof(protocol));
        ReadOnlyCollection<AdapterProtocol> supportedProtocols = ResolveSupportedProtocols(
            protocol,
            copiedEntrypoints);
        ValidateSubsumedHumanCommandBoundaries(copiedEntrypoints);

        Name = name;
        Protocol = protocol;
        Entrypoints = copiedEntrypoints;
        SupportedProtocols = supportedProtocols;
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
        SupportsProtocolMode = copiedEntrypoints.Any(
            static entrypoint => entrypoint.Mode == AdapterInvocationMode.Protocol);
        SupportsHumanCommandMode = copiedEntrypoints.Any(
            static entrypoint => entrypoint.Mode == AdapterInvocationMode.HumanCommand);
    }

    public string Name { get; }

    public AdapterProtocol Protocol { get; }

    public IReadOnlyList<AdapterProtocol> SupportedProtocols { get; }

    public IReadOnlyList<AdapterEntrypointDescriptor> Entrypoints { get; }

    public string? Description { get; }

    public bool SupportsProtocolMode { get; }

    public bool SupportsHumanCommandMode { get; }

    private static void ValidateProtocol(AdapterProtocol protocol, string paramName)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                protocol,
                "Unknown adapter protocol.");
        }
    }

    private static ReadOnlyCollection<AdapterEntrypointDescriptor> CopyEntrypoints(
        IEnumerable<AdapterEntrypointDescriptor> entrypoints)
    {
        ArgumentNullException.ThrowIfNull(entrypoints);

        var copiedEntrypoints = entrypoints.ToArray();
        if (copiedEntrypoints.Length == 0)
        {
            throw new ArgumentException(
                "Adapter descriptors require at least one entry point.",
                nameof(entrypoints));
        }

        if (Array.Exists(copiedEntrypoints, static entrypoint => entrypoint is null))
        {
            throw new ArgumentException(
                "Adapter descriptors must not contain null entry points.",
                nameof(entrypoints));
        }

        return Array.AsReadOnly(copiedEntrypoints);
    }

    private static ReadOnlyCollection<AdapterProtocol> ResolveSupportedProtocols(
        AdapterProtocol descriptorProtocol,
        IReadOnlyList<AdapterEntrypointDescriptor> entrypoints)
    {
        var seenProtocols = new HashSet<AdapterProtocol>();
        var supportedProtocols = new List<AdapterProtocol>();
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
                    "Protocol entry points require a concrete adapter protocol on the entry " +
                    "point or descriptor.",
                    nameof(entrypoints));
            }

            if (descriptorProtocol != AdapterProtocol.Unspecified &&
                entrypoint.Protocol != AdapterProtocol.Unspecified &&
                entrypoint.Protocol != descriptorProtocol)
            {
                throw new ArgumentException(
                    "Protocol entry points must agree with the descriptor protocol when " +
                    "descriptor metadata is declared.",
                    nameof(entrypoints));
            }

            if (seenProtocols.Add(resolvedProtocol))
            {
                supportedProtocols.Add(resolvedProtocol);
            }
        }

        if (supportedProtocols.Count == 0)
        {
            if (descriptorProtocol != AdapterProtocol.Unspecified)
            {
                throw new ArgumentException(
                    "Adapter descriptors with a concrete protocol require at least one " +
                    "protocol entry point.",
                    nameof(entrypoints));
            }

            return ReadOnlyCollection<AdapterProtocol>.Empty;
        }

        return Array.AsReadOnly(supportedProtocols.ToArray());
    }

    private static void ValidateSubsumedHumanCommandBoundaries(
        IReadOnlyList<AdapterEntrypointDescriptor> entrypoints)
    {
        AdapterEntrypointDescriptor[] humanEntrypoints = entrypoints
            .Where(static entrypoint => entrypoint.Mode == AdapterInvocationMode.HumanCommand)
            .ToArray();
        if (humanEntrypoints.Length == 0)
        {
            return;
        }

        AdapterEntrypointDescriptor[] protocolEntrypoints = entrypoints
            .Where(static entrypoint => entrypoint.Mode == AdapterInvocationMode.Protocol)
            .ToArray();
        foreach (AdapterEntrypointDescriptor humanEntrypoint in humanEntrypoints)
        {
            foreach (AdapterEntrypointDescriptor protocolEntrypoint in protocolEntrypoints)
            {
                if (!AdapterEntrypointBoundaryRelations.IsInvocationCoveredByProtocol(
                        humanEntrypoint,
                        protocolEntrypoint))
                {
                    continue;
                }

                throw new ArgumentException(
                    $"Protocol entry point '{protocolEntrypoint.Name}' must not subsume " +
                    $"human command entry point '{humanEntrypoint.Name}' on invocation " +
                    "boundary.",
                    nameof(entrypoints));
            }

            if (!AdapterEntrypointBoundaryRelations.IsInvocationCoveredByProtocolUnion(
                    humanEntrypoint,
                    protocolEntrypoints,
                    out AdapterEntrypointDescriptor[] coveringProtocolEntrypoints))
            {
                continue;
            }

            string coveringEntrypointNames =
                FormatEntrypointNames(coveringProtocolEntrypoints);
            throw new ArgumentException(
                $"Protocol entry points {coveringEntrypointNames} must not subsume human " +
                $"command entry point '{humanEntrypoint.Name}' on invocation boundary as " +
                "a union.",
                nameof(entrypoints));
        }
    }

    private static string FormatEntrypointNames(
        IEnumerable<AdapterEntrypointDescriptor> entrypoints)
    {
        return string.Join(
            ", ",
            entrypoints
                .Select(static entrypoint => entrypoint.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .Select(static name => $"'{name}'"));
    }
}
