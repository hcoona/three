using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class RubyRSpecResultTests
{
    private static readonly string[] Messages = ["outside example diagnostic"];

    [TestMethod]
    [DataRow("passed", 1, 0, 0, 0, "Passed")]
    [DataRow("failed", 1, 1, 0, 0, "Failed")]
    [DataRow("pending", 1, 0, 1, 0, "Passed")]
    [DataRow("outside", 0, 0, 0, 1, "Failed")]
    public void OriginalPublicCliResultsMatchNativeExampleAndErrorCounts(string scenario,
        int examples, int failures, int pending, int outside, string expected)
    {
        var assembly = typeof(RubyRSpecResultTests).Assembly;
        string name = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith($"ruby-rspec-{scenario}.json", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        string original = reader.ReadToEnd();

        RubyRSpecReport report = RubyRSpecResult.Read(original);

        Assert.AreEqual(examples, report.ExampleCount);
        Assert.AreEqual(failures, report.FailureCount);
        Assert.AreEqual(pending, report.PendingCount);
        Assert.AreEqual(outside, report.ErrorsOutsideExamples);
        using JsonDocument document = JsonDocument.Parse(original);
        Assert.IsTrue(JsonElement.DeepEquals(document.RootElement, report.Original));
        Assert.AreEqual(Enum.Parse<CheckStatus>(expected),
            RubyRSpecResult.Status(Command(), report));
    }
    [TestMethod]
    public void CompleteNativeExamplesAndMessagesRemainOriginal()
    {
        string output = Report(["passed", "failed", "pending"], 1);

        RubyRSpecReport report = RubyRSpecResult.Read(output);

        Assert.AreEqual("3.13.6", report.Version);
        Assert.AreEqual(3, report.ExampleCount);
        Assert.AreEqual(1, report.FailureCount);
        Assert.AreEqual(1, report.PendingCount);
        Assert.AreEqual(1, report.ErrorsOutsideExamples);
        Assert.AreEqual(output, report.Original.GetRawText());
        Assert.AreEqual("outside example diagnostic",
            report.Original.GetProperty("messages")[0].GetString());
        Assert.AreEqual("original failure",
            report.Original.GetProperty("examples")[1].GetProperty("exception")
                .GetProperty("message").GetString());
        Assert.AreEqual(CheckStatus.Failed, RubyRSpecResult.Status(Command(), report));
    }

    [TestMethod]
    public void PendingDoesNotIntroduceAnExtraUniversalQualityRule()
    {
        RubyRSpecReport report = RubyRSpecResult.Read(Report(["passed", "pending"]));

        Assert.AreEqual(CheckStatus.Passed, RubyRSpecResult.Status(Command(), report));
        Assert.AreEqual(1, report.PendingCount);
    }

    [TestMethod]
    [DataRow("Exited", 1, "Failed")]
    [DataRow("Cancelled", 0, "Cancelled")]
    [DataRow("TimedOut", 0, "TimedOut")]
    [DataRow("InvalidOutput", 0, "Failed")]
    [DataRow("CleanupFailed", 0, "Failed")]
    [DataRow("StartFailed", 0, "Failed")]
    public void CompletePassingOutputCannotOverrideNativeFailure(
        string termination, int code, string expected)
    {
        RubyRSpecReport report = RubyRSpecResult.Read(Report(["passed"]));

        Assert.AreEqual(Enum.Parse<CheckStatus>(expected), RubyRSpecResult.Status(
            Command(Enum.Parse<NativeTermination>(termination), code), report));
    }

    [TestMethod]
    public void ZeroExitWithoutRequiredResultOrWithCaptureErrorFails()
    {
        Assert.AreEqual(CheckStatus.Failed, RubyRSpecResult.Status(Command(), null));
        RubyRSpecReport report = RubyRSpecResult.Read(Report(["passed"]));
        Assert.AreEqual(CheckStatus.Failed,
            RubyRSpecResult.Status(Command() with { Error = "capture incomplete" }, report));
    }

    [TestMethod]
    public void OutsideExampleErrorFailsEvenWithNoFailedExamples()
    {
        RubyRSpecReport report = RubyRSpecResult.Read(Report([], 1));

        Assert.AreEqual(0, report.FailureCount);
        Assert.AreEqual(1, report.ErrorsOutsideExamples);
        Assert.AreEqual(CheckStatus.Failed, RubyRSpecResult.Status(Command(), report));
    }

    [TestMethod]
    public void FailedExampleCannotPassEvenWhenCommandReportsZeroExit()
    {
        RubyRSpecReport report = RubyRSpecResult.Read(Report(["passed", "failed"]));

        Assert.AreEqual(0, report.ErrorsOutsideExamples);
        Assert.AreEqual(1, report.FailureCount);
        Assert.AreEqual(CheckStatus.Failed, RubyRSpecResult.Status(Command(), report));
    }

    [TestMethod]
    [DataRow("truncated")]
    [DataRow("missing_summary")]
    [DataRow("missing_examples")]
    [DataRow("missing_outside_count")]
    [DataRow("missing_example_id")]
    [DataRow("unknown_status")]
    [DataRow("count_mismatch")]
    [DataRow("failure_mismatch")]
    [DataRow("pending_mismatch")]
    [DataRow("negative_count")]
    [DataRow("empty_version")]
    public void IncompleteResultCannotSatisfyRequiredCheck(string defect)
    {
        string text = Report(["passed", "pending"]);
        text = defect switch
        {
            "truncated" => text[..^1],
            "missing_summary" => text.Replace("\"summary\":", "\"absent\":"),
            "missing_examples" => text.Replace("\"examples\":", "\"absent\":"),
            "missing_outside_count" =>
                text.Replace("errors_outside_of_examples_count", "absent"),
            "missing_example_id" => text.Replace("\"id\":", "\"absent\":"),
            "unknown_status" => text.Replace("\"passed\"", "\"unknown\""),
            "count_mismatch" => text.Replace("\"example_count\":2", "\"example_count\":3"),
            "failure_mismatch" => text.Replace("\"failure_count\":0", "\"failure_count\":1"),
            "pending_mismatch" => text.Replace("\"pending_count\":1", "\"pending_count\":0"),
            "negative_count" => text.Replace("\"example_count\":2", "\"example_count\":-1"),
            "empty_version" => text.Replace("3.13.6", ""),
            _ => throw new ArgumentException("Unknown test defect.", nameof(defect)),
        };

        Assert.ThrowsExactly<InvalidDataException>(() => RubyRSpecResult.Read(text));
    }

    private static NativeCommandResult Command(
        NativeTermination termination = NativeTermination.Exited,
        int code = 0) => new(termination, code, "original stdout", "original stderr", 1, null);

    private static string Report(string[] statuses, int outside = 0) => JsonSerializer.Serialize(new
    {
        version = "3.13.6",
        messages = Messages,
        examples = statuses.Select((status, index) => new
        {
            id = $"./spec/native_spec.rb[1:{index + 1}]", status,
            exception = status == "failed" ? new { message = "original failure" } : null,
        }).ToArray(),
        summary = new
        {
            example_count = statuses.Length, failure_count = statuses.Count(s => s == "failed"),
            pending_count = statuses.Count(s => s == "pending"),
            errors_outside_of_examples_count = outside,
        },
    });
}
