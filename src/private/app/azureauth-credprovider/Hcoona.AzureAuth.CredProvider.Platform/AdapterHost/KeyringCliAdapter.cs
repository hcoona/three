using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public sealed class KeyringCliAdapter
{
    public const string CommandName = "keyring";

    private const string GetVerb = "get";
    private const string JsonOutput = "json";
    private const string PlainOutput = "plain";

    private readonly KeyringHelperAdapter helperAdapter;

    public KeyringCliAdapter()
        : this(CredentialProviderCompositionRoot.CreateProduction().AcquisitionService) { }

    public KeyringCliAdapter(CredentialCoreService? credentialCore)
        : this(
            credentialCore is null
                ? CredentialProviderCompositionRoot.CreateProduction().AcquisitionService
                : new LegacyV1CredentialAcquisitionService(credentialCore)
        )
    { }

    public KeyringCliAdapter(ICredentialAcquisitionService credentialAcquisition)
    {
        ArgumentNullException.ThrowIfNull(credentialAcquisition);
        helperAdapter = new KeyringHelperAdapter(credentialAcquisition);
    }

    public KeyringCliAdapter(BoundedCredentialAcquisitionAdapter credentialAcquisition)
    {
        ArgumentNullException.ThrowIfNull(credentialAcquisition);
        helperAdapter = new KeyringHelperAdapter(credentialAcquisition);
    }

    public static AdapterDescriptor Descriptor { get; } = CreateDescriptor();

    public static bool TryResolveProtocolInvocation(
        string? executablePath,
        IEnumerable<string>? arguments,
        out AdapterInvocationContext? context
    )
    {
        bool resolved = AdapterHostBootstrap.TryResolveInvocation(
            Descriptor,
            executablePath,
            arguments,
            out context
        );
        if (!resolved || context is null || !context.IsProtocolInvocation)
        {
            context = null;
            return false;
        }

        return true;
    }

    public static bool IsUnsupportedServiceInvocation(AdapterInvocationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return TryParseRequest(context.PayloadArguments, out KeyringCliRequest? request)
            && Uri.TryCreate(request.Service, UriKind.Absolute, out Uri? service)
            && !string.IsNullOrEmpty(service.Host)
            && !IsPotentialAzureArtifactsHost(service.IdnHost);
    }

    public AdapterHostExecutionOutcome Execute(
        string? executablePath,
        IEnumerable<string>? arguments,
        TextWriter protocolStdout,
        TextWriter humanStdout,
        DiagnosticRouter diagnosticRouter
    )
    {
        ArgumentNullException.ThrowIfNull(protocolStdout);
        ArgumentNullException.ThrowIfNull(humanStdout);
        ArgumentNullException.ThrowIfNull(diagnosticRouter);

        _ = humanStdout;
        AdapterInvocationContext? context = null;
        if (
            !TryResolveProtocolInvocation(executablePath, arguments, out context)
            || context is null
        )
        {
            return ExecuteHelper(
                context,
                ["set"],
                output: PlainOutput,
                mode: KeyringHelperMode.Password,
                protocolStdout,
                diagnosticRouter
            );
        }

        if (!TryParseRequest(context.PayloadArguments, out KeyringCliRequest? request))
        {
            return ExecuteHelper(
                context,
                ["set"],
                output: PlainOutput,
                mode: KeyringHelperMode.Password,
                protocolStdout,
                diagnosticRouter
            );
        }

        return ExecuteHelper(
            context,
            BuildHelperArguments(request),
            request.Output,
            request.Mode,
            protocolStdout,
            diagnosticRouter
        );
    }

    private static AdapterDescriptor CreateDescriptor()
    {
        AdapterEntrypointDescriptor sharedCliEntrypoint = new(
            "KeyringCli",
            AdapterInvocationMode.Protocol,
            executableNames: [KeyringHelperAdapter.ProductExecutableName],
            argumentTokens: [CommandName],
            argumentMatchMode: AdapterArgumentMatchMode.Prefix
        );
        AdapterEntrypointDescriptor dedicatedExecutableEntrypoint = new(
            "KeyringCliExecutable",
            AdapterInvocationMode.Protocol,
            executableNames: [CommandName]
        );

        return new AdapterDescriptor(
            "Keyring CLI",
            AdapterProtocol.KeyringHelper,
            [sharedCliEntrypoint, dedicatedExecutableEntrypoint]
        );
    }

    private AdapterHostExecutionOutcome ExecuteHelper(
        AdapterInvocationContext? context,
        IReadOnlyList<string> helperArguments,
        string output,
        KeyringHelperMode mode,
        TextWriter protocolStdout,
        DiagnosticRouter diagnosticRouter
    )
    {
        using var helperStdout = new StringWriter(CultureInfo.InvariantCulture);
        using var helperHumanStdout = new StringWriter(CultureInfo.InvariantCulture);
        AdapterHostExecutionOutcome helperOutcome = helperAdapter.Execute(
            KeyringHelperV2.CommandName,
            helperArguments,
            helperStdout,
            helperHumanStdout,
            diagnosticRouter
        );
        if (helperOutcome.Result.ExitCode == AdapterHostExitCode.Success)
        {
            WriteText(
                protocolStdout,
                output == JsonOutput
                    ? ConvertToJson(helperStdout.ToString(), mode)
                    : helperStdout.ToString()
            );
        }

        return new AdapterHostExecutionOutcome(context, helperOutcome.Result);
    }

    private static bool TryParseRequest(
        IReadOnlyList<string> arguments,
        [NotNullWhen(true)] out KeyringCliRequest? request
    )
    {
        request = null;
        int index = 0;
        string mode = "password";
        string output = PlainOutput;
        while (
            index < arguments.Count
            && arguments[index].StartsWith("--", StringComparison.Ordinal)
        )
        {
            string option = arguments[index];
            if (option.StartsWith("--mode=", StringComparison.Ordinal))
            {
                mode = option["--mode=".Length..];
            }
            else if (option.StartsWith("--output=", StringComparison.Ordinal))
            {
                output = option["--output=".Length..];
            }
            else
            {
                return false;
            }

            index++;
        }

        if (
            arguments.Count - index < 2
            || !string.Equals(arguments[index], GetVerb, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(arguments[index + 1])
            || !string.Equals(
                arguments[index + 1],
                arguments[index + 1].Trim(),
                StringComparison.Ordinal
            )
            || arguments[index + 1].Any(char.IsControl)
        )
        {
            return false;
        }

        string service = arguments[index + 1];
        index += 2;
        string? username = null;
        if (
            index < arguments.Count
            && arguments[index].StartsWith("--", StringComparison.Ordinal)
            && !string.Equals(arguments[index], "--mode", StringComparison.Ordinal)
        )
        {
            return false;
        }
        if (
            index < arguments.Count
            && !string.Equals(arguments[index], "--mode", StringComparison.Ordinal)
        )
        {
            username = arguments[index];
            if (IsInvalidUsername(username))
            {
                return false;
            }
            index++;
        }

        if (index < arguments.Count)
        {
            if (
                arguments.Count - index != 2
                || !string.Equals(arguments[index], "--mode", StringComparison.Ordinal)
            )
            {
                return false;
            }

            mode = arguments[index + 1];
            index += 2;
        }

        KeyringHelperMode parsedMode = mode switch
        {
            "password" => KeyringHelperMode.Password,
            "creds" => KeyringHelperMode.Credentials,
            _ => KeyringHelperMode.Unspecified,
        };
        if (
            index != arguments.Count
            || parsedMode == KeyringHelperMode.Unspecified
            || output is not (PlainOutput or JsonOutput)
            || (parsedMode == KeyringHelperMode.Password && username is null)
        )
        {
            return false;
        }

        request = new KeyringCliRequest(service, username, parsedMode, output);
        return true;
    }

    private static List<string> BuildHelperArguments(KeyringCliRequest request)
    {
        var arguments = new List<string>
        {
            KeyringHelperV2.GetVerb,
            "--protocol-version",
            ContractVersions.KeyringHelperMajor.ToString(CultureInfo.InvariantCulture),
            "--service",
            request.Service,
        };
        if (request.Username is not null)
        {
            arguments.Add("--username");
            arguments.Add(request.Username);
        }

        arguments.Add("--mode");
        arguments.Add(
            request.Mode == KeyringHelperMode.Credentials ? "creds" : "password"
        );
        return arguments;
    }

    private static string ConvertToJson(string helperStdout, KeyringHelperMode mode)
    {
        string[] records = helperStdout.Split('\n');
        if (records.Length > 0 && records[^1].Length == 0)
        {
            records = records[..^1];
        }

        return mode switch
        {
            KeyringHelperMode.Password when records.Length == 1 =>
                "{\"password\":" + EncodeJsonString(records[0]) + "}\n",
            KeyringHelperMode.Credentials when records.Length == 2 =>
                "{\"username\":"
                + EncodeJsonString(records[0])
                + ",\"password\":"
                + EncodeJsonString(records[1])
                + "}\n",
            _ => throw new InvalidOperationException(
                "Keyring helper success output does not match the requested mode."
            ),
        };
    }

    private static string EncodeJsonString(string value) =>
        "\"" + JsonEncodedText.Encode(value).ToString() + "\"";

    private static bool IsInvalidUsername(string? username) =>
        username is not null
        && (string.IsNullOrWhiteSpace(username) || username.Any(char.IsControl));

    private static bool IsPotentialAzureArtifactsHost(string host)
    {
        if (
            string.Equals(host, "pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        return HasSingleLabelPrefix(host, ".pkgs.visualstudio.com")
            || HasSingleLabelPrefix(host, ".visualstudio.com");
    }

    private static bool HasSingleLabelPrefix(string host, string suffix)
    {
        if (
            !host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            || host.Length <= suffix.Length
        )
        {
            return false;
        }

        string prefix = host[..^suffix.Length];
        return !string.IsNullOrWhiteSpace(prefix)
            && !prefix.Contains('.', StringComparison.Ordinal);
    }

    private static void WriteText(TextWriter writer, string value)
    {
        TextWriter synchronizedWriter = TextWriter.Synchronized(writer);
        synchronizedWriter.Write(value);
        synchronizedWriter.Flush();
    }

    private sealed record KeyringCliRequest(
        string Service,
        string? Username,
        KeyringHelperMode Mode,
        string Output
    );

}
