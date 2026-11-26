using System.Runtime.InteropServices;
using Authentication.Core;

namespace Authentication.Windows;

// This observation boundary supplies only the local facts used by admission. The
// native implementation owns its temporary handles/buffers and never exports them.
internal interface IWindowsHostObservations
{
    WindowsHostPlatform ReadPlatform(CancellationToken cancellationToken);
    bool? ReadWorkstationProduct(CancellationToken cancellationToken);
    WindowsThreadIdentity ReadThreadIdentity(CancellationToken cancellationToken);
    WindowsLocalLogon? ReadOwnLogonAndStation(CancellationToken cancellationToken);
    WindowsSessionConnection ReadSessionConnection(CancellationToken cancellationToken);
    bool? ReadInputDesktop(CancellationToken cancellationToken);
}

internal sealed record WindowsHostPlatform(bool IsWindows, Architecture ProcessArchitecture,
    Architecture OsArchitecture, Version Version);

internal enum WindowsThreadIdentity { NoToken, Impersonating, Unavailable }
internal enum WindowsLogonIdentity { Invalid, User, LocalSystem, LocalService, NetworkService }
internal enum WindowsSessionConnection { Unavailable, ZeroSession, Active, Inactive, Malformed, SessionIdUnavailable, QueryUnavailable }
internal enum WindowsLogonReadFailure { None, ProcessToken, TokenStatistics, SessionData, SessionDataSize, SessionId, SessionSid }

internal sealed record WindowsLocalLogon(uint LogonType, WindowsLogonIdentity Identity,
    bool VisibleStation, bool? StationUserMatches, WindowsLogonReadFailure ReadFailure = WindowsLogonReadFailure.None);

// Classifies synchronous local observations before initialization and later effects.
// Construction remains inert; the caller supplies the observation implementation.
internal sealed class WindowsHostAdmission : IWindowsHostAdmission
{
    private readonly IWindowsHostObservations observations;
    private readonly WindowsMechanismTrace? trace;

    internal WindowsHostAdmission(IWindowsHostObservations observations, WindowsMechanismTrace? trace = null)
    {
        ArgumentNullException.ThrowIfNull(observations);
        this.observations = observations;
        this.trace = trace;
    }

    public void Admit(CancellationToken cancellationToken)
    {
        var platform = Observe(observations.ReadPlatform, cancellationToken);
        Require(platform.IsWindows && platform.ProcessArchitecture == Architecture.X64
            && platform.OsArchitecture == Architecture.X64
            && platform.Version.Major == 10 && platform.Version.Minor == 0
            && platform.Version.Build >= 22000, WindowsMechanismFailure.HostPlatform);
        Require(Observe(observations.ReadWorkstationProduct, cancellationToken) == true, WindowsMechanismFailure.HostWorkstation);
        Require(Observe(observations.ReadThreadIdentity, cancellationToken) == WindowsThreadIdentity.NoToken, WindowsMechanismFailure.HostThreadToken);

        var logon = Observe(observations.ReadOwnLogonAndStation, cancellationToken);
        Require(logon is not null, WindowsMechanismFailure.HostLogon);
        Require(logon!.ReadFailure == WindowsLogonReadFailure.None, logon.ReadFailure switch
        {
            WindowsLogonReadFailure.ProcessToken => WindowsMechanismFailure.HostLogonProcessToken,
            WindowsLogonReadFailure.TokenStatistics => WindowsMechanismFailure.HostLogonTokenStatistics,
            WindowsLogonReadFailure.SessionData => WindowsMechanismFailure.HostLogonSessionData,
            WindowsLogonReadFailure.SessionDataSize => WindowsMechanismFailure.HostLogonSessionDataSize,
            WindowsLogonReadFailure.SessionId => WindowsMechanismFailure.HostLogonSessionId,
            WindowsLogonReadFailure.SessionSid => WindowsMechanismFailure.HostLogonSessionSid,
            _ => WindowsMechanismFailure.HostLogon,
        });
        Require(logon.LogonType is 2 or 10 or 11 or 12, WindowsMechanismFailure.HostLogonType);
        Require(logon.Identity == WindowsLogonIdentity.User, WindowsMechanismFailure.HostLogonIdentity);
        Require(logon.VisibleStation, WindowsMechanismFailure.HostStationVisible);
        Require(logon.StationUserMatches.HasValue, WindowsMechanismFailure.HostStationUserUnavailable);
        Require(logon.StationUserMatches == true, WindowsMechanismFailure.HostStationUserMismatch);

        RequireSession(cancellationToken);
        RequireInputDesktop(cancellationToken);
        RequireSession(cancellationToken); // OpenInputDesktop also opens a disconnected session's future desktop.
    }

    public void Recheck(CancellationToken cancellationToken)
    {
        Require(Observe(observations.ReadThreadIdentity, cancellationToken) == WindowsThreadIdentity.NoToken, WindowsMechanismFailure.HostThreadToken);
        RequireSession(cancellationToken);
        RequireInputDesktop(cancellationToken);
        RequireSession(cancellationToken); // OpenInputDesktop also opens a disconnected session's future desktop.
    }

    private void RequireSession(CancellationToken cancellationToken)
    {
        var session = Observe(observations.ReadSessionConnection, cancellationToken);
        Require(session == WindowsSessionConnection.Active, session switch
        {
            WindowsSessionConnection.Unavailable => WindowsMechanismFailure.HostSessionUnavailable,
            WindowsSessionConnection.ZeroSession => WindowsMechanismFailure.HostSessionZero,
            WindowsSessionConnection.Inactive => WindowsMechanismFailure.HostSessionInactive,
            WindowsSessionConnection.Malformed => WindowsMechanismFailure.HostSessionMalformed,
            WindowsSessionConnection.SessionIdUnavailable => WindowsMechanismFailure.HostSessionIdUnavailable,
            WindowsSessionConnection.QueryUnavailable => WindowsMechanismFailure.HostSessionQueryUnavailable,
            _ => WindowsMechanismFailure.HostSession,
        });
    }

    private void RequireInputDesktop(CancellationToken cancellationToken)
    {
        var receivesInput = Observe(observations.ReadInputDesktop, cancellationToken);
        Require(receivesInput == true, receivesInput.HasValue
            ? WindowsMechanismFailure.HostInputDesktopNotReceiving
            : WindowsMechanismFailure.HostInputDesktopUnavailable);
    }

    private static T Observe<T>(Func<CancellationToken, T> observation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        T result;
        try
        {
            result = observation(cancellationToken);
        }
        catch
        {
            // Preserve original cancellation even when an observation faults.
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private void Require(bool eligible, WindowsMechanismFailure failure)
    {
        if (!eligible)
        {
            trace?.Record(failure);
            throw new ProviderFailureException(AuthenticationFailure.MechanismUnavailable);
        }
    }
}
