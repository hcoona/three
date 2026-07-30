using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public static class AdapterHostExecutor
{
    private const string InvocationBoundaryMismatchSafeCode = "InvocationBoundaryMismatch";
    private const string ProtocolViolationSafeCode = "ProtocolViolation";
    private const string UnhandledHostFailureSafeCode = "UnhandledHostFailure";
    private const int MaxSafeDiagnosticPropertyCount = 16;

    public static AdapterHostExecutionOutcome Execute(
        AdapterDescriptor descriptor,
        string? executablePath,
        IEnumerable<string>? arguments,
        Func<AdapterInvocationContext, AdapterHostHandlerOutput> handler,
        TextWriter protocolStdout,
        TextWriter humanStdout,
        DiagnosticRouter diagnosticRouter
    )
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(protocolStdout);
        ArgumentNullException.ThrowIfNull(humanStdout);
        ArgumentNullException.ThrowIfNull(diagnosticRouter);

        AdapterInvocationContext? context = null;
        try
        {
            context = AdapterHostBootstrap.ResolveInvocation(descriptor, executablePath, arguments);
            AdapterHostHandlerOutput output =
                handler(context)
                ?? throw new InvalidOperationException("Adapter handler returned no output.");

            return context.IsProtocolInvocation
                ? ExecuteProtocol(context, output, protocolStdout, diagnosticRouter)
                : ExecuteHuman(context, output, humanStdout, diagnosticRouter);
        }
        catch (InvalidOperationException) when (context is null)
        {
            return CreateFailureOutcome(
                invocation: null,
                descriptor.Protocol,
                AdapterHostExitCode.ConfigurationError,
                InvocationBoundaryMismatchSafeCode,
                "Adapter host invocation boundary is unsupported.",
                diagnosticRouter
            );
        }
        catch (Exception)
        {
            return CreateFailureOutcome(
                context,
                context?.Protocol ?? descriptor.Protocol,
                AdapterHostExitCode.Fatal,
                UnhandledHostFailureSafeCode,
                SafeDiagnosticMessageFallback.GenericMessage,
                diagnosticRouter
            );
        }
    }

    private static AdapterHostExecutionOutcome ExecuteProtocol(
        AdapterInvocationContext context,
        AdapterHostHandlerOutput output,
        TextWriter protocolStdout,
        DiagnosticRouter diagnosticRouter
    )
    {
        AdapterHostResult result = MapProtocolResult(context, output);
        if (result.WriteDiagnosticStderr)
        {
            diagnosticRouter.Route(CreateSafeDiagnosticEvent(result, output));
        }

        if (result.WriteProtocolStdout)
        {
            WriteText(protocolStdout, output.ProtocolStdout!);
        }

        return new AdapterHostExecutionOutcome(context, result);
    }

    private static AdapterHostExecutionOutcome ExecuteHuman(
        AdapterInvocationContext context,
        AdapterHostHandlerOutput output,
        TextWriter humanStdout,
        DiagnosticRouter diagnosticRouter
    )
    {
        foreach (DiagnosticEvent diagnosticEvent in output.DiagnosticEvents)
        {
            if (diagnosticEvent.Channel != DiagnosticChannel.ProtocolStdout)
            {
                diagnosticRouter.Route(diagnosticEvent);
            }
        }

        if (!string.IsNullOrEmpty(output.HumanStdout))
        {
            WriteText(humanStdout, output.HumanStdout);
        }

        return new AdapterHostExecutionOutcome(
            context,
            CreateResult(
                AdapterProtocol.Unspecified,
                output.HumanCommandExitCode,
                writeProtocolStdout: false,
                writeDiagnosticStderr: output.DiagnosticEvents.Count != 0,
                safeDiagnosticCode: null
            )
        );
    }

    private static AdapterHostResult MapProtocolResult(
        AdapterInvocationContext context,
        AdapterHostHandlerOutput output
    )
    {
        if (output.CredentialResult is null)
        {
            return CreateResult(
                context.Protocol,
                AdapterHostExitCode.Fatal,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                UnhandledHostFailureSafeCode
            );
        }

        AdapterHostResult result = AdapterHostResultMapper.Map(
            context.Protocol,
            output.Operation,
            output.CredentialResult
        );
        if (result.WriteProtocolStdout && string.IsNullOrEmpty(output.ProtocolStdout))
        {
            return CreateResult(
                context.Protocol,
                AdapterHostExitCode.ConfigurationError,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                ProtocolViolationSafeCode
            );
        }

        return result;
    }

    private static DiagnosticEvent CreateSafeDiagnosticEvent(
        AdapterHostResult result,
        AdapterHostHandlerOutput output
    )
    {
        CredentialError? error = output.CredentialResult?.Error;
        string? safeCode = SafeDiagnosticEnvelopeSanitizer.SanitizeCode(result.SafeDiagnosticCode);
        string safeMessage = SafeDiagnosticMessageFallback.Create(
            result.SafeDiagnosticCode,
            error?.SafeMessage
        );
        var properties = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(safeCode))
        {
            properties[SafeDiagnosticEnvelopeSanitizer.CodePropertyName] = safeCode;
        }

        if (error?.SafeDetails is not null)
        {
            foreach (
                KeyValuePair<string, string> property in error.SafeDetails.Take(
                    MaxSafeDiagnosticPropertyCount - properties.Count
                )
            )
            {
                string key = SafeDiagnosticEnvelopeSanitizer.SanitizePropertyKey(property.Key);
                if (
                    string.IsNullOrEmpty(key)
                    || SafeDiagnosticEnvelopeSanitizer.IsCanonicalCodePropertyKey(key)
                )
                {
                    continue;
                }

                properties[key] = SafeDiagnosticEnvelopeSanitizer.SanitizePropertyValue(
                    property.Value
                );
            }
        }

        CorrelationId? correlationId = CorrelationId.TryParse(
            output.CredentialResult?.DiagnosticsCorrelationId,
            out CorrelationId? parsedCorrelationId
        )
            ? parsedCorrelationId
            : null;
        return new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            safeMessage,
            correlationId,
            properties,
            isSafeDiagnosticEnvelope: true
        )
        {
            AllowCodeSpecificFallback = true,
        };
    }

    private static void WriteText(TextWriter writer, string value)
    {
        TextWriter synchronizedWriter = TextWriter.Synchronized(writer);
        synchronizedWriter.Write(value);
        synchronizedWriter.Flush();
    }

    private static AdapterHostExecutionOutcome CreateFailureOutcome(
        AdapterInvocationContext? invocation,
        AdapterProtocol protocol,
        AdapterHostExitCode exitCode,
        string safeDiagnosticCode,
        string safeDiagnosticMessage,
        DiagnosticRouter diagnosticRouter
    )
    {
        AdapterHostResult result = CreateResult(
            protocol,
            exitCode,
            writeProtocolStdout: false,
            writeDiagnosticStderr: true,
            safeDiagnosticCode
        );

        try
        {
            diagnosticRouter.Route(
                new DiagnosticEvent(
                    DiagnosticSeverity.Error,
                    DiagnosticChannel.Diagnostic,
                    safeDiagnosticMessage,
                    properties: new Dictionary<string, string?>
                    {
                        [SafeDiagnosticEnvelopeSanitizer.CodePropertyName] =
                            SafeDiagnosticEnvelopeSanitizer.SanitizeCode(safeDiagnosticCode),
                    },
                    isSafeDiagnosticEnvelope: true
                )
                {
                    AllowCodeSpecificFallback = true,
                }
            );
        }
        catch (Exception)
        {
            // Diagnostics are best-effort after the adapter result has been determined.
        }

        return new AdapterHostExecutionOutcome(invocation, result);
    }

    private static AdapterHostResult CreateResult(
        AdapterProtocol protocol,
        AdapterHostExitCode exitCode,
        bool writeProtocolStdout,
        bool writeDiagnosticStderr,
        string? safeDiagnosticCode
    )
    {
        return new AdapterHostResult
        {
            Protocol = protocol,
            ExitCode = exitCode,
            WriteProtocolStdout = writeProtocolStdout,
            WriteDiagnosticStderr = writeDiagnosticStderr,
            SafeDiagnosticCode = safeDiagnosticCode,
        };
    }
}
