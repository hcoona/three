namespace Authentication.Windows.Scenarios;

// Scenarios in this category assert wall-clock process bounds (QPC on the same
// Windows host). They are Windows host-timing evidence and run only on a Windows host;
// no CI job runs them, including the Windows Scenario Tests job, which filters the
// category out.
internal static class WindowsHostTiming
{
    internal const string Category = "WindowsHostTiming";
}
