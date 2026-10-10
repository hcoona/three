using System.Text.Json;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.CI;

// Preserve the public formatter's original examples/messages independently of process exit.
internal sealed record RubyRSpecReport(string Version, int ExampleCount, int FailureCount,
    int PendingCount, int ErrorsOutsideExamples, JsonElement Original);

internal static class RubyRSpecResult
{
    internal static RubyRSpecReport Read(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            string version = root.GetProperty("version").GetString()
                ?? throw new InvalidDataException("Missing native RSpec version.");
            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidDataException("Missing native RSpec version.");
            JsonElement[] examples = root.GetProperty("examples").EnumerateArray().ToArray();
            JsonElement summary = root.GetProperty("summary");
            int count = Count(summary, "example_count"), failures = Count(summary, "failure_count"),
                pending = Count(summary, "pending_count"),
                outside = Count(summary, "errors_outside_of_examples_count");
            int actualFailures = 0, actualPending = 0;
            foreach (JsonElement example in examples)
            {
                if (string.IsNullOrWhiteSpace(example.GetProperty("id").GetString()))
                    throw new InvalidDataException("Missing native RSpec example identity.");
                switch (example.GetProperty("status").GetString())
                {
                    case "passed": break;
                    case "failed": actualFailures++; break;
                    case "pending": actualPending++; break;
                    default: throw new InvalidDataException("Incomplete native RSpec example.");
                }
            }
            if (count != examples.Length || failures != actualFailures || pending != actualPending)
                throw new InvalidDataException("Native RSpec examples and summary differ.");
            return new(version, count, failures, pending, outside, root.Clone());
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or FormatException or OverflowException)
        {
            throw new InvalidDataException("Invalid or incomplete native RSpec result.", exception);
        }
    }

    // Pending examples remain facts for adopted quality policy; do not strengthen native defaults.
    internal static CheckStatus Status(NativeCommandResult command, RubyRSpecReport? report) =>
        command.Termination switch
        {
            NativeTermination.Cancelled => CheckStatus.Cancelled,
            NativeTermination.TimedOut => CheckStatus.TimedOut,
            _ => command.Succeeded && command.Error is null && report is not null &&
                report.FailureCount == 0 && report.ErrorsOutsideExamples == 0
                    ? CheckStatus.Passed : CheckStatus.Failed,
        };

    private static int Count(JsonElement summary, string name)
    {
        int count = summary.GetProperty(name).GetInt32();
        return count >= 0 ? count :
            throw new InvalidDataException("Negative native RSpec result count.");
    }
}
