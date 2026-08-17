using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class CredentialProviderCompositionRootKeyringTests
{
    private const string ArtifactsKeyringNonInteractiveMode =
        "ARTIFACTS_KEYRING_NONINTERACTIVE_MODE";
    private const string Service = "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/";

    [Fact]
    public void CreateKeyringHelperAdapterUsesProductionEnvironmentVariableReader()
    {
        var acquisition = new RecordingAcquisitionService();
        var environment = new RecordingEnvironmentVariableReader();
        CredentialProviderCompositionRoot root = CreateRoot(acquisition, environment);
        KeyringHelperRequest request = new()
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri(Service),
            Mode = KeyringHelperMode.Password,
        };

        AdapterHostExecutionOutcome outcome = root.CreateKeyringHelperAdapter()
            .Execute(
                "/usr/local/bin/python-keyring",
                KeyringHelperV2.BuildArguments(request).Skip(1).ToArray(),
                new StringWriter(),
                new StringWriter(),
                CreateDiagnosticRouter()
            );

        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        AssertSilentRequest(acquisition);
        Assert.Equal([ArtifactsKeyringNonInteractiveMode], environment.RequestedVariables);
    }

    [Fact]
    public void CreateKeyringCliAdapterUsesProductionEnvironmentVariableReader()
    {
        var acquisition = new RecordingAcquisitionService();
        var environment = new RecordingEnvironmentVariableReader();
        CredentialProviderCompositionRoot root = CreateRoot(acquisition, environment);

        AdapterHostExecutionOutcome outcome = root.CreateKeyringCliAdapter()
            .Execute(
                "/opt/azureauth-credprovider/app/azureauth-credprovider",
                [KeyringCliAdapter.CommandName, "get", Service, "requested-user"],
                new StringWriter(),
                new StringWriter(),
                CreateDiagnosticRouter()
            );

        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        AssertSilentRequest(acquisition);
        Assert.Equal([ArtifactsKeyringNonInteractiveMode], environment.RequestedVariables);
    }

    private static CredentialProviderCompositionRoot CreateRoot(
        RecordingAcquisitionService acquisition,
        RecordingEnvironmentVariableReader environment
    ) =>
        CredentialProviderCompositionRoot.CreateExplicitTestScaffold(
            acquisition,
            new CredentialProviderProductionOptions { EnvironmentVariableReader = environment.Read }
        );

    private static DiagnosticRouter CreateDiagnosticRouter() => new([], SecretRedactor.Empty);

    private static void AssertSilentRequest(RecordingAcquisitionService acquisition)
    {
        CredentialRequestV2 request = Assert.Single(acquisition.Requests);
        Assert.Equal(InteractivePolicy.Never, request.InteractivePolicy);
        Assert.Equal(AcquisitionMode.SilentOnly, request.AcquisitionMode);
    }

    private sealed class RecordingEnvironmentVariableReader
    {
        public List<string> RequestedVariables { get; } = [];

        public string? Read(string name)
        {
            RequestedVariables.Add(name);
            return name == ArtifactsKeyringNonInteractiveMode ? "true" : null;
        }
    }

    private sealed class RecordingAcquisitionService : ICredentialAcquisitionService
    {
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
                    Username = "AzureDevOps",
                    Password = "composition-secret",
                    DiagnosticsCorrelationId = "composition-keyring-test",
                }
            );
        }
    }
}
