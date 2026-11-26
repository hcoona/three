using System.Text.Json;
using Authentication.Core;

namespace Authentication.Windows;

// Request-local fixed categories only; never retain an exception or provider value.
internal enum WindowsMechanismFailure
{
    Unavailable, HostPlatform, HostWorkstation, HostThreadToken, HostLogon,
    HostLogonProcessToken, HostLogonTokenStatistics, HostLogonSessionData,
    HostLogonSessionDataSize, HostLogonSessionId, HostLogonSessionSid,
    HostLogonType, HostLogonIdentity, HostStationVisible,
    HostStationUserUnavailable, HostStationUserMismatch,
    HostSession, HostSessionUnavailable, HostSessionZero, HostSessionInactive,
    HostSessionMalformed, HostSessionIdUnavailable, HostSessionQueryUnavailable,
    HostInputDesktop, HostInputDesktopUnavailable, HostInputDesktopNotReceiving,
    DllSearch, BrokerUnavailable,
    BrokerPlatform, BrokerInitialization, RejectedWebUi
}

internal sealed class WindowsMechanismTrace
{
    private int first;
    internal WindowsMechanismFailure Failure => (WindowsMechanismFailure)Volatile.Read(ref first);
    internal void Record(WindowsMechanismFailure failure)
    {
        if (failure > WindowsMechanismFailure.Unavailable && failure <= WindowsMechanismFailure.RejectedWebUi)
            _ = Interlocked.CompareExchange(ref first, (int)failure, 0);
    }
}

internal static class WindowsDiagnostics
{
    internal static byte[] MechanismIndication(WindowsMechanismFailure failure) => failure switch
    {
        WindowsMechanismFailure.HostPlatform => "Mechanism unavailable at host_platform.\n"u8.ToArray(),
        WindowsMechanismFailure.HostWorkstation => "Mechanism unavailable at host_workstation.\n"u8.ToArray(),
        WindowsMechanismFailure.HostThreadToken => "Mechanism unavailable at host_thread_token.\n"u8.ToArray(),
        WindowsMechanismFailure.HostLogon => "Mechanism unavailable at host_logon.\n"u8.ToArray(),
        WindowsMechanismFailure.HostLogonProcessToken => "Mechanism unavailable at host_logon_process_token.\n"u8.ToArray(),
        WindowsMechanismFailure.HostLogonTokenStatistics => "Mechanism unavailable at host_logon_token_statistics.\n"u8.ToArray(),
        WindowsMechanismFailure.HostLogonSessionData => "Mechanism unavailable at host_logon_session_data.\n"u8.ToArray(),
        WindowsMechanismFailure.HostLogonSessionDataSize => "Mechanism unavailable at host_logon_session_data_size.\n"u8.ToArray(),
        WindowsMechanismFailure.HostLogonSessionId => "Mechanism unavailable at host_logon_session_id.\n"u8.ToArray(),
        WindowsMechanismFailure.HostLogonSessionSid => "Mechanism unavailable at host_logon_session_sid.\n"u8.ToArray(),
        WindowsMechanismFailure.HostLogonType => "Mechanism unavailable at host_logon_type.\n"u8.ToArray(),
        WindowsMechanismFailure.HostLogonIdentity => "Mechanism unavailable at host_logon_identity.\n"u8.ToArray(),
        WindowsMechanismFailure.HostStationVisible => "Mechanism unavailable at host_station_visible.\n"u8.ToArray(),
        WindowsMechanismFailure.HostStationUserUnavailable => "Mechanism unavailable at host_station_user_unavailable.\n"u8.ToArray(),
        WindowsMechanismFailure.HostStationUserMismatch => "Mechanism unavailable at host_station_user_mismatch.\n"u8.ToArray(),
        WindowsMechanismFailure.HostSession => "Mechanism unavailable at host_session.\n"u8.ToArray(),
        WindowsMechanismFailure.HostSessionUnavailable => "Mechanism unavailable at host_session_unavailable.\n"u8.ToArray(),
        WindowsMechanismFailure.HostSessionZero => "Mechanism unavailable at host_session_zero.\n"u8.ToArray(),
        WindowsMechanismFailure.HostSessionInactive => "Mechanism unavailable at host_session_inactive.\n"u8.ToArray(),
        WindowsMechanismFailure.HostSessionMalformed => "Mechanism unavailable at host_session_malformed.\n"u8.ToArray(),
        WindowsMechanismFailure.HostSessionIdUnavailable => "Mechanism unavailable at host_session_id_unavailable.\n"u8.ToArray(),
        WindowsMechanismFailure.HostSessionQueryUnavailable => "Mechanism unavailable at host_session_query_unavailable.\n"u8.ToArray(),
        WindowsMechanismFailure.HostInputDesktop => "Mechanism unavailable at host_input_desktop.\n"u8.ToArray(),
        WindowsMechanismFailure.HostInputDesktopUnavailable => "Mechanism unavailable at host_input_desktop_unavailable.\n"u8.ToArray(),
        WindowsMechanismFailure.HostInputDesktopNotReceiving => "Mechanism unavailable at host_input_desktop_not_receiving.\n"u8.ToArray(),
        WindowsMechanismFailure.DllSearch => "Mechanism unavailable at dll_search.\n"u8.ToArray(),
        WindowsMechanismFailure.BrokerUnavailable => "Mechanism unavailable at broker_unavailable.\n"u8.ToArray(),
        WindowsMechanismFailure.BrokerPlatform => "Mechanism unavailable at broker_platform.\n"u8.ToArray(),
        WindowsMechanismFailure.BrokerInitialization => "Mechanism unavailable at broker_initialization.\n"u8.ToArray(),
        WindowsMechanismFailure.RejectedWebUi => "Mechanism unavailable at rejected_web_ui.\n"u8.ToArray(),
        _ => "Mechanism unavailable at unavailable.\n"u8.ToArray(),
    };

