using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class GitCredentialHelperAdapterTests
{
    [Fact]
    public void GetForDevAzureRepoWritesGitCredentialFieldsOnly()
    {
        var provider = new DeterministicFakeIdentityProvider();
        AdapterRunResult result = Execute(
            ["git", "credential-helper", "get"],
            """
            protocol=https
            host=dev.azure.com
            path=org/project/_git/repository
            username=User@Example.com

            """,
            credentialCore: new CredentialCoreService(provider));

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.True(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Empty(result.HumanStdout);
        Assert.StartsWith("username=AzureDevOps\npassword=fake-secret-", result.ProtocolStdout);
        Assert.EndsWith("\n", result.ProtocolStdout, StringComparison.Ordinal);
        Assert.DoesNotContain("User@Example.com", result.ProtocolStdout, StringComparison.Ordinal);
        Assert.Equal(1, provider.InvocationCount);
    }

    [Theory]
    [InlineData("store")]
    [InlineData("erase")]
    public void StoreAndEraseAreNoOpSuccessesWithNoProtocolStdout(string operation)
    {
        var provider = new DeterministicFakeIdentityProvider();
        AdapterRunResult result = Execute(
            ["git", "credential-helper", operation],
            """
            protocol=https
            host=dev.azure.com
            path=org/project/_git/repository
            username=AzureDevOps
            password=should-not-leak

            """,
            credentialCore: new CredentialCoreService(provider));

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void GetForUnsupportedHostReturnsNoCredentialWithNoOutput()
    {
        var provider = new DeterministicFakeIdentityProvider();
        AdapterRunResult result = Execute(
            ["git", "credential-helper", "get"],
            """
            protocol=https
            host=example.com
            path=org/project/_git/repository

            """,
            credentialCore: new CredentialCoreService(provider));

        Assert.Equal(AdapterHostExitCode.NoCredential, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void GetForDevAzureWithoutPathReturnsNoCredentialWithNoOutput()
    {
        var provider = new DeterministicFakeIdentityProvider();
        AdapterRunResult result = Execute(
            ["git", "credential-helper", "get"],
            """
            protocol=https
            host=dev.azure.com

            """,
            credentialCore: new CredentialCoreService(provider));

        Assert.Equal(AdapterHostExitCode.NoCredential, result.Outcome.Result.ExitCode);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void GetWithMalformedInputFailsClosedWithSafeDiagnostic()
    {
        AdapterRunResult result = Execute(
            ["git", "credential-helper", "get"],
            """
            protocol=https
            host=dev.azure.com
            host=should-not-leak.example

            """);

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Contains("code=ProtocolViolation", result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-leak", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void HelperExecutableEntrypointAcceptsGitOperationAsFirstArgument()
    {
        AdapterRunResult result = Execute(
            ["get"],
            """
            protocol=https
            host=dev.azure.com
            path=org/project/_git/repository

            """,
            executablePath: "/usr/local/bin/git-credential-azureauth-credprovider");

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.StartsWith("username=AzureDevOps\npassword=fake-secret-", result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public void UnknownOperationIsSilentNoOpSuccess()
    {
        AdapterRunResult result = Execute(
            ["capability"],
            """
            protocol=https
            host=dev.azure.com
            path=org/project/_git/repository

            """,
            executablePath: "/usr/local/bin/git-credential-azureauth-credprovider");

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    private static AdapterRunResult Execute(
        string[] args,
        string stdin,
        string executablePath = "/usr/local/bin/azureauth-credprovider",
        CredentialCoreService? credentialCore = null)
    {
        var protocolStdout = new StringWriter();
        var humanStdout = new StringWriter();
        var stderr = new StringWriter();
        var diagnosticRouter = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(stderr)],
            SecretRedactor.Empty);
        AdapterHostExecutionOutcome outcome = new GitCredentialHelperAdapter(
            credentialCore).Execute(
                executablePath,
                args,
                new StringReader(stdin),
                protocolStdout,
                humanStdout,
                diagnosticRouter);

        return new AdapterRunResult(
            outcome,
            protocolStdout.ToString(),
            humanStdout.ToString(),
            stderr.ToString());
    }

    private sealed record AdapterRunResult(
        AdapterHostExecutionOutcome Outcome,
        string ProtocolStdout,
        string HumanStdout,
        string Stderr);
}
