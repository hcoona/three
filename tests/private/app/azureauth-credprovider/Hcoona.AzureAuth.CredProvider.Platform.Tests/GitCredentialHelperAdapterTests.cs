using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
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
            credentialCore: new CredentialCoreService(provider)
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.True(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Empty(result.HumanStdout);
        Assert.StartsWith("username=AzureDevOps\npassword=fake-secret-", result.ProtocolStdout);
        Assert.EndsWith("\n", result.ProtocolStdout, StringComparison.Ordinal);
        Assert.DoesNotContain("User@Example.com", result.ProtocolStdout, StringComparison.Ordinal);
        Assert.Equal(0, provider.InvocationCount);
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
            credentialCore: new CredentialCoreService(provider)
        );

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
            credentialCore: new CredentialCoreService(provider)
        );

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
            credentialCore: new CredentialCoreService(provider)
        );

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

            """
        );

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
            executablePath: "/usr/local/bin/git-credential-azureauth-credprovider"
        );

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
            executablePath: "/usr/local/bin/git-credential-azureauth-credprovider"
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public void GetWithOrdinaryProtocolUsernameDoesNotSetAccountHintOrTriggerBindingMismatch()
    {
        var credentialAcquisition = new MismatchSensitiveAcquisitionService();
        AdapterRunResult result = Execute(
            ["git", "credential-helper", "get"],
            """
            protocol=https
            host=dev.azure.com
            path=org/project/_git/repository
            username=User@Example.com

            """,
            credentialAcquisition: credentialAcquisition
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.True(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal("username=AzureDevOps\npassword=fake-secret-git\n", result.ProtocolStdout);
        Assert.Empty(result.HumanStdout);
        Assert.Equal(string.Empty, result.Stderr);

        CredentialRequestV2 request = Assert.Single(credentialAcquisition.Requests);
        Assert.Null(request.AccountHint);
        Assert.Equal(IdentityFlow.InteractiveBrowser, request.IdentityFlow);
        Assert.Equal(InteractivePolicy.Never, request.InteractivePolicy);
        Assert.Equal(AcquisitionMode.SilentOnly, request.AcquisitionMode);
        Assert.False(credentialAcquisition.BindingMismatchDetected);
        Assert.DoesNotContain("User@Example.com", result.ProtocolStdout, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AzureAuthBindingAccountMismatch",
            result.Stderr,
            StringComparison.Ordinal
        );
    }

    private static AdapterRunResult Execute(
        string[] args,
        string stdin,
        string executablePath = "/usr/local/bin/azureauth-credprovider",
        CredentialCoreService? credentialCore = null,
        ICredentialAcquisitionService? credentialAcquisition = null
    )
    {
        var protocolStdout = new StringWriter();
        var humanStdout = new StringWriter();
        var stderr = new StringWriter();
        var diagnosticRouter = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(stderr)],
            SecretRedactor.Empty
        );
        _ = credentialCore;
        AdapterHostExecutionOutcome outcome = new GitCredentialHelperAdapter(
            credentialAcquisition ?? new SuccessfulTestAcquisitionService()
        ).Execute(
            executablePath,
            args,
            new StringReader(stdin),
            protocolStdout,
            humanStdout,
            diagnosticRouter
        );

        return new AdapterRunResult(
            outcome,
            protocolStdout.ToString(),
            humanStdout.ToString(),
            stderr.ToString()
        );
    }

    private sealed record AdapterRunResult(
        AdapterHostExecutionOutcome Outcome,
        string ProtocolStdout,
        string HumanStdout,
        string Stderr
    );

    private sealed class SuccessfulTestAcquisitionService : ICredentialAcquisitionService
    {
        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        ) =>
            ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    Username = "AzureDevOps",
                    Password = "fake-secret-git",
                    DiagnosticsCorrelationId = "git-adapter-test",
                }
            );
    }

    private sealed class MismatchSensitiveAcquisitionService : ICredentialAcquisitionService
    {
        public bool BindingMismatchDetected { get; private set; }

        public List<CredentialRequestV2> Requests { get; } = [];

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            Requests.Add(request);
            if (request.AccountHint is not null)
            {
                BindingMismatchDetected = true;
                return ValueTask.FromResult(
                    new CredentialResult
                    {
                        Status = CredentialResultStatus.Unauthorized,
                        DiagnosticsCorrelationId = "git-binding-mismatch-test",
                        Error = new CredentialError
                        {
                            Kind = CredentialErrorKind.Unauthorized,
                            Code = "AzureAuthBindingAccountMismatch",
                            SafeMessage = "The supplied identity does not match the binding.",
                        },
                    }
                );
            }

            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    Username = "AzureDevOps",
                    Password = "fake-secret-git",
                    DiagnosticsCorrelationId = "git-binding-match-test",
                }
            );
        }
    }

    [Fact]
    public void GetKeepsSilentOnlyPolicyAndHumanStdoutEmpty()
    {
        var credentialAcquisition = new MismatchSensitiveAcquisitionService();

        AdapterRunResult result = Execute(
            ["git", "credential-helper", "get"],
            """
            protocol=https
            host=dev.azure.com
            path=org/project/_git/repository

            """,
            credentialAcquisition: credentialAcquisition
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.True(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal("username=AzureDevOps\npassword=fake-secret-git\n", result.ProtocolStdout);
        Assert.Empty(result.HumanStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.DoesNotContain("fake-secret-git", result.HumanStdout, StringComparison.Ordinal);

        CredentialRequestV2 request = Assert.Single(credentialAcquisition.Requests);
        Assert.Equal(CredentialEcosystem.Git, request.Ecosystem);
        Assert.Equal(CredentialOperation.Get, request.Operation);
        Assert.Equal(TokenAudience.AzureDevOps, request.RequestedAudience);
        Assert.Equal(CredentialKind.BasicPassword, request.CredentialKind);
        Assert.Equal(IdentityFlow.InteractiveBrowser, request.IdentityFlow);
        Assert.NotEqual(IdentityFlow.DeviceCode, request.IdentityFlow);
        Assert.Equal(InteractivePolicy.Never, request.InteractivePolicy);
        Assert.Equal(AcquisitionMode.SilentOnly, request.AcquisitionMode);
        Assert.Equal(CachePolicyMode.ProductPersistentCacheDisabled, request.CachePolicy);
        CiContext ciContext = Assert.IsType<CiContext>(request.CiContext);
        Assert.False(ciContext.ExplicitCiMode);
        Assert.False(ciContext.AllowsPersistentWrites);
    }

    [Fact]
    public void GetSilentRequestPreservesDefaultServiceAndCanonicalResource()
    {
        var credentialAcquisition = new MismatchSensitiveAcquisitionService();

        AdapterRunResult result = Execute(
            ["git", "credential-helper", "get"],
            """
            protocol=https
            host=dev.azure.com
            path=org/project/_git/repository

            """,
            credentialAcquisition: credentialAcquisition
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Empty(result.HumanStdout);
        Assert.Equal(string.Empty, result.Stderr);
        CredentialRequestV2 request = Assert.Single(credentialAcquisition.Requests);
        Assert.Equal("default", request.ServiceIdentity);
        CanonicalResourceIdentity resource = request.Resource;
        Assert.Equal("dev.azure.com", resource.AzureDevOpsHost);
        Assert.Equal("org", resource.Organization);
        Assert.Equal("project", resource.Project);
        Assert.Null(resource.Feed);
        Assert.Equal("repository", resource.Repository);
        Assert.Equal(
            new Uri("https://dev.azure.com/org/project/_git/repository"),
            resource.ServiceEndpoint
        );
        Assert.Equal(IdentityFlow.InteractiveBrowser, request.IdentityFlow);
        Assert.Equal(AcquisitionMode.SilentOnly, request.AcquisitionMode);
        Assert.Equal("username=AzureDevOps\npassword=fake-secret-git\n", result.ProtocolStdout);
    }
}
