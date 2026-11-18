using System.Diagnostics;

namespace LiteAgensh;

internal static class ApplicationTelemetry
{
    internal static ActivitySource Source { get; } = new("LiteAgensh");
}
