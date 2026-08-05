using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
public sealed class NuGetPhase10VerticalSliceServiceTests
{
    [Fact]
    public async Task DryRunConfigurePlansCanonicalNuGetPluginLayoutMarker()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);

        NuGetPhase10ConfigureDryRunResult result = await service.DryRunConfigureAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.Validation.IsValid);
        Assert.Equal(ConfigurationPlanOperation.DryRun, result.PlanResult.Operation);
        ConfigurationPlannedChange change = Assert.Single(result.PlanResult.Changes);
        Assert.Equal(ConfigurationChangeOperation.Set, change.Operation);
        Assert.Equal(ConfigurationTargetKind.NuGetPluginLayout, change.TargetKind);
        Assert.Equal(service.Paths.PluginTargetRootPath, change.TargetPathOrName);
        Assert.Equal("physical-target", change.Key);
        Assert.True(change.HasPlannedValue);
        Assert.False(fileSystem.FileExists(service.Paths.PluginLayoutMarkerPath));
        Assert.False(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
    }

    [Fact]
    public async Task ConfigureWritesMarkerAndManifestAndDoctorPassesWhenEntrypointExists()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        fileSystem.AtomicWriteAllText(service.Paths.PluginEntrypointPath, "fake-assembly");

        NuGetPhase10ConfigureResult configureResult = await service.ConfigureAsync(
            TestContext.Current.CancellationToken
        );
        NuGetPhase10DoctorResult doctorResult = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.NotEqual(ConfigurationPlanOperation.DryRun, configureResult.PlanResult.Operation);
        Assert.True(configureResult.PluginLayoutMarkerPresent);
        Assert.True(configureResult.OwnershipManifestPresent);
        Assert.Equal(
            NuGetPhase10VerticalSliceService.MarkerValue,
            fileSystem.ReadAllText(service.Paths.PluginLayoutMarkerPath)
        );
        Assert.True(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
        Assert.True(doctorResult.ConfigurationPlanValid);
        Assert.True(doctorResult.PluginLayoutMarkerPresent);
        Assert.True(doctorResult.OwnershipManifestPresent);
        Assert.True(doctorResult.NetCorePluginEntrypointPresent);
        Assert.True(doctorResult.PluginModeEntrypointResolvable);
        Assert.False(doctorResult.AzureArtifactsSourceCanonicalizationSuccess);
        Assert.False(doctorResult.InteractivePolicyGuidanceSuccess);
        Assert.True(doctorResult.OptionalEnvironmentOverridesAbsent);
    }

    [Fact]
    public async Task UnconfigureRemovesManifestAndLayoutMarker()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        fileSystem.AtomicWriteAllText(service.Paths.PluginEntrypointPath, "fake-assembly");
        await service.ConfigureAsync(TestContext.Current.CancellationToken);

        NuGetPhase10UnconfigureResult result = await service.UnconfigureAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.HadOwnedConfiguration);
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, result.PlanResult?.Operation);
        Assert.False(result.PluginLayoutMarkerPresent);
        Assert.False(result.OwnershipManifestPresent);
        Assert.False(fileSystem.FileExists(service.Paths.PluginLayoutMarkerPath));
        Assert.False(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
    }

    [Fact]
    public async Task DoctorReportsEnvironmentOverrideConflict()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(
            fileSystem,
            name =>
                string.Equals(name, "NUGET_NETCORE_PLUGIN_PATHS", StringComparison.Ordinal)
                    ? "/tmp/plugin.dll"
                    : null
        );
        fileSystem.AtomicWriteAllText(service.Paths.PluginEntrypointPath, "fake-assembly");
        await service.ConfigureAsync(TestContext.Current.CancellationToken);

        NuGetPhase10DoctorResult result = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.False(result.OptionalEnvironmentOverridesAbsent);
        Assert.True(result.PluginLayoutMarkerPresent);
        Assert.True(result.OwnershipManifestPresent);
    }

    [Fact]
    public async Task DoctorReportsMissingPluginEntrypoint()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(TestContext.Current.CancellationToken);

        NuGetPhase10DoctorResult result = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.PluginLayoutMarkerPresent);
        Assert.True(result.OwnershipManifestPresent);
        Assert.False(result.NetCorePluginEntrypointPresent);
        Assert.False(result.PluginModeEntrypointResolvable);
    }

    [Fact]
    public async Task ConfigureRejectsProductOwnedMarkerWithoutManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        fileSystem.AtomicWriteAllText(
            service.Paths.PluginLayoutMarkerPath,
            NuGetPhase10VerticalSliceService.MarkerValue
        );

        await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
            await service.ConfigureAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task ConfigureTreatsMalformedOwnershipManifestAsUnrecognizedState()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        fileSystem.AtomicWriteAllText(service.Paths.OwnershipManifestPath, "{");

        await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
            await service.ConfigureAsync(TestContext.Current.CancellationToken)
        );
    }

    private static NuGetPhase10VerticalSliceService CreateService(
        InMemoryFileSystem fileSystem,
        Func<string, string?>? environmentVariableReader = null
    ) =>
        new(
            new NuGetPhase10VerticalSliceOptions
            {
                StateDirectoryPath = "/state/azureauth-credprovider/phase10",
                FileSystem = fileSystem,
                EnvironmentVariableReader = environmentVariableReader ?? (_ => null),
            }
        );

    [Theory]
    [InlineData("https://pkgs.dev.azure.com/contoso/_packaging/Feed/nuget/v3/index.json")]
    [InlineData("https://pkgs.dev.azure.com/contoso/Project/_packaging/Feed/nuget/v3/index.json")]
    [InlineData("https://contoso.pkgs.visualstudio.com/_packaging/Feed/nuget/v3/index.json")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "The underscores separate the protocol condition from the expected result."
    )]
    public async Task DoctorAsync_ForSupportedAzureArtifactsSource_RequiresProductionNuGetBasicCredentialShape(
        string source
    )
    {
        const string opaquePassword = "opaque-session-token-7c9e2d41";
        string productionUsername = Hcoona
            .AzureAuth
            .CredProvider
            .Platform
            .TokenMaterialization
            .CredentialFormPolicy
            .NuGetSessionTokenUsername;

        Assert.Equal("VssSessionToken", productionUsername);
        Assert.NotEqual("AzureDevOps", productionUsername);
        Assert.False(string.IsNullOrWhiteSpace(opaquePassword));
        Assert.NotEqual("fake-secret-nuget", opaquePassword);
        Assert.False(opaquePassword.Contains("fake", StringComparison.OrdinalIgnoreCase));
        Assert.False(opaquePassword.Contains("scaffold", StringComparison.OrdinalIgnoreCase));

        CredentialResult productionCredential = CreateSuccessfulCredential(
            productionUsername,
            opaquePassword
        );
        DoctorArrangement production = await RunDoctorArrangementAsync(
            source,
            productionCredential
        );
        Assert.Equal(
            NuGet.Protocol.Plugins.MessageResponseCode.Success,
            production.SourceResponse.ResponseCode
        );
        Assert.Equal(productionUsername, production.SourceResponse.Username);
        Assert.Equal(opaquePassword, production.SourceResponse.Password);
        Assert.Equal(["Basic"], production.SourceResponse.AuthenticationTypes);
        Assert.True(production.Result.InteractivePolicyGuidanceSuccess);
        AssertDoctorRequests(production.Acquisition.Requests, source);

        DoctorArrangement scaffoldCredential = await RunDoctorArrangementAsync(
            source,
            CreateSuccessfulCredential("AzureDevOps", "fake-secret-nuget")
        );
        Assert.True(scaffoldCredential.Result.InteractivePolicyGuidanceSuccess);
        AssertDoctorRequests(scaffoldCredential.Acquisition.Requests, source);

        DoctorArrangement emptyPassword = await RunDoctorArrangementAsync(
            source,
            CreateSuccessfulCredential(productionUsername, string.Empty)
        );
        Assert.True(emptyPassword.Result.InteractivePolicyGuidanceSuccess);
        AssertDoctorRequests(emptyPassword.Acquisition.Requests, source);

        Assert.True(production.Result.AzureArtifactsSourceCanonicalizationSuccess);
        Assert.False(scaffoldCredential.Result.AzureArtifactsSourceCanonicalizationSuccess);
        Assert.False(emptyPassword.Result.AzureArtifactsSourceCanonicalizationSuccess);
    }

    private static async Task<DoctorArrangement> RunDoctorArrangementAsync(
        string source,
        CredentialResult interactiveCredential
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        var acquisition = new ScriptedNuGetDoctorCredentialAcquisitionService(
            interactiveCredential
        );
        var boundedAcquisition =
            new Hcoona.AzureAuth.CredProvider.Platform.Composition.BoundedCredentialAcquisitionAdapter(
                acquisition
            );
        var adapter = new Hcoona.AzureAuth.CredProvider.Platform.AdapterHost.NuGetPluginAdapter(
            boundedAcquisition
        );
        NuGet.Protocol.Plugins.GetAuthenticationCredentialsResponse sourceResponse =
            await adapter.HandleGetAuthenticationCredentialsAsync(
                new NuGet.Protocol.Plugins.GetAuthenticationCredentialsRequest(
                    new Uri(source),
                    isRetry: false,
                    isNonInteractive: false,
                    canShowDialog: true
                ),
                TestContext.Current.CancellationToken
            );
        var service = new NuGetPhase10VerticalSliceService(
            new NuGetPhase10VerticalSliceOptions
            {
                StateDirectoryPath = "/state/azureauth-credprovider/phase10",
                FileSystem = fileSystem,
                EnvironmentVariableReader = _ => null,
                CredentialAcquisition = boundedAcquisition,
            }
        );
        fileSystem.AtomicWriteAllText(service.Paths.PluginEntrypointPath, "fake-assembly");
        await service.ConfigureAsync(TestContext.Current.CancellationToken);

        NuGetPhase10DoctorResult result = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        return new DoctorArrangement(result, sourceResponse, acquisition);
    }

    private static CredentialResult CreateSuccessfulCredential(string username, string password) =>
        new()
        {
            Status = CredentialResultStatus.Success,
            Username = username,
            Password = password,
            DiagnosticsCorrelationId = "nuget-doctor-production-contract",
        };

    [Fact]
    public void ProductionCredentialResponseValidationRequiresVssSessionTokenOpaquePasswordAndBasicAuthentication()
    {
        const string opaquePassword = "opaque-session-token-7c9e2d41";
        const string productionUsername = "VssSessionToken";

        Assert.True(
            NuGetPhase10VerticalSliceService.IsProductionCredentialResponse(
                CreateCredentialResponse(productionUsername, opaquePassword, ["Basic"])
            )
        );
        Assert.False(
            NuGetPhase10VerticalSliceService.IsProductionCredentialResponse(
                CreateCredentialResponse("AzureDevOps", opaquePassword, ["Basic"])
            )
        );
        Assert.False(
            NuGetPhase10VerticalSliceService.IsProductionCredentialResponse(
                CreateCredentialResponse(productionUsername, string.Empty, ["Basic"])
            )
        );
        Assert.False(
            NuGetPhase10VerticalSliceService.IsProductionCredentialResponse(
                CreateCredentialResponse(productionUsername, opaquePassword, ["Bearer"])
            )
        );
        Assert.False(
            NuGetPhase10VerticalSliceService.IsProductionCredentialResponse(
                CreateCredentialResponse(productionUsername, opaquePassword, ["Basic", "Bearer"])
            )
        );
    }

    private static NuGet.Protocol.Plugins.GetAuthenticationCredentialsResponse CreateCredentialResponse(
        string username,
        string password,
        IList<string> authenticationTypes
    ) =>
        new(
            username,
            password,
            message: null,
            authenticationTypes,
            NuGet.Protocol.Plugins.MessageResponseCode.Success
        );

    private static void AssertDoctorRequests(List<CredentialRequestV2> requests, string source)
    {
        Assert.True(requests.Count >= 3);
        Assert.Contains(requests, request => request.Resource.ServiceEndpoint == new Uri(source));
        Assert.Equal(
            requests.Count - 1,
            requests.Count(request => request.InteractivePolicy == InteractivePolicy.HostToolAllows)
        );
        CredentialRequestV2 nonInteractiveRequest = Assert.Single(
            requests,
            request => request.InteractivePolicy == InteractivePolicy.Never
        );
        Assert.Equal(AcquisitionMode.SilentOnly, nonInteractiveRequest.AcquisitionMode);
    }

    private sealed record DoctorArrangement(
        NuGetPhase10DoctorResult Result,
        NuGet.Protocol.Plugins.GetAuthenticationCredentialsResponse SourceResponse,
        ScriptedNuGetDoctorCredentialAcquisitionService Acquisition
    );

    private sealed class ScriptedNuGetDoctorCredentialAcquisitionService(
        CredentialResult interactiveCredential
    ) : Hcoona.AzureAuth.CredProvider.Platform.Composition.ICredentialAcquisitionService
    {
        public List<CredentialRequestV2> Requests { get; } = [];

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);

            return ValueTask.FromResult(
                request.InteractivePolicy == InteractivePolicy.Never
                    ? new CredentialResult
                    {
                        Status = CredentialResultStatus.InteractionBlocked,
                        DiagnosticsCorrelationId = "nuget-doctor-interaction-blocked",
                        Error = new CredentialError
                        {
                            Kind = CredentialErrorKind.InteractionBlocked,
                            Code = "InteractionBlocked",
                            SafeMessage = "interaction is blocked",
                        },
                    }
                    : interactiveCredential
            );
        }
    }
}
