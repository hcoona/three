using Microsoft.Extensions.Time.Testing;

namespace LiteAgensh.Tests.Support;

internal sealed class ManualScenarioClock : FakeTimeProvider
{
    internal ManualScenarioClock()
        : base(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)) { }
}
