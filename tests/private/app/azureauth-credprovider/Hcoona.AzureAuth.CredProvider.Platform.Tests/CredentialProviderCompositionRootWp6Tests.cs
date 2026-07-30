using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class CredentialProviderCompositionRootWp6Tests
{
    [Fact]
    public void MissingConfigurationIsProviderNotConfiguredWithoutProductionFake()
    {
        string rootPath = CreateTestDirectory();
        try
        {
            CredentialProviderCompositionRoot root =
                CredentialProviderCompositionRoot.CreateProduction(
                    new CredentialProviderProductionOptions { SecureStoreRootPath = rootPath }
                );

            Assert.Equal(AzureAuthProviderSelection.Unspecified, root.ProviderConfig.Selection);
            Assert.False(root.Readiness.Interactive.IsReady);
            Assert.Equal("ProviderNotConfigured", root.Readiness.Interactive.Code);
            Assert.False(root.Readiness.Silent.IsReady);
            Assert.Equal(CredentialProviderCompositionMode.Production, root.Mode);
            AzureAuthDoctorReport doctor = root.RunProviderDoctor(
                TestContext.Current.CancellationToken
            );
            Assert.Equal(
                AzureAuthDoctorCheckStatus.Fail,
                doctor.Checks.Single(check => check.Code == "provider-selection").Status
            );
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public void AzureAuthReadinessDiscoversInstallationOnceAndKeepsSilentIndependent()
    {
        string rootPath = CreateTestDirectory();
        try
        {
            AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
            AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
                config,
                "user@example.com",
                "tenant",
                DateTimeOffset.UtcNow
            );
            var discovery = new CountingDiscovery(
                AzureAuthInstallation.Available(
                    @"C:\Users\User\AppData\Local\Programs\AzureAuth\0.9.5\azureauth.exe",
                    "/mnt/c/Users/User/AppData/Local/Programs/AzureAuth/0.9.5/azureauth.exe",
                    "0.9.5"
                )
            );

            CredentialProviderCompositionRoot root =
                CredentialProviderCompositionRoot.CreateProduction(
                    new CredentialProviderProductionOptions
                    {
                        SecureStoreRootPath = rootPath,
                        ProviderConfig = config,
                        Binding = binding,
                        InstallationDiscovery = discovery,
                    }
                );

            Assert.True(root.Readiness.Interactive.IsReady);
            Assert.True(root.Readiness.IsReady);
            Assert.False(root.Readiness.Silent.IsReady);
            Assert.Equal("SilentAcquisitionUnavailable", root.Readiness.Silent.Code);
            Assert.Equal(1, discovery.CallCount);
            _ = root.GetReadiness(TestContext.Current.CancellationToken);
            _ = root.RunProviderDoctor(TestContext.Current.CancellationToken);
            Assert.Equal(1, discovery.CallCount);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Theory]
    [InlineData(AzureAuthInstallationStatus.Missing, "AzureAuthInstallationMissing")]
    [InlineData(AzureAuthInstallationStatus.WrongVersion, "AzureAuthVersionMismatch")]
    public void InstallationFailuresAreInteractiveBlockers(
        AzureAuthInstallationStatus status,
        string code
    )
    {
        string rootPath = CreateTestDirectory();
        try
        {
            AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
            AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
                config,
                null,
                "tenant",
                DateTimeOffset.UtcNow
            );

            CredentialProviderCompositionRoot root =
                CredentialProviderCompositionRoot.CreateProduction(
                    new CredentialProviderProductionOptions
                    {
                        SecureStoreRootPath = rootPath,
                        ProviderConfig = config,
                        Binding = binding,
                        InstallationDiscovery = new CountingDiscovery(
                            AzureAuthInstallation.Failure(status, code, "actionable")
                        ),
                    }
                );

            Assert.False(root.Readiness.Interactive.IsReady);
            Assert.Equal(code, root.Readiness.Interactive.Code);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public void ExplicitTestScaffoldRemainsClearlyNonProduction()
    {
        var service = new CooperativeService();
        CredentialProviderCompositionRoot root =
            CredentialProviderCompositionRoot.CreateExplicitTestScaffold(service);

        Assert.Equal(CredentialProviderCompositionMode.TestScaffold, root.Mode);
        Assert.Equal("TestScaffold", root.Readiness.Interactive.Code);
        Assert.False(root.Readiness.IsReady);
    }

    [Fact]
    public void SynchronousBoundaryBlocksStraightforwardlyOnCooperativeAsync()
    {
        var adapter = new BoundedCredentialAcquisitionAdapter(new CooperativeService());

        CredentialResult result = adapter.Acquire(
            CreateRequest(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CredentialResultStatus.CredentialUnavailable, result.Status);
        Assert.Equal("cooperative", result.Error!.Code);
    }

    private static CredentialRequestV2 CreateRequest() =>
        new()
        {
            ContractMajor = ContractVersions.CredentialContractV2Major,
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("https://dev.azure.com/org")
            ),
            ServiceIdentity = "default",
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = CredentialKind.BearerToken,
            IdentityFlow = IdentityFlow.InteractiveBrowser,
            InteractivePolicy = InteractivePolicy.UserAllowed,
            AcquisitionMode = AcquisitionMode.InteractionAllowed,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            CiContext = new CiContext { ExplicitCiMode = false, AllowsPersistentWrites = false },
        };

    private static string CreateTestDirectory()
    {
        string path = Path.Combine(
            Environment.CurrentDirectory,
            ".test-output",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CountingDiscovery(AzureAuthInstallation installation)
        : IAzureAuthInstallationDiscovery
    {
        public int CallCount { get; private set; }

        public AzureAuthInstallation Discover(
            AzureAuthProviderConfig config,
            CancellationToken cancellationToken = default
        )
        {
            CallCount++;
            return installation;
        }
    }

    private sealed class CooperativeService : ICredentialAcquisitionService
    {
        public async ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return new CredentialResult
            {
                Status = CredentialResultStatus.CredentialUnavailable,
                Error = new CredentialError
                {
                    Kind = CredentialErrorKind.CredentialUnavailable,
                    Code = "cooperative",
                    SafeMessage = "cooperative",
                },
                DiagnosticsCorrelationId = CorrelationId.New().ToString(),
            };
        }
    }

    private sealed class CountingProcessRunner : IProcessRunner
    {
        public int CallCount { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            CallCount++;
            throw new InvalidOperationException("The AzureAuth process must not be launched.");
        }
    }

    [Fact]
    public void PreCanceledAzureAuthSilentOnlyRequestReturnsCanceledBeforeSynchronousPreflight()
    {
        string rootPath = CreateTestDirectory();
        try
        {
            AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
            AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
                config,
                "user@example.com",
                "tenant",
                DateTimeOffset.UtcNow
            );
            var processRunner = new CountingProcessRunner();
            var discovery = new CountingDiscovery(
                AzureAuthInstallation.Available(
                    @"C:\AzureAuth\azureauth.exe",
                    "/mnt/c/AzureAuth/azureauth.exe",
                    "0.9.5"
                )
            );
            CredentialProviderCompositionRoot root =
                CredentialProviderCompositionRoot.CreateProduction(
                    new CredentialProviderProductionOptions
                    {
                        SecureStoreRootPath = rootPath,
                        ProviderConfig = config,
                        Binding = binding,
                        InstallationDiscovery = discovery,
                        ProcessRunner = processRunner,
                    }
                );
            CredentialRequestV2 request = CreateRequest() with
            {
                InteractivePolicy = InteractivePolicy.Never,
                AcquisitionMode = AcquisitionMode.SilentOnly,
            };
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            int discoveryCallsBeforeAcquire = discovery.CallCount;

            Assert.True(CredentialRequestV2Policy.IsValid(request));

            CredentialResult result = root.Boundary.Acquire(request, cancellation.Token);

            Assert.Equal(CredentialResultStatus.CredentialUnavailable, result.Status);
            Assert.Equal(CredentialErrorKind.CredentialUnavailable, result.Error?.Kind);
            Assert.Equal("CredentialAcquisitionCanceled", result.Error?.Code);
            Assert.False(result.ContainsCredentialMaterial);
            Assert.Equal(discoveryCallsBeforeAcquire, discovery.CallCount);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }
}
