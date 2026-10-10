using System.Text;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record RubyRSpecRequest(string Checkout, string Ruby, string Bundle,
    string BundlerVersion, string Gemfile, string Scratch, int DeadlineSeconds,
    IReadOnlyDictionary<string, string?> Environment);
internal sealed record RubyRSpecRun(NativeCommand Command, NativeCommandResult Result,
    RubyRSpecReport? Report, CheckStatus Status, string? ReportError);

// The caller prepares the bundle and exact checkout; RSpec selects its entire project suite.
internal static class RubyRSpecExecution
{
    internal static Task<RubyRSpecRun> RunAsync(CheckKey check, RubyRSpecRequest request,
        CancellationToken token) => RunAsync(check, request, NativeProcess.ExecuteAsync, token);

    internal static async Task<RubyRSpecRun> RunAsync(CheckKey check, RubyRSpecRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ImpactPlanner.ValidatePath(check.Target);
        if (check.Check != RubyProjectChecks.RSpec || check.Variant != "default" ||
            request.DeadlineSeconds <= 0 || string.IsNullOrWhiteSpace(request.BundlerVersion))
            throw new InvalidDataException("Unsupported whole-project Ruby RSpec request.");
        foreach (string path in new[] { request.Checkout, request.Ruby, request.Bundle,
                     request.Gemfile, request.Scratch })
            if (!Path.IsPathFullyQualified(path))
                throw new InvalidDataException("RSpec requires caller-prepared absolute paths.");
        string directory = Path.GetFullPath(Path.Combine(request.Checkout, check.Target));
        string output = Path.Combine(request.Scratch, "rspec-" + Guid.NewGuid() + ".json");
        var environment = new Dictionary<string, string?>(request.Environment,
            StringComparer.Ordinal)
        {
            ["BUNDLE_GEMFILE"] = request.Gemfile,
            ["BUNDLE_FROZEN"] = "1",
        };
        var command = new NativeCommand(request.Ruby, directory,
            [request.Bundle, "_" + request.BundlerVersion + "_", "exec", "rspec",
                "--format", "json", "--out", output], request.DeadlineSeconds,
            environment, InheritEnvironment: false);
        try
        {
            NativeCommandResult result = await execute(command, token);
            if (token.IsCancellationRequested)
                return new(command, result, null, CheckStatus.Cancelled,
                    "RSpec execution was cancelled before result collection.");
            RubyRSpecReport? report = null;
            string? error = null;
            try
            {
                if (File.Exists(output))
                    report = RubyRSpecResult.Read(await File.ReadAllTextAsync(output,
                        new UTF8Encoding(false, true), token));
                else
                    error = "Native RSpec did not produce its required JSON result.";
            }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException or DecoderFallbackException or InvalidDataException)
            {
                error = "Native RSpec result could not be read: " + exception.Message;
            }
            CheckStatus status = token.IsCancellationRequested ? CheckStatus.Cancelled :
                RubyRSpecResult.Status(result, report);
            return new(command, result, report, status, error);
        }
        finally
        {
            File.Delete(output);
        }
    }
}
