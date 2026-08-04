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
                    "0.9.5",
                    AzureAuthHostPlatform.Wsl
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

    [Fact]
    public void NativeLinuxReadinessIncludesCacheOnlyAcquisition()
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
            var discovery = new CountingDiscovery(
                AzureAuthInstallation.Available(
                    "/usr/lib/azureauth/azureauth",
                    "/usr/lib/azureauth/azureauth",
                    "0.9.5",
                    AzureAuthHostPlatform.NativeLinux
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
            Assert.True(root.Readiness.Silent.IsReady);
            Assert.Equal("AzureAuthSilentReady", root.Readiness.Silent.Code);
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

    [Fact]
    public async Task CreateProductionWithPromptWriterRoutesUserAllowedDeviceCodePrompt()
    {
        const string DeviceToken = "composition-device-private-token";
        const string BrowserToken = "composition-browser-private-token";
        string rootPath = CreateTestDirectory();
        var promptWriter = new StringWriter();
        var processRunner = new CompositionRecordingRunner(
            new ProcessResult(0, DeviceToken, string.Empty),
            new ProcessResult(0, BrowserToken, string.Empty)
        );
        try
        {
            AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
            AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
                config,
                "composition.user@example.com",
                "tenant-composition",
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
                            AzureAuthInstallation.Available(
                                "/usr/lib/azureauth/azureauth",
                                "/usr/lib/azureauth/azureauth",
                                "0.9.5",
                                AzureAuthHostPlatform.NativeLinux
                            )
                        ),
                        ProcessRunner = processRunner,
                        DeviceCodePromptWriter = promptWriter,
                    }
                );
            CredentialRequestV2 deviceRequest = CreateRequest() with
            {
                AccountHint = "composition.user@example.com",
                TenantHint = "tenant-composition",
                CredentialKind = CredentialKind.BasicPassword,
                IdentityFlow = IdentityFlow.DeviceCode,
                InteractivePolicy = InteractivePolicy.UserAllowed,
                AcquisitionMode = AcquisitionMode.InteractionAllowed,
            };
            CredentialRequestV2 browserRequest = CreateRequest() with
            {
                AccountHint = "composition.user@example.com",
                TenantHint = "tenant-composition",
                CredentialKind = CredentialKind.BasicPassword,
                IdentityFlow = IdentityFlow.InteractiveBrowser,
                InteractivePolicy = InteractivePolicy.UserAllowed,
                AcquisitionMode = AcquisitionMode.InteractionAllowed,
            };

            CredentialResult deviceResult = await root.AcquisitionService.AcquireAsync(
                deviceRequest,
                TestContext.Current.CancellationToken
            );
            CredentialResult browserResult = await root.AcquisitionService.AcquireAsync(
                browserRequest,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(CredentialResultStatus.Success, deviceResult.Status);
            Assert.Equal(CredentialResultStatus.Success, browserResult.Status);
            Assert.Equal(DeviceToken, deviceResult.Password);
            Assert.Equal(BrowserToken, browserResult.Password);
            Assert.Equal(2, processRunner.StartSpecs.Count);
            Assert.Same(promptWriter, processRunner.StartSpecs[0].StandardErrorTee);
            Assert.Null(processRunner.StartSpecs[1].StandardErrorTee);
            Assert.Contains("devicecode", processRunner.StartSpecs[0].Arguments);
            Assert.Contains("web", processRunner.StartSpecs[1].Arguments);
            Assert.Same(promptWriter, root.ProductionOptions.DeviceCodePromptWriter);
            Assert.True(root.Readiness.Interactive.IsReady);
            Assert.DoesNotContain(DeviceToken, promptWriter.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(BrowserToken, promptWriter.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CreateProductionWithoutPromptWriterMapsValidDeviceCodeToInteractionBlocked()
    {
        string rootPath = CreateTestDirectory();
        var processRunner = new CountingProcessRunner();
        try
        {
            AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
            AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
                config,
                "composition.user@example.com",
                "tenant-composition",
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
                            AzureAuthInstallation.Available(
                                "/usr/lib/azureauth/azureauth",
                                "/usr/lib/azureauth/azureauth",
                                "0.9.5",
                                AzureAuthHostPlatform.NativeLinux
                            )
                        ),
                        ProcessRunner = processRunner,
                    }
                );
            CredentialRequestV2 request = CreateRequest() with
            {
                AccountHint = "composition.user@example.com",
                TenantHint = "tenant-composition",
                CredentialKind = CredentialKind.BasicPassword,
                IdentityFlow = IdentityFlow.DeviceCode,
                InteractivePolicy = InteractivePolicy.UserAllowed,
                AcquisitionMode = AcquisitionMode.InteractionAllowed,
            };

            Assert.True(CredentialRequestV2Policy.IsValid(request));

            CredentialResult result = await root.AcquisitionService.AcquireAsync(
                request,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(CredentialResultStatus.InteractionBlocked, result.Status);
            Assert.NotEqual(CredentialResultStatus.ProtocolViolation, result.Status);
            Assert.Equal(CredentialErrorKind.InteractionBlocked, result.Error?.Kind);
            Assert.Equal("AzureAuthDeviceCodePromptUnavailable", result.Error?.Code);
            Assert.False(result.ContainsCredentialMaterial);
            Assert.Null(root.ProductionOptions.DeviceCodePromptWriter);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CreateProductionRejectsHostToolAllowsBeforePromptWriterValidation()
    {
        string rootPath = CreateTestDirectory();
        var processRunner = new CountingProcessRunner();
        try
        {
            AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
            AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
                config,
                "composition.user@example.com",
                "tenant-composition",
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
                            AzureAuthInstallation.Available(
                                "/usr/lib/azureauth/azureauth",
                                "/usr/lib/azureauth/azureauth",
                                "0.9.5",
                                AzureAuthHostPlatform.NativeLinux
                            )
                        ),
                        ProcessRunner = processRunner,
                    }
                );
            CredentialRequestV2 request = CreateRequest() with
            {
                AccountHint = "composition.user@example.com",
                TenantHint = "tenant-composition",
                CredentialKind = CredentialKind.BasicPassword,
                IdentityFlow = IdentityFlow.DeviceCode,
                InteractivePolicy = InteractivePolicy.HostToolAllows,
                AcquisitionMode = AcquisitionMode.InteractionAllowed,
            };

            Assert.True(CredentialRequestV2Policy.IsValid(request));

            CredentialResult result = await root.AcquisitionService.AcquireAsync(
                request,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(CredentialResultStatus.ProtocolViolation, result.Status);
            Assert.NotEqual(CredentialResultStatus.InteractionBlocked, result.Status);
            Assert.Equal(CredentialErrorKind.ProtocolViolation, result.Error?.Kind);
            Assert.Equal("AzureAuthDeviceCodeUnsupported", result.Error?.Code);
            Assert.False(result.ContainsCredentialMaterial);
            Assert.Null(root.ProductionOptions.DeviceCodePromptWriter);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private sealed class CompositionRecordingRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> queuedResults = new(results);

        public List<ProcessStartSpec> StartSpecs { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartSpecs.Add(startSpec);
            return Task.FromResult(queuedResults.Dequeue());
        }
    }

    [Theory]
    [InlineData(
        InteractivePolicy.UserAllowed,
        CredentialResultStatus.InteractionBlocked,
        CredentialErrorKind.InteractionBlocked,
        "AzureAuthDeviceCodePromptUnavailable",
        "Native Linux device-code login requires an attached human prompt stream."
    )]
    [InlineData(
        InteractivePolicy.HostToolAllows,
        CredentialResultStatus.ProtocolViolation,
        CredentialErrorKind.ProtocolViolation,
        "AzureAuthDeviceCodeUnsupported",
        "AzureAuth device-code login requires an explicit interactive native Linux request."
    )]
    public async Task DeviceCodeCompositionRejectionsPreserveExactSafeMessages(
        InteractivePolicy interactivePolicy,
        CredentialResultStatus expectedStatus,
        CredentialErrorKind expectedKind,
        string expectedCode,
        string expectedSafeMessage
    )
    {
        string rootPath = CreateTestDirectory();
        var processRunner = new CountingProcessRunner();
        try
        {
            AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
            AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
                config,
                "composition.user@example.com",
                "tenant-composition",
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
                            AzureAuthInstallation.Available(
                                "/usr/lib/azureauth/azureauth",
                                "/usr/lib/azureauth/azureauth",
                                "0.9.5",
                                AzureAuthHostPlatform.NativeLinux
                            )
                        ),
                        ProcessRunner = processRunner,
                    }
                );
            CredentialRequestV2 request = CreateRequest() with
            {
                AccountHint = "composition.user@example.com",
                TenantHint = "tenant-composition",
                CredentialKind = CredentialKind.BasicPassword,
                IdentityFlow = IdentityFlow.DeviceCode,
                InteractivePolicy = interactivePolicy,
                AcquisitionMode = AcquisitionMode.InteractionAllowed,
            };

            Assert.True(CredentialRequestV2Policy.IsValid(request));

            CredentialResult result = await root.AcquisitionService.AcquireAsync(
                request,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(expectedStatus, result.Status);
            CredentialError error = Assert.IsType<CredentialError>(result.Error);
            Assert.Equal(expectedKind, error.Kind);
            Assert.Equal(expectedCode, error.Code);
            Assert.Equal(expectedSafeMessage, error.SafeMessage);
            Assert.False(result.ContainsCredentialMaterial);
            Assert.Null(root.ProductionOptions.DeviceCodePromptWriter);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }
}
