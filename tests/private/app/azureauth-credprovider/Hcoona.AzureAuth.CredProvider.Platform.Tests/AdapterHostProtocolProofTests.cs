using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AdapterHostProtocolProofTests
{
    private const int SuccessExitCode = 0;
    private const int NoCredentialExitCode = 1;
    private const int UnauthorizedExitCode = 3;
    private const int ConfigurationErrorExitCode = 64;
    private const int FatalExitCode = 70;

    [Fact]
    public async Task GitGetSuccessWritesOnlyProtocolPayloadFromRealChildProcess()
    {
        ProcessResult result = await RunProofAsync(AdapterHostProofProcess.GitGetSuccessScenario);

        AssertProtocolSuccess(result, AdapterHostProofProcess.GitGetSuccessProtocolPayload);
        Assert.DoesNotContain(
            AdapterHostProofProcess.SuppressedProtocolPayload,
            result.StandardOutput,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AdapterHostProofProcess.GitStoreSuccessScenario)]
    [InlineData(AdapterHostProofProcess.GitEraseSuccessScenario)]
    public async Task GitStoreAndEraseSuccessStaySilentOnStdoutAndStderr(string scenario)
    {
        ProcessResult result = await RunProofAsync(scenario);

        Assert.Equal(SuccessExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Theory]
    [InlineData(
        AdapterHostProofProcess.GitFailureScenario,
        ConfigurationErrorExitCode,
        AdapterHostProofProcess.ProtocolViolationSafeCode,
        AdapterHostProofProcess.ProtocolViolationSafeMessage)]
    [InlineData(
        AdapterHostProofProcess.GitUnauthorizedScenario,
        UnauthorizedExitCode,
        AdapterHostProofProcess.UnauthorizedSafeCode,
        AdapterHostProofProcess.UnauthorizedSafeMessage)]
    [InlineData(
        AdapterHostProofProcess.GitFatalScenario,
        FatalExitCode,
        AdapterHostProofProcess.FatalSafeCode,
        AdapterHostProofProcess.FatalSafeMessage)]
    public async Task GitFailuresEmitOnlySafeStderrFromRealChildProcess(
        string scenario,
        int expectedExitCode,
        string expectedSafeCode,
        string expectedSafeMessage)
    {
        ProcessResult result = await RunProofAsync(scenario);

        AssertSafeFailure(
            result,
            expectedExitCode,
            expectedSafeCode,
            expectedSafeMessage,
            AdapterHostProofProcess.SharedUsername,
            AdapterHostProofProcess.GitPassword);
    }

    [Fact]
    public async Task GitNoCredentialUsesFrozenExitCodeAndSuppressesAllOutputFromRealChildProcess()
    {
        ProcessResult result = await RunProofAsync(AdapterHostProofProcess.GitNoCredentialScenario);

        Assert.Equal(NoCredentialExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
        Assert.DoesNotContain(
            AdapterHostProofProcess.SuppressedProtocolPayload,
            result.StandardOutput,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            AdapterHostProofProcess.SuppressedProtocolPayload,
            result.StandardError,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            AdapterHostProofProcess.SuppressedHumanStdout,
            result.StandardOutput,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            AdapterHostProofProcess.SuppressedDiagnosticMessage,
            result.StandardError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NuGetSuccessWritesOnlySyntheticPluginPayloadFromRealChildProcess()
    {
        ProcessResult result = await RunProofAsync(AdapterHostProofProcess.NuGetSuccessScenario);

        AssertProtocolSuccess(result, AdapterHostProofProcess.NuGetSuccessProtocolPayload);
        Assert.DoesNotContain(
            "banner",
            result.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "prompt",
            result.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NuGetFailureEmitsOnlySafeStderrFromRealChildProcess()
    {
        ProcessResult result = await RunProofAsync(AdapterHostProofProcess.NuGetFailureScenario);

        AssertSafeFailure(
            result,
            ConfigurationErrorExitCode,
            AdapterHostProofProcess.ProtocolViolationSafeCode,
            AdapterHostProofProcess.ProtocolViolationSafeMessage,
            AdapterHostProofProcess.SharedUsername,
            AdapterHostProofProcess.NuGetPassword,
            AdapterHostProofProcess.NuGetSuccessProtocolPayload);
    }

    [Fact]
    public async Task KeyringSuccessMatchesFrozenHelperResponseShapeFromRealChildProcess()
    {
        ProcessResult result = await RunProofAsync(AdapterHostProofProcess.KeyringSuccessScenario);

        AssertProtocolSuccess(
            result,
            AdapterHostProofProcess.CreateKeyringSuccessProtocolPayload());
    }

    [Fact]
    public async Task KeyringFailureEmitsOnlySafeStderrFromRealChildProcess()
    {
        ProcessResult result = await RunProofAsync(AdapterHostProofProcess.KeyringFailureScenario);

        AssertSafeFailure(
            result,
            ConfigurationErrorExitCode,
            AdapterHostProofProcess.ProtocolViolationSafeCode,
            AdapterHostProofProcess.ProtocolViolationSafeMessage,
            AdapterHostProofProcess.SharedUsername,
            AdapterHostProofProcess.KeyringPassword,
            AdapterHostProofProcess.CreateKeyringSuccessProtocolPayload());
    }

    [Fact]
    public async Task
        InvocationBoundaryMismatchEmitsOnlySafeConfigurationStderrFromRealChildProcess()
    {
        ProcessResult result = await RunProofAsync(
            AdapterHostProofProcess.InvocationBoundaryMismatchScenario);

        AssertSafeFailure(
            result,
            ConfigurationErrorExitCode,
            AdapterHostProofProcess.InvocationBoundaryMismatchSafeCode,
            AdapterHostProofProcess.InvocationBoundaryMismatchSafeMessage,
            AdapterHostProofProcess.InvocationBoundaryMismatchDescriptorMarker,
            AdapterHostProofProcess.InvocationBoundaryMismatchPayloadMarker,
            "does not match the current invocation boundary",
            "InvalidOperationException");
    }

    [Fact]
    public async Task ProofHelperRejectsMissingScenarioWithDeterministicConfigurationFailure()
    {
        ProcessResult result = await RunProofHelperAsync();

        AssertConfigurationFailure(
            result,
            "Adapter host proof process requires exactly one scenario.");
    }

    [Fact]
    public async Task ProofHelperRejectsBlankScenarioWithDeterministicConfigurationFailure()
    {
        ProcessResult result = await RunProofHelperAsync(" ");

        AssertConfigurationFailure(
            result,
            "Adapter host proof process requires exactly one scenario.");
    }

    [Fact]
    public async Task ProofHelperRejectsUnknownScenarioWithDeterministicConfigurationFailure()
    {
        ProcessResult result = await RunProofHelperAsync("bogus");

        AssertConfigurationFailure(result, "Unknown adapter host proof scenario 'bogus'.");
    }

    [Fact]
    public async Task SharedHostHumanCommandAllowsHumanStdoutWithoutProtocolLeak()
    {
        ProcessResult result = await RunProofAsync(AdapterHostProofProcess.HumanCommandScenario);

        Assert.Equal(SuccessExitCode, result.ExitCode);
        Assert.Equal(AdapterHostProofProcess.HumanCommandStdout, result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
        Assert.DoesNotContain(
            AdapterHostProofProcess.SuppressedProtocolPayload,
            result.StandardOutput,
            StringComparison.Ordinal);
    }

    private static async Task<ProcessResult> RunProofAsync(string scenario)
    {
        return await RunProofHelperAsync(scenario);
    }

    private static async Task<ProcessResult> RunProofHelperAsync(params string[] scenarioArguments)
    {
        var runner = new SystemProcessRunner();
        return await runner.RunAsync(
            ProofStartSpec(scenarioArguments),
            TestContext.Current.CancellationToken);
    }

    private static ProcessStartSpec ProofStartSpec(params string[] scenarioArguments)
    {
        var helperNonce = ProcessTestApp.CreateHelperNonce();
        List<string> arguments = ProcessTestApp.CreateHelperArguments(
            helperNonce,
            "adapter-host-proof",
            scenarioArguments
        );

        return new ProcessStartSpec(
            ProcessTestApp.AppHostPath(),
            arguments,
            environment: ProcessTestApp.CreateHelperEnvironment(
                helperNonce,
                environmentMode: ProcessEnvironmentMode.ExplicitOnly),
            environmentMode: ProcessEnvironmentMode.ExplicitOnly);
    }

    private static void AssertProtocolSuccess(ProcessResult result, string expectedStdout)
    {
        Assert.Equal(SuccessExitCode, result.ExitCode);
        Assert.Equal(expectedStdout, result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
        Assert.DoesNotContain(
            AdapterHostProofProcess.SuppressedHumanStdout,
            result.StandardOutput,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            AdapterHostProofProcess.SuppressedDiagnosticMessage,
            result.StandardOutput,
            StringComparison.Ordinal);
    }

    private static void AssertConfigurationFailure(ProcessResult result, string expectedStderr)
    {
        Assert.Equal(ConfigurationErrorExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Equal(expectedStderr, ProcessTestApp.NormalizeNewlines(result.StandardError));
    }

    private static void AssertSafeFailure(
        ProcessResult result,
        int expectedExitCode,
        string expectedSafeCode,
        string expectedSafeMessage,
        params string[] forbiddenFragments)
    {
        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.NotEqual(string.Empty, result.StandardError);

        string stderr = ProcessTestApp.NormalizeNewlines(result.StandardError);
        Assert.Contains(expectedSafeMessage, stderr, StringComparison.Ordinal);
        Assert.Contains($"code={expectedSafeCode}", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(
            AdapterHostProofProcess.SuppressedProtocolPayload,
            stderr,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            AdapterHostProofProcess.SuppressedHumanStdout,
            stderr,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            AdapterHostProofProcess.SuppressedDiagnosticMessage,
            stderr,
            StringComparison.Ordinal);
        Assert.Single(stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        foreach (string forbiddenFragment in forbiddenFragments)
        {
            Assert.DoesNotContain(forbiddenFragment, stderr, StringComparison.Ordinal);
        }
    }
}
