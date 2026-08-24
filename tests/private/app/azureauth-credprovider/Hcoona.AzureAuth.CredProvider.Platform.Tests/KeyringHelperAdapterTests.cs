using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;
using System.Text.Json;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class KeyringHelperAdapterTests
{
    private static readonly DateTimeOffset ExpiresAt = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CredentialsModeForModernFeedWritesKeyringCredentialPairOnly()
    {
        var provider = new MismatchSensitiveAcquisitionService();
        KeyringHelperRequest request = CreateRequest(
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            username: "User@Example.com",
            KeyringHelperMode.Credentials
        );

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(request).Skip(1).ToArray(),
            credentialAcquisition: provider
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.True(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal("AzureDevOps\nphase11-secret\n", result.ProtocolStdout);
        Assert.Empty(result.HumanStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(1, provider.InvocationCount);

        CredentialRequestV2 credentialRequest = Assert.Single(provider.Requests);
        Assert.Equal(CredentialEcosystem.Python, credentialRequest.Ecosystem);
        Assert.Equal(CredentialKind.BasicPassword, credentialRequest.CredentialKind);
        Assert.Equal(TokenAudience.AzureArtifacts, credentialRequest.RequestedAudience);
        Assert.Null(credentialRequest.AccountHint);
        Assert.Equal(IdentityFlow.InteractiveBrowser, credentialRequest.IdentityFlow);
        Assert.Equal(InteractivePolicy.UserAllowed, credentialRequest.InteractivePolicy);
        Assert.Equal(AcquisitionMode.InteractionAllowed, credentialRequest.AcquisitionMode);
        Assert.False(provider.BindingMismatchDetected);
        Assert.DoesNotContain(
            "AzureAuthBindingAccountMismatch",
            result.Stderr,
            StringComparison.Ordinal
        );
        Assert.Equal("org", credentialRequest.Resource.Organization);
        Assert.Null(credentialRequest.Resource.Project);
        Assert.Equal("feed", credentialRequest.Resource.Feed);
    }

    [Fact]
    public void PasswordModeForLegacyFeedWritesOnlyPassword()
    {
        var provider = new SuccessfulAcquisitionService();
        KeyringHelperRequest request = CreateRequest(
            "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/pypi/simple/",
            username: null,
            KeyringHelperMode.Password
        );

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(request).Skip(1).ToArray(),
            credentialAcquisition: provider
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Equal("phase11-secret\n", result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);

        CredentialRequestV2 credentialRequest = Assert.Single(provider.Requests);
        Assert.Equal("org", credentialRequest.Resource.Organization);
        Assert.Equal("project", credentialRequest.Resource.Project);
        Assert.Equal("feed", credentialRequest.Resource.Feed);
    }

    [Theory]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/upload/", null)]
    [InlineData("https://dev.azure.com/org/project/_packaging/feed/pypi/upload/", "project")]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/pypi/upload/",
        "project"
    )]
    public void UploadEndpointFeedsAreAcceptedForPublishing(string service, string? project)
    {
        var provider = new SuccessfulAcquisitionService();
        KeyringHelperRequest request = CreateRequest(
            service,
            username: null,
            KeyringHelperMode.Credentials
        );

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(request).Skip(1).ToArray(),
            credentialAcquisition: provider
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Equal("AzureDevOps\nphase11-secret\n", result.ProtocolStdout);

        CredentialRequestV2 credentialRequest = Assert.Single(provider.Requests);
        Assert.Equal("org", credentialRequest.Resource.Organization);
        Assert.Equal(project, credentialRequest.Resource.Project);
        Assert.Equal("feed", credentialRequest.Resource.Feed);
    }

    [Theory]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/download/"
            + "package/1.2/package-1.2-py3-none-any.whl",
        null,
        "feed",
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/project-id/_packaging/feed-id/pypi/download/"
            + "package/1.2/package-1.2-py3-none-any.whl",
        "project-id",
        "feed-id",
        "https://pkgs.dev.azure.com/org/project-id/_packaging/feed-id/pypi/simple/"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/pypi/download/"
            + "package/1.2/package-1.2-py3-none-any.whl",
        "project",
        "feed",
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/pypi/simple/"
    )]
    public void KeyringCliNormalizesUvLockedDownloadUrlToFeedEndpoint(
        string service,
        string? project,
        string feed,
        string expectedService
    )
    {
        var provider = new SuccessfulAcquisitionService();

        AdapterRunResult result = ExecuteKeyringCli(
            ["get", service, "VssSessionToken"],
            provider
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Equal("phase11-secret\n", result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);

        CredentialRequestV2 request = Assert.Single(provider.Requests);
        Assert.Equal("org", request.Resource.Organization);
        Assert.Equal(project, request.Resource.Project);
        Assert.Equal(feed, request.Resource.Feed);
        Assert.Equal(new Uri(expectedService), request.Resource.ServiceEndpoint);
    }

    [Fact]
    public void SharedHostEntrypointAcceptsFullHelperCommand()
    {
        var provider = new SuccessfulAcquisitionService();
        KeyringHelperRequest request = CreateRequest(
            "https://dev.azure.com/org/project/_packaging/feed/pypi/simple/",
            username: null,
            KeyringHelperMode.Credentials
        );

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(request).ToArray(),
            executablePath: "/usr/local/bin/azureauth-credprovider",
            credentialAcquisition: provider
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Equal("AzureDevOps\nphase11-secret\n", result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(1, provider.InvocationCount);

        CredentialRequestV2 credentialRequest = Assert.Single(provider.Requests);
        Assert.Equal(CredentialEcosystem.Python, credentialRequest.Ecosystem);
        Assert.Equal(CredentialOperation.Get, credentialRequest.Operation);
        Assert.Equal("org", credentialRequest.Resource.Organization);
        Assert.Equal("project", credentialRequest.Resource.Project);
        Assert.Equal("feed", credentialRequest.Resource.Feed);
        Assert.Equal("creds", credentialRequest.ExtensionData["python.keyring.mode"]);
    }

    [Fact]
    public void DedicatedShimEntrypointAcceptsPythonBuiltFullHelperCommand()
    {
        var provider = new SuccessfulAcquisitionService();
        KeyringHelperRequest request = CreateRequest(
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            username: null,
            KeyringHelperMode.Password
        );

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(request).ToArray(),
            credentialAcquisition: provider
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Equal("phase11-secret\n", result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(1, provider.InvocationCount);
    }

    [Fact]
    public void DedicatedShimEntrypointRoutesMalformedCommandToProtocolFailure()
    {
        AdapterRunResult result = Execute([
            "set",
            "--protocol-version",
            "2",
            "--service",
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            "--mode",
            "creds",
        ]);

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Contains("code=ProtocolViolation", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void HelperContractRejectsUvDownloadUrlWithoutAcquisition()
    {
        var provider = new SuccessfulAcquisitionService();
        AdapterRunResult result = Execute(
            [
                "get",
                "--protocol-version",
                "2",
                "--service",
                "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/download/"
                    + "package/1.2/package.whl",
                "--username",
                "VssSessionToken",
                "--mode",
                "password",
            ],
            credentialAcquisition: provider
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Contains("code=ProtocolViolation", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void UnsupportedServiceHostReturnsNoCredentialWithoutInvokingCredentialCore()
    {
        var provider = new CapturingIdentityProvider();
        AdapterRunResult result = Execute(
            [
                "get",
                "--protocol-version",
                "2",
                "--service",
                "https://example.com/org/_packaging/feed/pypi/simple/",
                "--mode",
                "creds",
            ],
            credentialCore: new CredentialCoreService(provider)
        );

        Assert.Equal(AdapterHostExitCode.NoCredential, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void MalformedAzureServicePathFailsClosedWithoutInvokingCredentialCore()
    {
        var provider = new CapturingIdentityProvider();
        AdapterRunResult result = Execute(
            [
                "get",
                "--protocol-version",
                "2",
                "--service",
                "https://pkgs.dev.azure.com/org/_packaging/feed/npm",
                "--mode",
                "creds",
            ],
            credentialCore: new CredentialCoreService(provider)
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Contains("code=ProtocolViolation", result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("pkgs.dev.azure.com", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void OldProtocolMajorFailsClosedWithoutInvokingCredentialCore()
    {
        var provider = new CapturingIdentityProvider();
        AdapterRunResult result = Execute(
            [
                "get",
                "--protocol-version",
                "1",
                "--service",
                "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
                "--mode",
                "password",
            ],
            credentialCore: new CredentialCoreService(provider)
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Contains("code=ProtocolViolation", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void CredentialUnavailableSuppressesStdoutAndWritesSafeDiagnostic()
    {
        AdapterRunResult result = Execute(
            KeyringHelperV2
                .BuildArguments(
                    CreateRequest(
                        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
                        username: null,
                        KeyringHelperMode.Credentials
                    )
                )
                .Skip(1)
                .ToArray(),
            credentialAcquisition: new FixedResultAcquisitionService(
                CredentialResultStatus.CredentialUnavailable,
                CredentialErrorKind.CredentialUnavailable,
                "TokenExchangeUnavailable"
            )
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Contains("code=TokenExchangeUnavailable", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void FatalCredentialFailureSuppressesStdoutAndWritesSafeDiagnostic()
    {
        AdapterRunResult result = Execute(
            KeyringHelperV2
                .BuildArguments(
                    CreateRequest(
                        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
                        username: null,
                        KeyringHelperMode.Password
                    )
                )
                .Skip(1)
                .ToArray(),
            credentialAcquisition: new FixedResultAcquisitionService(
                CredentialResultStatus.Fatal,
                CredentialErrorKind.Fatal,
                "TokenExchangeFailed"
            )
        );

        Assert.Equal(AdapterHostExitCode.Fatal, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Contains("code=TokenExchangeFailed", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolveProtocolInvocationRecognizesDedicatedShimEvenWithBadArgs()
    {
        bool resolved = KeyringHelperAdapter.TryResolveProtocolInvocation(
            "/usr/local/bin/python-keyring",
            ["set"],
            out AdapterInvocationContext? context
        );

        Assert.True(resolved);
        Assert.NotNull(context);
        Assert.Equal(AdapterProtocol.KeyringHelper, context.Protocol);
        Assert.Equal(["set"], context.PayloadArguments);
    }

    [Fact]
    public void TryResolveProtocolInvocationRecognizesSharedApphostAndStripsEntrypoint()
    {
        const string Apphost = "/opt/azureauth-credprovider/azureauth-credprovider";
        string[] arguments =
        [
            "python-keyring",
            "get",
            "--protocol-version",
            "2",
            "--service",
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            "--username",
            "user",
            "--mode",
            "creds",
        ];

        bool resolved = KeyringHelperAdapter.TryResolveProtocolInvocation(
            Apphost,
            arguments,
            out AdapterInvocationContext? context
        );

        Assert.True(resolved);
        Assert.NotNull(context);
        Assert.Equal("KeyringHelper", context.Entrypoint.Name);
        Assert.Equal(AdapterProtocol.KeyringHelper, context.Protocol);
        Assert.Equal(Apphost, context.ExecutablePath);
        Assert.Equal(arguments, context.RawArguments);
        Assert.Equal(["python-keyring"], context.MatchedArguments);
        Assert.Equal(
            [
                "get",
                "--protocol-version",
                "2",
                "--service",
                "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
                "--username",
                "user",
                "--mode",
                "creds",
            ],
            context.PayloadArguments
        );
    }

    [Fact]
    public void KeyringCliCredentialsJsonModeAllowsUvBrowserInteraction()
    {
        var provider = new SuccessfulAcquisitionService(
            "Azure\"DevOps\\user",
            "phase11-\"secret\\value"
        );

        AdapterRunResult result = ExecuteKeyringCli(
            [
                "--mode=creds",
                "--output=json",
                "get",
                "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            ],
            provider
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Empty(result.HumanStdout);
        Assert.Equal(string.Empty, result.Stderr);
        using JsonDocument payload = JsonDocument.Parse(result.ProtocolStdout);
        Assert.Equal(
            "Azure\"DevOps\\user",
            payload.RootElement.GetProperty("username").GetString()
        );
        Assert.Equal(
            "phase11-\"secret\\value",
            payload.RootElement.GetProperty("password").GetString()
        );
        Assert.DoesNotContain("phase11-", result.Stderr, StringComparison.Ordinal);

        CredentialRequestV2 request = Assert.Single(provider.Requests);
        Assert.Equal("creds", request.ExtensionData["python.keyring.mode"]);
        Assert.Equal(IdentityFlow.InteractiveBrowser, request.IdentityFlow);
        Assert.Equal(InteractivePolicy.UserAllowed, request.InteractivePolicy);
        Assert.Equal(AcquisitionMode.InteractionAllowed, request.AcquisitionMode);
    }

    [Fact]
    public void KeyringCliPasswordModeSupportsPipSubprocessShape()
    {
        var provider = new SuccessfulAcquisitionService();

        AdapterRunResult result = ExecuteKeyringCli(
            [
                "get",
                "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
                "requested-user",
            ],
            provider
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Equal("phase11-secret\n", result.ProtocolStdout);
        Assert.Empty(result.HumanStdout);
        Assert.Equal(string.Empty, result.Stderr);
        CredentialRequestV2 request = Assert.Single(provider.Requests);
        Assert.Equal("password", request.ExtensionData["python.keyring.mode"]);
        Assert.Equal(IdentityFlow.InteractiveBrowser, request.IdentityFlow);
        Assert.Equal(InteractivePolicy.UserAllowed, request.InteractivePolicy);
        Assert.Equal(AcquisitionMode.InteractionAllowed, request.AcquisitionMode);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void ArtifactsKeyringNonInteractiveModeTrueMakesSharedKeyringRequestSilent(
        string value
    )
    {
        var provider = new SuccessfulAcquisitionService();

        AdapterRunResult result = ExecuteKeyringCli(
            [
                "get",
                "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
                "requested-user",
            ],
            provider,
            new Dictionary<string, string?>
            {
                ["ARTIFACTS_KEYRING_NONINTERACTIVE_MODE"] = value,
            }
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Equal("phase11-secret\n", result.ProtocolStdout);
        CredentialRequestV2 request = Assert.Single(provider.Requests);
        Assert.Equal(InteractivePolicy.Never, request.InteractivePolicy);
        Assert.Equal(AcquisitionMode.SilentOnly, request.AcquisitionMode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("1")]
    [InlineData("unsupported")]
    public void ArtifactsKeyringNonInteractiveModeOtherValuesKeepRequestInteractive(
        string? value
    )
    {
        var provider = new SuccessfulAcquisitionService();
        KeyringHelperRequest helperRequest = CreateRequest(
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            username: null,
            KeyringHelperMode.Password
        );

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(helperRequest).Skip(1).ToArray(),
            credentialAcquisition: provider,
            environment: new Dictionary<string, string?>
            {
                ["ARTIFACTS_KEYRING_NONINTERACTIVE_MODE"] = value,
            }
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        CredentialRequestV2 request = Assert.Single(provider.Requests);
        Assert.Equal(InteractivePolicy.UserAllowed, request.InteractivePolicy);
        Assert.Equal(AcquisitionMode.InteractionAllowed, request.AcquisitionMode);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("1", true)]
    [InlineData("false", true)]
    [InlineData("unsupported", true)]
    public void AzureAuthNoUserUsesNonEmptyAzureAuthSemantics(
        string? value,
        bool expectSilent
    )
    {
        var provider = new SuccessfulAcquisitionService();
        KeyringHelperRequest helperRequest = CreateRequest(
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            username: null,
            KeyringHelperMode.Password
        );

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(helperRequest).Skip(1).ToArray(),
            credentialAcquisition: provider,
            environment: new Dictionary<string, string?>
            {
                ["AZUREAUTH_NO_USER"] = value,
            }
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        CredentialRequestV2 request = Assert.Single(provider.Requests);
        Assert.Equal(
            expectSilent ? InteractivePolicy.Never : InteractivePolicy.UserAllowed,
            request.InteractivePolicy
        );
        Assert.Equal(
            expectSilent ? AcquisitionMode.SilentOnly : AcquisitionMode.InteractionAllowed,
            request.AcquisitionMode
        );
    }

    [Theory]
    [InlineData("PIP_NO_INPUT", "1")]
    [InlineData("TWINE_NON_INTERACTIVE", "true")]
    public void CallerSpecificNonInteractiveSignalsDoNotSuppressSharedKeyringRequests(
        string variable,
        string value
    )
    {
        var provider = new SuccessfulAcquisitionService();
        KeyringHelperRequest helperRequest = CreateRequest(
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            username: null,
            KeyringHelperMode.Password
        );

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(helperRequest).Skip(1).ToArray(),
            credentialAcquisition: provider,
            environment: new Dictionary<string, string?> { [variable] = value }
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        CredentialRequestV2 request = Assert.Single(provider.Requests);
        Assert.Equal(InteractivePolicy.UserAllowed, request.InteractivePolicy);
        Assert.Equal(AcquisitionMode.InteractionAllowed, request.AcquisitionMode);
    }

    [Fact]
    public void KeyringCliPasswordModeWithoutUsernameFailsClosed()
    {
        var provider = new SuccessfulAcquisitionService();

        AdapterRunResult result = ExecuteKeyringCli(
            [
                "get",
                "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            ],
            provider
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Empty(result.HumanStdout);
        Assert.Contains("code=ProtocolViolation", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void KeyringCliUnsupportedHostReturnsNoCredentialWithoutAcquisition()
    {
        var provider = new SuccessfulAcquisitionService();

        AdapterRunResult result = ExecuteKeyringCli(
            ["--mode=creds", "get", "https://example.com/simple/"],
            provider
        );

        Assert.Equal(AdapterHostExitCode.NoCredential, result.Outcome.Result.ExitCode);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Empty(result.HumanStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Theory]
    [InlineData("unrelated-service")]
    [InlineData(" https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/ ")]
    [InlineData(
        "https://pkgs.dev.azure.com/org/project/_packaging/feed/pypi/download/package/1.2"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/project/_packaging/feed/pypi/download/"
            + "package/1.2/package.whl/extra"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/project/_packaging/feed/pypi/download/"
            + "package/1.2/package.whl/"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/project/_packaging/feed/pypi/not-download/"
            + "package/1.2/package.whl"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/project/_packaging/feed/pypi/download/"
            + "package//package.whl"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/download/"
            + "package/%252e%252e/package.whl"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/evil/%2e%2e/org/_packaging/feed/pypi/download/"
            + "package/1.2/package.whl"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/download/"
            + "package/%252f/package.whl"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/download/"
            + "package/%250a/package.whl"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/download/"
            + "package/1.2/bad%file.whl"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/download/"
            + "package/1.2/bad%.whl"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/download/"
            + "package/1.2/bad%2.whl"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/download/"
            + "%2e%2e/%2e/simple"
    )]
    public void KeyringCliMalformedServiceFailsClosed(string service)
    {
        var provider = new SuccessfulAcquisitionService();

        AdapterRunResult result = ExecuteKeyringCli(
            ["get", service, "requested-user"],
            provider
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Empty(result.HumanStdout);
        Assert.Contains("code=ProtocolViolation", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("bad\u0001user")]
    public void KeyringCliInvalidUsernameFailsClosed(string username)
    {
        var provider = new SuccessfulAcquisitionService();

        AdapterRunResult result = ExecuteKeyringCli(
            [
                "get",
                "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
                username,
            ],
            provider
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Empty(result.HumanStdout);
        Assert.Contains("code=ProtocolViolation", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void KeyringCliInvalidArgumentsFailWithoutEchoingUserInput()
    {
        const string SecretUsername = "must-not-leak-keyring-argument";
        var provider = new SuccessfulAcquisitionService();

        AdapterRunResult result = ExecuteKeyringCli(
            [
                "--output=plaintext",
                "get",
                "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
                SecretUsername,
            ],
            provider
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Empty(result.HumanStdout);
        Assert.DoesNotContain(SecretUsername, result.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void KeyringCliSharedApphostEntrypointStripsCommandToken()
    {
        string[] arguments =
        [
            "keyring",
            "--mode=creds",
            "get",
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
        ];

        bool resolved = KeyringCliAdapter.TryResolveProtocolInvocation(
            "/opt/azureauth-credprovider/app/azureauth-credprovider",
            arguments,
            out AdapterInvocationContext? context
        );

        Assert.True(resolved);
        Assert.NotNull(context);
        Assert.Equal(["keyring"], context.MatchedArguments);
        Assert.Equal(arguments[1..], context.PayloadArguments);
        Assert.Equal(AdapterProtocol.KeyringHelper, context.Protocol);
    }

    private static AdapterRunResult Execute(
        string[] args,
        string executablePath = "/usr/local/bin/python-keyring",
        CredentialCoreService? credentialCore = null,
        ICredentialAcquisitionService? credentialAcquisition = null,
        Dictionary<string, string?>? environment = null
    )
    {
        var protocolStdout = new StringWriter();
        var humanStdout = new StringWriter();
        var stderr = new StringWriter();
        var diagnosticRouter = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(stderr)],
            SecretRedactor.Empty
        );
        AdapterHostExecutionOutcome outcome = new KeyringHelperAdapter(
            credentialAcquisition is null
                ? credentialCore is null
                    ? CredentialProviderCompositionRoot.CreateProduction().AcquisitionService
                    : new LegacyV1CredentialAcquisitionService(credentialCore)
                : credentialAcquisition,
            name =>
                environment is not null && environment.TryGetValue(name, out string? value)
                    ? value
                    : null
        ).Execute(executablePath, args, protocolStdout, humanStdout, diagnosticRouter);

        return new AdapterRunResult(
            outcome,
            protocolStdout.ToString(),
            humanStdout.ToString(),
            stderr.ToString()
        );
    }

    private static AdapterRunResult ExecuteKeyringCli(
        string[] args,
        ICredentialAcquisitionService credentialAcquisition,
        Dictionary<string, string?>? environment = null
    )
    {
        var protocolStdout = new StringWriter();
        var humanStdout = new StringWriter();
        var stderr = new StringWriter();
        var diagnosticRouter = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(stderr)],
            SecretRedactor.Empty
        );
        AdapterHostExecutionOutcome outcome = new KeyringCliAdapter(
            credentialAcquisition,
            name =>
                environment is not null && environment.TryGetValue(name, out string? value)
                    ? value
                    : null
        ).Execute(
            "/opt/azureauth-credprovider/app/azureauth-credprovider",
            [KeyringCliAdapter.CommandName, .. args],
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

    private static KeyringHelperRequest CreateRequest(
        string service,
        string? username,
        KeyringHelperMode mode
    )
    {
        return new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri(service),
            Username = username,
            Mode = mode,
        };
    }

    private static CredentialCoreService CreateCredentialCore(TokenExchangeResult exchangeResult)
    {
        return new CredentialCoreService(
            new CapturingIdentityProvider(),
            diagnosticRouter: null,
            derivedCredentialCache: null,
            tokenExchange: new FixedTokenExchange(exchangeResult)
        );
    }

    private sealed class SuccessfulAcquisitionService(
        string username = "AzureDevOps",
        string password = "phase11-secret"
    ) : ICredentialAcquisitionService
    {
        public int InvocationCount => Requests.Count;

        public List<CredentialRequestV2> Requests { get; } = [];

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            Requests.Add(request);
            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    Username = username,
                    Password = password,
                    DiagnosticsCorrelationId = "keyring-test",
                }
            );
        }
    }

    private sealed class MismatchSensitiveAcquisitionService : ICredentialAcquisitionService
    {
        public bool BindingMismatchDetected { get; private set; }

        public int InvocationCount => Requests.Count;

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
                        DiagnosticsCorrelationId = "keyring-binding-mismatch-test",
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
                    Password = "phase11-secret",
                    DiagnosticsCorrelationId = "keyring-binding-match-test",
                }
            );
        }
    }

    private sealed class FixedResultAcquisitionService(
        CredentialResultStatus status,
        CredentialErrorKind kind,
        string code
    ) : ICredentialAcquisitionService
    {
        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        ) =>
            ValueTask.FromResult(
                new CredentialResult
                {
                    Status = status,
                    DiagnosticsCorrelationId = "keyring-failure-test",
                    Error = new CredentialError
                    {
                        Kind = kind,
                        Code = code,
                        SafeMessage = "Credential acquisition failed.",
                    },
                }
            );
    }

    private sealed class CapturingIdentityProvider : IIdentityProvider
    {
        public int InvocationCount { get; private set; }

        public List<CredentialRequest> Requests { get; } = [];

        public IdentityMaterial GetIdentity(CredentialRequest request)
        {
            InvocationCount++;
            Requests.Add(request);
            return new IdentityMaterial
            {
                Account = request.AccountHint ?? "default@org.example",
                Tenant = "tenant",
                Secret = "phase11-secret",
                AccessToken = "phase11-token",
                ExpiresAt = ExpiresAt,
            };
        }
    }

    private sealed class FixedTokenExchange(TokenExchangeResult exchangeResult) : ITokenExchange
    {
        public TokenExchangeResult Exchange(
            CredentialRequest request,
            IdentityMaterial identity,
            CacheKey cacheKey
        )
        {
            return exchangeResult;
        }
    }

    private sealed record AdapterRunResult(
        AdapterHostExecutionOutcome Outcome,
        string ProtocolStdout,
        string HumanStdout,
        string Stderr
    );

    [Fact]
    public void GetPasswordAllowsBrowserInteractionAndKeepsHumanStdoutEmpty()
    {
        var credentialAcquisition = new SuccessfulAcquisitionService();
        KeyringHelperRequest helperRequest = CreateRequest(
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            username: null,
            KeyringHelperMode.Password
        );

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(helperRequest).Skip(1).ToArray(),
            credentialAcquisition: credentialAcquisition
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.True(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal("phase11-secret\n", result.ProtocolStdout);
        Assert.Empty(result.HumanStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.DoesNotContain("phase11-secret", result.HumanStdout, StringComparison.Ordinal);

        CredentialRequestV2 request = Assert.Single(credentialAcquisition.Requests);
        Assert.Equal(CredentialEcosystem.Python, request.Ecosystem);
        Assert.Equal(CredentialOperation.Get, request.Operation);
        Assert.Equal(TokenAudience.AzureArtifacts, request.RequestedAudience);
        Assert.Equal(CredentialKind.BasicPassword, request.CredentialKind);
        Assert.Equal(IdentityFlow.InteractiveBrowser, request.IdentityFlow);
        Assert.NotEqual(IdentityFlow.DeviceCode, request.IdentityFlow);
        Assert.Equal(InteractivePolicy.UserAllowed, request.InteractivePolicy);
        Assert.Equal(AcquisitionMode.InteractionAllowed, request.AcquisitionMode);
        Assert.Equal(CachePolicyMode.ProductPersistentCacheDisabled, request.CachePolicy);
        CiContext ciContext = Assert.IsType<CiContext>(request.CiContext);
        Assert.False(ciContext.ExplicitCiMode);
        Assert.False(ciContext.AllowsPersistentWrites);
        IReadOnlyDictionary<string, string> extensionData = Assert.IsAssignableFrom<
            IReadOnlyDictionary<string, string>
        >(request.ExtensionData);
        Assert.Equal("password", extensionData["python.keyring.mode"]);
    }

    [Fact]
    public void GetPasswordInteractiveRequestPreservesDefaultServiceAndCanonicalResource()
    {
        var credentialAcquisition = new SuccessfulAcquisitionService();
        KeyringHelperRequest helperRequest = CreateRequest(
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            username: null,
            KeyringHelperMode.Password
        );

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(helperRequest).Skip(1).ToArray(),
            credentialAcquisition: credentialAcquisition
        );

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Empty(result.HumanStdout);
        Assert.Equal(string.Empty, result.Stderr);
        CredentialRequestV2 request = Assert.Single(credentialAcquisition.Requests);
        Assert.Equal("default", request.ServiceIdentity);
        CanonicalResourceIdentity resource = request.Resource;
        Assert.Equal("pkgs.dev.azure.com", resource.AzureDevOpsHost);
        Assert.Equal("org", resource.Organization);
        Assert.Null(resource.Project);
        Assert.Equal("feed", resource.Feed);
        Assert.Null(resource.Repository);
        Assert.Equal(
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            resource.ServiceEndpoint
        );
        Assert.Equal(IdentityFlow.InteractiveBrowser, request.IdentityFlow);
        Assert.Equal(InteractivePolicy.UserAllowed, request.InteractivePolicy);
        Assert.Equal(AcquisitionMode.InteractionAllowed, request.AcquisitionMode);
        Assert.Equal("phase11-secret\n", result.ProtocolStdout);
    }
}
