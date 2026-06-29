using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public static class AdapterHostExecutor
{
    private const string InvocationBoundaryMismatchSafeCode = "InvocationBoundaryMismatch";
    private const string ProtocolViolationSafeCode = "ProtocolViolation";
    private const string UnhandledHostFailureSafeCode = "UnhandledHostFailure";
    private const string SafeDiagnosticCodePropertyName =
        SafeDiagnosticEnvelopeSanitizer.CodePropertyName;
    private const int MaxSafeDiagnosticPropertyCount = 16;
    private const int MaxSafeDiagnosticPropertyInspectionCount =
        MaxSafeDiagnosticPropertyCount
        * SafeDiagnosticEnvelopeSanitizer.InspectionMultiplier;

    public static AdapterHostExecutionOutcome Execute(
        AdapterDescriptor descriptor,
        string? executablePath,
        IEnumerable<string>? arguments,
        Func<AdapterInvocationContext, AdapterHostHandlerOutput> handler,
        TextWriter protocolStdout,
        TextWriter humanStdout,
        DiagnosticRouter diagnosticRouter)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(protocolStdout);
        ArgumentNullException.ThrowIfNull(humanStdout);
        ArgumentNullException.ThrowIfNull(diagnosticRouter);

        AdapterInvocationContext? context = null;
        var userVisibleOutputStarted = false;
        try
        {
            context = AdapterHostBootstrap.ResolveInvocation(descriptor, executablePath, arguments);

            AdapterHostHandlerOutput handlerOutput = handler(context);
            ArgumentNullException.ThrowIfNull(handlerOutput);

            if (context.IsProtocolInvocation)
            {
                return ExecuteProtocolInvocation(
                    context,
                    handlerOutput,
                    protocolStdout,
                    diagnosticRouter,
                    ref userVisibleOutputStarted);
            }

            return ExecuteHumanCommandInvocation(
                context,
                handlerOutput,
                humanStdout,
                diagnosticRouter,
                ref userVisibleOutputStarted);
        }
        catch (InvalidOperationException)
            when (context is null && !userVisibleOutputStarted)
        {
            return CreateFailureOutcome(
                invocation: null,
                descriptor.Protocol,
                AdapterHostExitCode.ConfigurationError,
                InvocationBoundaryMismatchSafeCode,
                "Adapter host invocation boundary is unsupported.",
                diagnosticRouter);
        }
        catch (Exception) when (!userVisibleOutputStarted)
        {
            return CreateFailureOutcome(
                context,
                context?.Protocol ?? descriptor.Protocol,
                AdapterHostExitCode.Fatal,
                UnhandledHostFailureSafeCode,
                SafeDiagnosticMessageFallback.GenericMessage,
                diagnosticRouter);
        }
    }

    private static AdapterHostExecutionOutcome ExecuteProtocolInvocation(
        AdapterInvocationContext context,
        AdapterHostHandlerOutput handlerOutput,
        TextWriter protocolStdout,
        DiagnosticRouter diagnosticRouter,
        ref bool userVisibleOutputStarted)
    {
        AdapterHostResult result = MapProtocolResult(context, handlerOutput);
        List<DiagnosticEvent> diagnosticEvents = BuildProtocolDiagnosticEvents(
            handlerOutput,
            result);
        string protocolPayload = PrepareProtocolStdout(handlerOutput, result);

        WriteDiagnosticEvents(diagnosticEvents, diagnosticRouter, ref userVisibleOutputStarted);
        if (result.WriteProtocolStdout)
        {
            userVisibleOutputStarted = true;
            protocolStdout.Write(protocolPayload);
        }

        return new AdapterHostExecutionOutcome(context, result);
    }

    private static AdapterHostExecutionOutcome ExecuteHumanCommandInvocation(
        AdapterInvocationContext context,
        AdapterHostHandlerOutput handlerOutput,
        TextWriter humanStdout,
        DiagnosticRouter diagnosticRouter,
        ref bool userVisibleOutputStarted)
    {
        List<DiagnosticEvent> diagnosticEvents = NormalizeDiagnosticEvents(
            handlerOutput.DiagnosticEvents);
        WriteDiagnosticEvents(diagnosticEvents, diagnosticRouter, ref userVisibleOutputStarted);
        if (!string.IsNullOrEmpty(handlerOutput.HumanStdout))
        {
            userVisibleOutputStarted = true;
            humanStdout.Write(handlerOutput.HumanStdout);
        }

        return new AdapterHostExecutionOutcome(
            context,
            CreateResult(
                AdapterProtocol.Unspecified,
                handlerOutput.HumanCommandExitCode,
                writeProtocolStdout: false,
                writeDiagnosticStderr: diagnosticEvents.Count != 0,
                safeDiagnosticCode: null));
    }

    private static AdapterHostResult MapProtocolResult(
        AdapterInvocationContext context,
        AdapterHostHandlerOutput handlerOutput)
    {
        if (handlerOutput.CredentialResult is null)
        {
            return CreateResult(
                context.Protocol,
                AdapterHostExitCode.Fatal,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                safeDiagnosticCode: UnhandledHostFailureSafeCode);
        }

        AdapterHostResult result = AdapterHostResultMapper.Map(
            context.Protocol,
            handlerOutput.Operation,
            handlerOutput.CredentialResult);
        if (result.WriteProtocolStdout && string.IsNullOrEmpty(handlerOutput.ProtocolStdout))
        {
            return CreateResult(
                context.Protocol,
                AdapterHostExitCode.ConfigurationError,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                safeDiagnosticCode: ProtocolViolationSafeCode);
        }

        return result;
    }

    private static string PrepareProtocolStdout(
        AdapterHostHandlerOutput handlerOutput,
        AdapterHostResult result)
    {
        return result.WriteProtocolStdout
            ? handlerOutput.ProtocolStdout ?? string.Empty
            : string.Empty;
    }

    private static List<DiagnosticEvent> BuildProtocolDiagnosticEvents(
        AdapterHostHandlerOutput handlerOutput,
        AdapterHostResult result)
    {
        if (!result.WriteDiagnosticStderr)
        {
            return [];
        }

        return
        [
            CreateSafeDiagnosticEvent(
                result,
                handlerOutput.Operation,
                handlerOutput.CredentialResult)
        ];
    }

    private static DiagnosticEvent CreateSafeDiagnosticEvent(
        AdapterHostResult result,
        CredentialOperation operation,
        CredentialResult? credentialResult)
    {
        CorrelationId? correlationId = TryGetCorrelationId(
            credentialResult?.DiagnosticsCorrelationId);
        bool useCanonicalValidationDiagnostic = credentialResult is not null
            && IsMapperOwnedValidationDiagnostic(result, operation, credentialResult);
        try
        {
            string? safeCode = SanitizeSafeDiagnosticCode(result.SafeDiagnosticCode);
            Dictionary<string, string?> properties = CreateSafeDiagnosticProperties(
                safeCode,
                useCanonicalValidationDiagnostic
                    ? null
                    : credentialResult?.Error?.SafeDetails);

            string message = CreateSafeDiagnosticMessage(
                result.SafeDiagnosticCode,
                useCanonicalValidationDiagnostic
                    ? null
                    : credentialResult?.Error?.SafeMessage,
                useCanonicalValidationDiagnostic);

            return new DiagnosticEvent(
                DiagnosticSeverity.Error,
                DiagnosticChannel.Diagnostic,
                message,
                correlationId,
                properties,
                isSafeDiagnosticEnvelope: true)
            {
                AllowCodeSpecificFallback = useCanonicalValidationDiagnostic,
            };
        }
        catch (Exception)
        {
            return CreateFallbackSafeDiagnosticEvent(
                result,
                correlationId,
                useCanonicalValidationDiagnostic);
        }
    }

    private static List<DiagnosticEvent> NormalizeDiagnosticEvents(
        IEnumerable<DiagnosticEvent> diagnosticEvents)
    {
        var normalizedDiagnosticEvents = new List<DiagnosticEvent>();
        foreach (DiagnosticEvent diagnosticEvent in diagnosticEvents)
        {
            if (diagnosticEvent.Channel == DiagnosticChannel.ProtocolStdout)
            {
                continue;
            }

            normalizedDiagnosticEvents.Add(
                diagnosticEvent.Channel == DiagnosticChannel.Diagnostic
                    && !diagnosticEvent.IsSafeDiagnosticEnvelope
                    ? diagnosticEvent
                    : new DiagnosticEvent(
                        diagnosticEvent.Severity,
                        DiagnosticChannel.Diagnostic,
                        diagnosticEvent.Message,
                        diagnosticEvent.CorrelationId,
                        diagnosticEvent.Properties,
                        diagnosticEvent.Timestamp));
        }

        return normalizedDiagnosticEvents;
    }

    private static Dictionary<string, string?> CreateSafeDiagnosticProperties(
        string? safeCode,
        IReadOnlyDictionary<string, string>? safeDetails)
    {
        var properties = new Dictionary<string, string?>(StringComparer.Ordinal);
        int remainingPropertyCapacity = MaxSafeDiagnosticPropertyCount;
        if (!string.IsNullOrEmpty(safeCode))
        {
            remainingPropertyCapacity--;
        }

        TryAddSafeDiagnosticProperties(
            properties,
            remainingPropertyCapacity,
            safeDetails);

        if (!string.IsNullOrEmpty(safeCode))
        {
            properties[SafeDiagnosticCodePropertyName] = safeCode;
        }

        return properties;
    }

    private static void TryAddSafeDiagnosticProperties(
        Dictionary<string, string?> properties,
        int remainingPropertyCapacity,
        IReadOnlyDictionary<string, string>? safeDetails)
    {
        if (safeDetails is null)
        {
            return;
        }

        try
        {
            int inspectedPropertyCount = 0;
            foreach (KeyValuePair<string, string> pair in safeDetails)
            {
                if (properties.Count >= remainingPropertyCapacity
                    || inspectedPropertyCount >= MaxSafeDiagnosticPropertyInspectionCount)
                {
                    break;
                }

                inspectedPropertyCount++;

                string key = SanitizeSafeDiagnosticPropertyKey(pair.Key);
                if (string.IsNullOrEmpty(key) || IsReservedSafeDiagnosticPropertyKey(key))
                {
                    continue;
                }

                if (pair.Value is null)
                {
                    continue;
                }

                properties[key] = SanitizeSafeDiagnosticPropertyValue(pair.Value);
            }
        }
        catch (Exception)
        {
            // Preserve the mapped failure envelope when producer-owned safe-details
            // metadata is malformed or concurrently mutated during enumeration.
        }
    }

    private static bool IsMapperOwnedValidationDiagnostic(
        AdapterHostResult result,
        CredentialOperation operation,
        CredentialResult credentialResult)
    {
        return result.SafeDiagnosticCode switch
        {
            "UnsupportedAdapterProtocol" => result.Protocol == AdapterProtocol.Unspecified,
            "UnsupportedContractMajor" => credentialResult.ContractMajor
                != ContractVersions.CredentialContractMajor,
            "UnsupportedCacheKeySchemaMajor" => credentialResult.CacheKey is not null
                && !CacheKeySchema.IsValid(credentialResult.CacheKey),
            ProtocolViolationSafeCode => credentialResult.Status == CredentialResultStatus.Success
                || (
                    result.Protocol == AdapterProtocol.GitCredentialHelper
                    && !IsSupportedGitCredentialHelperOperation(operation)
                ),
            _ => false,
        };
    }

    private static bool IsSupportedGitCredentialHelperOperation(CredentialOperation operation) =>
        operation
            is CredentialOperation.Get
                or CredentialOperation.Store
                or CredentialOperation.Erase;

    private static string CreateSafeDiagnosticMessage(
        string? safeCode,
        string? safeMessage,
        bool allowCodeSpecificFallback = true)
    {
        return SafeDiagnosticMessageFallback.Create(
            safeCode,
            safeMessage,
            allowCodeSpecificFallback);
    }

    private static string? SanitizeSafeDiagnosticCode(string? safeCode)
    {
        return SafeDiagnosticEnvelopeSanitizer.SanitizeCode(safeCode);
    }

    private static string SanitizeSafeDiagnosticPropertyKey(string key)
    {
        return SafeDiagnosticEnvelopeSanitizer.SanitizePropertyKey(key);
    }

    private static bool IsReservedSafeDiagnosticPropertyKey(string key)
    {
        return SafeDiagnosticEnvelopeSanitizer.IsReservedPropertyKey(key);
    }

    private static string SanitizeSafeDiagnosticPropertyValue(string value)
    {
        return SafeDiagnosticEnvelopeSanitizer.SanitizePropertyValue(value)
            ?? string.Empty;
    }

    private static DiagnosticEvent CreateFallbackSafeDiagnosticEvent(
        AdapterHostResult result,
        CorrelationId? correlationId,
        bool useCanonicalValidationDiagnostic)
    {
        string message = useCanonicalValidationDiagnostic
            ? GetDefaultSafeDiagnosticMessage(result.SafeDiagnosticCode)
            : SafeDiagnosticMessageFallback.GenericMessage;
        var properties = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? safeCode = TryGetPassthroughSafeDiagnosticCode(result.SafeDiagnosticCode);
        if (!string.IsNullOrEmpty(safeCode))
        {
            properties[SafeDiagnosticCodePropertyName] = safeCode;
        }

        return new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            message,
            correlationId,
            properties,
            isSafeDiagnosticEnvelope: true)
        {
            AllowCodeSpecificFallback = useCanonicalValidationDiagnostic,
        };
    }

    private static string? TryGetPassthroughSafeDiagnosticCode(string? safeCode)
    {
        return SafeDiagnosticEnvelopeSanitizer.TryGetPassthroughCode(safeCode);
    }

    private static CorrelationId? TryGetCorrelationId(string? diagnosticsCorrelationId)
    {
        return CorrelationId.TryParse(diagnosticsCorrelationId, out CorrelationId? correlationId)
            ? correlationId
            : null;
    }

    private static string GetDefaultSafeDiagnosticMessage(string? safeCode)
    {
        return SafeDiagnosticMessageFallback.GetDefaultMessage(safeCode);
    }

    private static void WriteDiagnosticEvents(
        IEnumerable<DiagnosticEvent> diagnosticEvents,
        DiagnosticRouter diagnosticRouter,
        ref bool userVisibleOutputStarted)
    {
        foreach (DiagnosticEvent diagnosticEvent in diagnosticEvents)
        {
            userVisibleOutputStarted = true;
            diagnosticRouter.Route(diagnosticEvent);
        }
    }

    private static AdapterHostExecutionOutcome CreateFailureOutcome(
        AdapterInvocationContext? invocation,
        AdapterProtocol protocol,
        AdapterHostExitCode exitCode,
        string safeDiagnosticCode,
        string safeDiagnosticMessage,
        DiagnosticRouter diagnosticRouter)
    {
        AdapterHostResult result = CreateResult(
            protocol,
            exitCode,
            writeProtocolStdout: false,
            writeDiagnosticStderr: true,
            safeDiagnosticCode);

        string? sanitizedSafeCode = SanitizeSafeDiagnosticCode(safeDiagnosticCode);
        diagnosticRouter.Route(new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            CreateSafeDiagnosticMessage(safeDiagnosticCode, safeDiagnosticMessage),
            properties: new Dictionary<string, string?>
            {
                [SafeDiagnosticCodePropertyName] = sanitizedSafeCode,
            },
            isSafeDiagnosticEnvelope: true)
        {
            AllowCodeSpecificFallback = true,
        });

        return new AdapterHostExecutionOutcome(invocation, result);
    }

    private static AdapterHostResult CreateResult(
        AdapterProtocol protocol,
        AdapterHostExitCode exitCode,
        bool writeProtocolStdout,
        bool writeDiagnosticStderr,
        string? safeDiagnosticCode)
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
