using System.Runtime.ExceptionServices;
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

        diagnosticRouter.PruneClosedActiveCommitTrackingScope();
        DiagnosticCommitTrackingScope? ambientDiagnosticCommitTrackingScope =
            diagnosticRouter.CaptureActiveCommitTrackingScope();

        AdapterInvocationContext? context = null;
        DiagnosticCommitTrackingScope? diagnosticCommitTrackingScope = null;
        var userVisibleOutputCommitted = false;
        try
        {
            context = AdapterHostBootstrap.ResolveInvocation(descriptor, executablePath, arguments);

            diagnosticCommitTrackingScope = diagnosticRouter.BeginUserVisibleCommitTracking(
                validateHumanStdoutSinks: false,
                suppressDirectCredentialCoreSafeDiagnosticRoutes: context.IsProtocolInvocation);
            try
            {
                AdapterHostHandlerOutput handlerOutput = handler(context);
                ArgumentNullException.ThrowIfNull(handlerOutput);

                if (context.IsProtocolInvocation)
                {
                    return ExecuteProtocolInvocation(
                        context,
                        handlerOutput,
                        protocolStdout,
                        diagnosticRouter,
                        diagnosticCommitTrackingScope,
                        ref userVisibleOutputCommitted);
                }

                return ExecuteHumanCommandInvocation(
                    context,
                    handlerOutput,
                    humanStdout,
                    diagnosticRouter,
                    diagnosticCommitTrackingScope,
                    ref userVisibleOutputCommitted);
            }
            finally
            {
                diagnosticCommitTrackingScope.Dispose();
            }
        }
        catch (InvalidOperationException)
        {
            if (context is null)
            {
                if (
                    !HasUserVisibleOutputCommitted(
                        userVisibleOutputCommitted,
                        diagnosticCommitTrackingScope ?? ambientDiagnosticCommitTrackingScope
                    )
                )
                {
                    return CreateFailureOutcome(
                        invocation: null,
                        descriptor.Protocol,
                        AdapterHostExitCode.ConfigurationError,
                        InvocationBoundaryMismatchSafeCode,
                        "Adapter host invocation boundary is unsupported.",
                        diagnosticRouter,
                        diagnosticCommitTrackingScope ?? ambientDiagnosticCommitTrackingScope);
                }

                throw;
            }

            if (
                !HasUserVisibleOutputCommitted(
                    userVisibleOutputCommitted,
                    diagnosticCommitTrackingScope ?? ambientDiagnosticCommitTrackingScope
                )
            )
            {
                return CreateFailureOutcome(
                    context,
                    context.Protocol,
                    AdapterHostExitCode.Fatal,
                    UnhandledHostFailureSafeCode,
                    SafeDiagnosticMessageFallback.GenericMessage,
                    diagnosticRouter,
                    diagnosticCommitTrackingScope ?? ambientDiagnosticCommitTrackingScope);
            }

            throw;
        }
        catch (Exception)
        {
            if (
                !HasUserVisibleOutputCommitted(
                    userVisibleOutputCommitted,
                    diagnosticCommitTrackingScope ?? ambientDiagnosticCommitTrackingScope
                )
            )
            {
                return CreateFailureOutcome(
                    context,
                    context?.Protocol ?? descriptor.Protocol,
                    AdapterHostExitCode.Fatal,
                    UnhandledHostFailureSafeCode,
                    SafeDiagnosticMessageFallback.GenericMessage,
                    diagnosticRouter,
                    diagnosticCommitTrackingScope ?? ambientDiagnosticCommitTrackingScope);
            }

            throw;
        }
    }

    private static AdapterHostExecutionOutcome ExecuteProtocolInvocation(
        AdapterInvocationContext context,
        AdapterHostHandlerOutput handlerOutput,
        TextWriter protocolStdout,
        DiagnosticRouter diagnosticRouter,
        DiagnosticCommitTrackingScope diagnosticCommitTrackingScope,
        ref bool userVisibleOutputCommitted)
    {
        AdapterHostResult result = MapProtocolResult(context, handlerOutput);
        List<DiagnosticEvent> diagnosticEvents = BuildProtocolDiagnosticEvents(
            handlerOutput,
            result);
        string protocolPayload = PrepareProtocolStdout(handlerOutput, result);

        WriteDiagnosticEvents(diagnosticEvents, diagnosticRouter, ref userVisibleOutputCommitted);
        if (result.WriteProtocolStdout)
        {
            WriteUserVisibleText(
                protocolStdout,
                protocolPayload,
                diagnosticCommitTrackingScope,
                ref userVisibleOutputCommitted);
        }

        return new AdapterHostExecutionOutcome(context, result);
    }

    private static AdapterHostExecutionOutcome ExecuteHumanCommandInvocation(
        AdapterInvocationContext context,
        AdapterHostHandlerOutput handlerOutput,
        TextWriter humanStdout,
        DiagnosticRouter diagnosticRouter,
        DiagnosticCommitTrackingScope diagnosticCommitTrackingScope,
        ref bool userVisibleOutputCommitted)
    {
        List<DiagnosticEvent> diagnosticEvents = NormalizeDiagnosticEvents(
            handlerOutput.DiagnosticEvents);
        WriteDiagnosticEvents(
            diagnosticEvents,
            diagnosticRouter,
            ref userVisibleOutputCommitted);
        if (!string.IsNullOrEmpty(handlerOutput.HumanStdout))
        {
            WriteUserVisibleText(
                humanStdout,
                handlerOutput.HumanStdout,
                diagnosticCommitTrackingScope,
                ref userVisibleOutputCommitted);
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
        CredentialError? credentialError = credentialResult?.Error;
        bool useCanonicalValidationDiagnostic = credentialResult is not null
            && IsMapperOwnedValidationDiagnostic(result, operation, credentialResult);
        bool useCredentialCoreFallback = !useCanonicalValidationDiagnostic
            && IsTrustedCredentialCoreDiagnostic(result.SafeDiagnosticCode, credentialError);
        bool allowCodeSpecificFallback = useCanonicalValidationDiagnostic
            || useCredentialCoreFallback;
        SafeDiagnosticFallbackScope fallbackScope = useCredentialCoreFallback
            ? SafeDiagnosticFallbackScope.CredentialCore
            : SafeDiagnosticFallbackScope.AdapterHost;
        CredentialError? safeDiagnosticCredentialError = useCanonicalValidationDiagnostic
            || useCredentialCoreFallback
            || IsCredentialCoreDiagnosticCode(result.SafeDiagnosticCode)
            ? null
            : credentialError;
        try
        {
            string? safeCode = SanitizeSafeDiagnosticCode(result.SafeDiagnosticCode);
            Dictionary<string, string?> properties = CreateSafeDiagnosticProperties(
                safeCode,
                safeDiagnosticCredentialError?.SafeDetails);

            string message = CreateSafeDiagnosticMessage(
                fallbackScope,
                result.SafeDiagnosticCode,
                safeDiagnosticCredentialError?.SafeMessage,
                allowCodeSpecificFallback);

            return new DiagnosticEvent(
                DiagnosticSeverity.Error,
                DiagnosticChannel.Diagnostic,
                message,
                correlationId,
                properties,
                isSafeDiagnosticEnvelope: true)
            {
                AllowCodeSpecificFallback = allowCodeSpecificFallback,
                FallbackScope = fallbackScope,
            };
        }
        catch (Exception)
        {
            return CreateFallbackSafeDiagnosticEvent(
                result,
                correlationId,
                allowCodeSpecificFallback,
                fallbackScope);
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
        SafeDiagnosticFallbackScope fallbackScope,
        string? safeCode,
        string? safeMessage,
        bool allowCodeSpecificFallback = true)
    {
        return SafeDiagnosticMessageFallback.Create(
            fallbackScope,
            safeCode,
            safeMessage,
            allowCodeSpecificFallback);
    }

    private static string CreateSafeDiagnosticMessage(
        string? safeCode,
        string? safeMessage,
        bool allowCodeSpecificFallback = true)
    {
        return CreateSafeDiagnosticMessage(
            SafeDiagnosticFallbackScope.AdapterHost,
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
        bool allowCodeSpecificFallback,
        SafeDiagnosticFallbackScope fallbackScope)
    {
        string message = allowCodeSpecificFallback
            ? GetDefaultSafeDiagnosticMessage(fallbackScope, result.SafeDiagnosticCode)
            : GetGenericSafeDiagnosticMessage(fallbackScope);
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
            AllowCodeSpecificFallback = allowCodeSpecificFallback,
            FallbackScope = fallbackScope,
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

    private static string GetDefaultSafeDiagnosticMessage(
        SafeDiagnosticFallbackScope fallbackScope,
        string? safeCode)
    {
        return SafeDiagnosticMessageFallback.GetDefaultMessage(fallbackScope, safeCode);
    }

    private static string GetGenericSafeDiagnosticMessage(
        SafeDiagnosticFallbackScope fallbackScope)
    {
        return SafeDiagnosticMessageFallback.Create(
            fallbackScope,
            safeCode: null,
            safeMessage: null,
            allowCodeSpecificFallback: false);
    }

    private static bool IsTrustedCredentialCoreDiagnostic(
        string? safeCode,
        CredentialError? credentialError)
    {
        if (credentialError is null
            || string.IsNullOrWhiteSpace(safeCode)
            || !string.Equals(safeCode, credentialError.Code, StringComparison.Ordinal))
        {
            return false;
        }

        return safeCode switch
        {
            "CacheUnavailable" =>
                credentialError.Kind == CredentialErrorKind.CacheUnavailable
                && HasDefaultCredentialCoreSafeMessage(safeCode, credentialError),
            "CredentialCoreFailure" =>
                credentialError.Kind == CredentialErrorKind.Fatal
                && HasDefaultCredentialCoreSafeMessage(safeCode, credentialError),
            "OperationNotSupported" =>
                credentialError.Kind == CredentialErrorKind.CredentialUnavailable
                && HasDefaultCredentialCoreSafeMessage(safeCode, credentialError),
            "ProtocolViolation" =>
                credentialError.Kind == CredentialErrorKind.ProtocolViolation
                && IsTrustedCredentialCoreProtocolViolationDiagnostic(credentialError),
            "TokenExchangeFailed" =>
                credentialError.Kind == CredentialErrorKind.Fatal
                && HasDefaultCredentialCoreSafeMessage(safeCode, credentialError),
            "TokenExchangeUnavailable" =>
                credentialError.Kind == CredentialErrorKind.CredentialUnavailable
                && HasDefaultCredentialCoreSafeMessage(safeCode, credentialError),
            "DirectMsalUnavailable" or "DirectMsalNotImplemented" =>
                credentialError.Kind == CredentialErrorKind.CredentialUnavailable
                && HasDefaultCredentialCoreSafeMessage(safeCode, credentialError),
            "FlowDeferred" =>
                credentialError.Kind == CredentialErrorKind.FlowDeferred
                && HasDefaultCredentialCoreSafeMessage(safeCode, credentialError),
            "FlowDisabled" =>
                credentialError.Kind == CredentialErrorKind.FlowDisabled
                && HasDefaultCredentialCoreSafeMessage(safeCode, credentialError),
            "UnsupportedFlow" =>
                credentialError.Kind == CredentialErrorKind.UnsupportedFlow
                && HasDefaultCredentialCoreSafeMessage(safeCode, credentialError),
            "InteractionBlocked" =>
                credentialError.Kind == CredentialErrorKind.InteractionBlocked
                && HasDefaultCredentialCoreSafeMessage(safeCode, credentialError),
            _ => false,
        };
    }

    private static bool IsCredentialCoreDiagnosticCode(string? safeCode)
    {
        return safeCode switch
        {
            "CredentialIssued" or "CacheUnavailable" or "CredentialCoreFailure"
                or "OperationNotSupported" or "ProtocolViolation" or "TokenExchangeFailed"
                or "TokenExchangeUnavailable" or "DirectMsalUnavailable"
                or "DirectMsalNotImplemented" or "FlowDeferred" or "FlowDisabled"
                or "UnsupportedFlow" or "InteractionBlocked" => true,
            _ => false,
        };
    }

    private static bool HasDefaultCredentialCoreSafeMessage(
        string safeCode,
        CredentialError credentialError)
    {
        return string.Equals(
                credentialError.SafeMessage,
                GetDefaultSafeDiagnosticMessage(
                    SafeDiagnosticFallbackScope.CredentialCore,
                    safeCode),
                StringComparison.Ordinal)
            || HasLegacyCredentialCoreSafeMessageVariant(
                safeCode,
                credentialError.SafeMessage);
    }

    private static bool HasLegacyCredentialCoreSafeMessageVariant(
        string safeCode,
        string? safeMessage)
    {
        return safeCode switch
        {
            "FlowDisabled" => string.Equals(
                safeMessage,
                "Requested identity flow is disabled by the MVP scaffold.",
                StringComparison.Ordinal),
            "UnsupportedFlow" => string.Equals(
                safeMessage,
                "Requested identity flow is not supported by the MVP scaffold.",
                StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool IsTrustedCredentialCoreProtocolViolationDiagnostic(
        CredentialError credentialError)
    {
        return HasDefaultCredentialCoreSafeMessage(ProtocolViolationSafeCode, credentialError)
            || (
                !string.IsNullOrEmpty(credentialError.SafeMessage)
                && credentialError.SafeMessage.StartsWith(
                    "Protocol violation: ",
                    StringComparison.Ordinal)
                && HasCredentialCoreProtocolViolationSafeDetails(credentialError.SafeDetails)
            );
    }

    private static bool HasCredentialCoreProtocolViolationSafeDetails(
        IReadOnlyDictionary<string, string> safeDetails)
    {
        if (safeDetails is null)
        {
            return false;
        }

        try
        {
            bool hasStatusKey = false;
            bool hasProtocolViolationStatus = false;
            bool hasOperationKey = false;
            bool hasEcosystemKey = false;
            bool hasCredentialKindKey = false;
            bool hasIdentityFlowKey = false;
            var inspectedPropertyCount = 0;

            foreach (KeyValuePair<string, string> pair in safeDetails)
            {
                inspectedPropertyCount++;

                switch (pair.Key)
                {
                    case "status":
                        if (hasStatusKey)
                        {
                            return false;
                        }

                        hasStatusKey = true;
                        hasProtocolViolationStatus = string.Equals(
                            pair.Value,
                            CredentialResultStatus.ProtocolViolation.ToString(),
                            StringComparison.Ordinal);
                        break;
                    case "operation":
                        if (hasOperationKey)
                        {
                            return false;
                        }

                        hasOperationKey = true;
                        break;
                    case "ecosystem":
                        if (hasEcosystemKey)
                        {
                            return false;
                        }

                        hasEcosystemKey = true;
                        break;
                    case "credentialKind":
                        if (hasCredentialKindKey)
                        {
                            return false;
                        }

                        hasCredentialKindKey = true;
                        break;
                    case "identityFlow":
                        if (hasIdentityFlowKey)
                        {
                            return false;
                        }

                        hasIdentityFlowKey = true;
                        break;
                }

                if (hasProtocolViolationStatus
                    && hasOperationKey
                    && hasEcosystemKey
                    && hasCredentialKindKey
                    && hasIdentityFlowKey)
                {
                    return true;
                }

                if (inspectedPropertyCount >= MaxSafeDiagnosticPropertyInspectionCount)
                {
                    return false;
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void WriteDiagnosticEvents(
        IEnumerable<DiagnosticEvent> diagnosticEvents,
        DiagnosticRouter diagnosticRouter,
        ref bool userVisibleOutputCommitted)
    {
        foreach (DiagnosticEvent diagnosticEvent in diagnosticEvents)
        {
            try
            {
                userVisibleOutputCommitted |=
                    diagnosticRouter.RouteWithCommitTracking(diagnosticEvent);
            }
            catch (DiagnosticWriteException ex)
            {
                userVisibleOutputCommitted |= ex.OutputCommitted;
                ExceptionDispatchInfo.Capture(ex.OriginalException).Throw();
                throw;
            }
        }
    }

    private static bool HasUserVisibleOutputCommitted(
        bool userVisibleOutputCommitted,
        DiagnosticCommitTrackingScope? diagnosticCommitTrackingScope)
    {
        return userVisibleOutputCommitted
            || (diagnosticCommitTrackingScope?.OutputCommitted ?? false);
    }

    private static void WriteUserVisibleText(
        TextWriter writer,
        string value,
        DiagnosticCommitTrackingScope diagnosticCommitTrackingScope,
        ref bool userVisibleOutputCommitted)
    {
        if (!diagnosticCommitTrackingScope.TryEnterRoute())
        {
            return;
        }

        bool outputCommitted = false;
        try
        {
            object sharedSyncRoot = TextWriterSynchronization.GetWriterSyncRoot(writer);
            using (TextWriterSynchronization.AcquireWriteLock(writer, sharedSyncRoot))
            {
                TextWriterUnicodeScalarWriter.Write(
                    writer,
                    value,
                    ref outputCommitted,
                    trackCommit: true);
                TextWriterSynchronization.FlushUnderSharedLockIfNeeded(writer);
            }
        }
        finally
        {
            diagnosticCommitTrackingScope.CompleteRoute(outputCommitted);
            userVisibleOutputCommitted |= outputCommitted;
        }
    }

    private static AdapterHostExecutionOutcome CreateFailureOutcome(
        AdapterInvocationContext? invocation,
        AdapterProtocol protocol,
        AdapterHostExitCode exitCode,
        string safeDiagnosticCode,
        string safeDiagnosticMessage,
        DiagnosticRouter diagnosticRouter,
        DiagnosticCommitTrackingScope? closedDiagnosticCommitTrackingScope)
    {
        AdapterHostResult result = CreateResult(
            protocol,
            exitCode,
            writeProtocolStdout: false,
            writeDiagnosticStderr: true,
            safeDiagnosticCode);

        string? sanitizedSafeCode = SanitizeSafeDiagnosticCode(safeDiagnosticCode);
        var fallbackDiagnosticOutputCommitted = false;
        try
        {
            fallbackDiagnosticOutputCommitted = diagnosticRouter.RouteWithCommitTracking(
                new DiagnosticEvent(
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
        }
        catch (DiagnosticWriteException ex) when (!ex.OutputCommitted)
        {
            // Zero-byte fallback diagnostic failures must not suppress the safe outcome.
        }
        catch (DiagnosticWriteException ex)
        {
            closedDiagnosticCommitTrackingScope?.RecordCommit(ex.OutputCommitted);
            ExceptionDispatchInfo.Capture(ex.OriginalException).Throw();
            throw;
        }
        catch (Exception)
        {
            // Zero-byte fallback diagnostic failures must not suppress the safe outcome.
        }

        closedDiagnosticCommitTrackingScope?.RecordCommit(fallbackDiagnosticOutputCommitted);
        closedDiagnosticCommitTrackingScope?.SuppressLateCredentialCoreRecovery();

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