    internal static LocalActivityTelemetry? Start(bool telemetry, long entryTimestamp)
    {
        if (!telemetry) return null;
        try { return new(entryTimestamp, bytes => WindowsStandardHandles.Write(WindowsStandardHandles.Error, bytes)); }
        catch (Exception) { return null; }
    }

    // Returns the pending raw stderr write, if any. The process host waits for it,
    // and for local telemetry, only within its remaining shutdown bound.
    internal static Task Completed(SerializedResult result, bool telemetry, long entryTimestamp,
        WindowsMechanismTrace? trace = null, LocalActivityTelemetry? observation = null)
    {
        try
        {
            // Read only our own allowlisted projection. The diagnostic worker
            // receives fixed indications/event bytes, never the result or token.
            using var json = JsonDocument.Parse(result.Utf8Json);
            var outcome = json.RootElement.GetProperty("outcome").GetString();
            using var buffer = new MemoryStream();
            buffer.Write(outcome == "cancelled"
                ? "Authentication request cancelled.\n"u8 : "Authentication request completed.\n"u8);
            if (outcome == "mechanism_unavailable")
                buffer.Write(MechanismIndication(trace?.Failure ?? WindowsMechanismFailure.Unavailable));
            if (telemetry)
            {
                using var writer = new Utf8JsonWriter(buffer);
                writer.WriteStartObject();
                writer.WriteString("event", "request_completed");
                writer.WriteString("stage", "completion");
                writer.WriteString("outcome", outcome);
                writer.WriteNumber("elapsedMilliseconds", (long)Math.Max(0,
                    TimeProvider.System.GetElapsedTime(entryTimestamp).TotalMilliseconds));
                if (outcome == "success")
                {
                    var interpretation = result.Utf8Json.Length <= 262144
                        ? TokenDiagnostics.Describe(json.RootElement.GetProperty("accessToken").GetString())
                        : new TokenDiagnostic(TokenDiagnosticFormat.LimitExceeded, false, false, false);
                    writer.WriteString("tokenFormat", interpretation.Format switch
                    {
                        TokenDiagnosticFormat.DecodedUnverified => "decoded_unverified",
                        TokenDiagnosticFormat.Unreadable => "unreadable",
                        TokenDiagnosticFormat.LimitExceeded => "limit_exceeded", _ => "unavailable",
                    });
                    writer.WriteBoolean("hasExpirationClaim", interpretation.HasExpiration);
                    writer.WriteBoolean("hasAudienceClaim", interpretation.HasAudience);
                    writer.WriteBoolean("hasScopesClaim", interpretation.HasScopes);
                    if (json.RootElement.GetProperty("expiresOn").TryGetDateTimeOffset(out var expiration))
                    {
                        writer.WriteString("providerExpiresOn", expiration);
                        writer.WriteNumber("remainingSeconds", (long)Math.Floor((expiration - DateTimeOffset.UtcNow).TotalSeconds));
                    }
                }
                writer.WriteEndObject();
                writer.Flush();
                buffer.WriteByte((byte)'\n');
            }

            if (buffer.Length > 8192) return Task.CompletedTask;
            var bytes = buffer.ToArray();
            if (observation is not null)
            {
                _ = observation.TryWriteFinal(bytes);
                return Task.CompletedTask;
            }
            return WriteError(bytes);
        }
        catch (Exception)
        {
            // Optional diagnostics cannot alter authentication or process status.
            return Task.CompletedTask;
        }
    }

    // One background stderr write. A blocked sink never holds an authentication or
    // UI thread; the returned task completes when the write ends.
    internal static Task WriteError(byte[] bytes)
    {
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            new Thread(() =>
            {
                try { _ = WindowsStandardHandles.Write(WindowsStandardHandles.Error, bytes); }
                catch (Exception) { }
                finally { written.TrySetResult(); }
            }) { IsBackground = true }.Start();
        }
        catch (Exception) { written.TrySetResult(); }
        return written.Task;
    }
}
