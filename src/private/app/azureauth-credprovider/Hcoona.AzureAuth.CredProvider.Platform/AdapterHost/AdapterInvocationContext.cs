using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public sealed class AdapterInvocationContext
{
    internal AdapterInvocationContext(
        AdapterDescriptor descriptor,
        AdapterEntrypointDescriptor entrypoint,
        string? executablePath,
        string? executableName,
        IReadOnlyList<string> rawArguments,
        IReadOnlyList<string> matchedArguments,
        IReadOnlyList<string> payloadArguments)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(entrypoint);
        ArgumentNullException.ThrowIfNull(rawArguments);
        ArgumentNullException.ThrowIfNull(matchedArguments);
        ArgumentNullException.ThrowIfNull(payloadArguments);

        Descriptor = descriptor;
        Entrypoint = entrypoint;
        Protocol = entrypoint.ResolveProtocol(descriptor.Protocol);
        ExecutablePath = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath;
        ExecutableName = executableName;
        RawArguments = rawArguments;
        MatchedArguments = matchedArguments;
        PayloadArguments = payloadArguments;
    }

    public AdapterDescriptor Descriptor { get; }

    public AdapterEntrypointDescriptor Entrypoint { get; }

    public AdapterInvocationMode Mode => Entrypoint.Mode;

    public AdapterProtocol Protocol { get; }

    public string? ExecutablePath { get; }

    public string? ExecutableName { get; }

    public IReadOnlyList<string> RawArguments { get; }

    public IReadOnlyList<string> MatchedArguments { get; }

    public IReadOnlyList<string> PayloadArguments { get; }

    public bool IsProtocolInvocation => Mode == AdapterInvocationMode.Protocol;

    public bool IsHumanCommandInvocation => Mode == AdapterInvocationMode.HumanCommand;
}
