using System.Reflection;
using System.Reflection.Emit;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AzurePipelines;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AuthPhase14VerticalSliceServiceTests
{
    [Fact]
    public void LoginInteractiveBrowserUsesAcceptedMvpFlowWithoutPersistentDerivedCredentials()
    {
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions
            {
                CredentialCoreService = new CredentialCoreService(
                    new DeterministicFakeIdentityProvider()
                ),
            }
        );

        AuthPhase14LoginResult result = service.Login(
            new AuthPhase14LoginRequest
            {
                IdentityFlow = IdentityFlow.InteractiveBrowser,
                AccountHint = "Alice@Example",
                TenantHint = "TenantA",
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CredentialResultStatus.Success, result.CredentialResult.Status);
        Assert.Equal("alice@example", result.CredentialResult.Account);
        Assert.Equal("tenanta", result.CredentialResult.Tenant);
        Assert.False(result.PersistentDerivedCredentialsStored);
        Assert.True(result.CredentialResult.ContainsCredentialMaterial);
    }

    [Fact]
    public void LoginPatCompatibilityIsDeferredWithoutMaterialization()
    {
        var service = new AuthPhase14VerticalSliceService();

        AuthPhase14LoginResult result = service.Login(
            new AuthPhase14LoginRequest
            {
                IdentityFlow = IdentityFlow.PatCompatibility,
                ExplicitPatMaterialProvided = true,
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CredentialResultStatus.FlowDeferred, result.CredentialResult.Status);
        Assert.Equal("PatCompatibilityDeferred", result.CredentialResult.Error?.Code);
        Assert.False(result.CredentialResult.ContainsCredentialMaterial);
    }

    [Fact]
    public async Task LoginCancellationStopsBoundedCredentialAcquisitionPromptly()
    {
        var acquisition = new BlockingCredentialAcquisitionService();
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions
            {
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(acquisition),
            }
        );
        using var cancellation = new CancellationTokenSource();
        Task<AuthPhase14LoginResult> login = Task.Run(() =>
            service.Login(
                new AuthPhase14LoginRequest { IdentityFlow = IdentityFlow.InteractiveBrowser },
                cancellation.Token
            )
        );
        await acquisition.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        cancellation.Cancel();
        AuthPhase14LoginResult result = await login.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("CredentialAcquisitionCanceled", result.CredentialResult.Error?.Code);
    }

    [Theory]
    [MemberData(nameof(MalformedPatRequests))]
    public void ExecuteCredentialRequestRejectsMalformedPatBeforeProviderOrCache(
        CredentialRequest request
    )
    {
        var provider = new CountingIdentityProvider();
        var cache = new CountingDerivedCredentialCache();
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions
            {
                CredentialCoreService = new CredentialCoreService(provider, null, cache),
            }
        );

        CredentialResult result = service.ExecuteCredentialRequest(request);

        Assert.Equal(CredentialResultStatus.ProtocolViolation, result.Status);
        Assert.Equal(CredentialErrorKind.ProtocolViolation, result.Error?.Kind);
        Assert.Equal("ProtocolViolation", result.Error?.Code);
        Assert.False(result.ContainsCredentialMaterial);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(0, cache.InvocationCount);
    }

    [Fact]
    public void ExecuteCredentialRequestDefersValidPatBeforeProviderOrCache()
    {
        var provider = new CountingIdentityProvider();
        var cache = new CountingDerivedCredentialCache();
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions
            {
                CredentialCoreService = new CredentialCoreService(provider, null, cache),
            }
        );

        CredentialResult result = service.ExecuteCredentialRequest(CreatePatRequest());

        Assert.Equal(CredentialResultStatus.FlowDeferred, result.Status);
        Assert.Equal("PatCompatibilityDeferred", result.Error?.Code);
        Assert.False(result.ContainsCredentialMaterial);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(0, cache.InvocationCount);
    }

    [Fact]
    public void LoginAzurePipelinesRequiresExplicitCiModeAndTokenEnvironment()
    {
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions { EnvironmentVariableReader = _ => null }
        );

        AuthPhase14LoginResult result = service.Login(
            new AuthPhase14LoginRequest
            {
                IdentityFlow = IdentityFlow.AzurePipelinesSystemAccessToken,
                ExplicitAzurePipelinesCiMode = true,
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CredentialResultStatus.CredentialUnavailable, result.CredentialResult.Status);
        Assert.Equal(
            "AzurePipelinesSystemAccessTokenUnavailable",
            result.CredentialResult.Error?.Code
        );
    }

    [Fact]
    public void LoginAzurePipelinesUsesNonPersistentCiPolicy()
    {
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions { EnvironmentVariableReader = _ => "token" }
        );

        AuthPhase14LoginResult result = service.Login(
            new AuthPhase14LoginRequest
            {
                IdentityFlow = IdentityFlow.AzurePipelinesSystemAccessToken,
                ExplicitAzurePipelinesCiMode = true,
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CredentialResultStatus.Success, result.CredentialResult.Status);
        Assert.Null(result.CredentialResult.Account);
        Assert.Null(result.CredentialResult.Tenant);
        Assert.Null(result.CredentialResult.CacheKey);
        Assert.Null(result.CredentialResult.ExpiresAt);
        Assert.Equal("token", result.CredentialResult.BearerToken);
        Assert.False(result.PersistentDerivedCredentialsStored);
    }

    [Fact]
    public void LoginInteractiveBrowserRequestsSupportedGitBasicPasswordForm()
    {
        var acquisition = new CapturingCredentialAcquisitionService();
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions
            {
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(acquisition),
            }
        );

        AuthPhase14LoginResult result = service.Login(
            new AuthPhase14LoginRequest { IdentityFlow = IdentityFlow.InteractiveBrowser },
            TestContext.Current.CancellationToken
        );

        CredentialRequestV2 request = Assert.IsType<CredentialRequestV2>(acquisition.Request);
        Assert.Equal(CredentialEcosystem.Git, request.Ecosystem);
        Assert.Equal(CredentialKind.BasicPassword, request.CredentialKind);
        Assert.Equal(IdentityFlow.InteractiveBrowser, request.IdentityFlow);
        Assert.Equal(AcquisitionMode.InteractionAllowed, request.AcquisitionMode);
        Assert.Equal(CredentialResultStatus.Success, result.CredentialResult.Status);
    }

    [Fact]
    public void ExecuteAzurePipelinesTranslatesToSilentOnlyV2AndUsesV2Overload()
    {
        const string token = "phase14-system-access-token";
        var environmentReads = new List<string>();
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions
            {
                EnvironmentVariableReader = name =>
                {
                    environmentReads.Add(name);
                    return token;
                },
            }
        );
        CredentialRequest legacyRequest = CreateAzurePipelinesRequest();
        MethodInfo executeMethod = typeof(AuthPhase14VerticalSliceService).GetMethod(
            nameof(AuthPhase14VerticalSliceService.ExecuteCredentialRequest),
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        MethodInfo translateMethod = typeof(AuthPhase14VerticalSliceService).GetMethod(
            "TranslateV1Request",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        var translatedRequest = Assert.IsType<CredentialRequestV2>(
            translateMethod.Invoke(null, [legacyRequest])
        );
        MethodInfo v1Handle = typeof(AzurePipelinesSystemAccessTokenService).GetMethod(
            nameof(AzurePipelinesSystemAccessTokenService.Handle),
            [typeof(CredentialRequest), typeof(string)]
        )!;
        MethodInfo v2Handle = typeof(AzurePipelinesSystemAccessTokenService).GetMethod(
            nameof(AzurePipelinesSystemAccessTokenService.Handle),
            [typeof(CredentialRequestV2), typeof(string)]
        )!;

        CredentialResult result = service.ExecuteCredentialRequest(legacyRequest);
        List<MethodBase> calledMethods = GetCalledMethods(executeMethod);

        Assert.Equal(AcquisitionMode.SilentOnly, translatedRequest.AcquisitionMode);
        Assert.Equal(InteractivePolicy.Never, translatedRequest.InteractivePolicy);
        Assert.Equal(IdentityFlow.AzurePipelinesSystemAccessToken, translatedRequest.IdentityFlow);
        Assert.Equal(CachePolicyMode.NonPersistentCi, translatedRequest.CachePolicy);
        Assert.Same(legacyRequest.CiContext, translatedRequest.CiContext);
        Assert.Contains(translateMethod, calledMethods);
        Assert.Contains(v2Handle, calledMethods);
        Assert.DoesNotContain(v1Handle, calledMethods);
        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal(token, result.BearerToken);
        Assert.Equal(
            [AuthPhase14VerticalSliceService.AzurePipelinesSystemAccessTokenVariable],
            environmentReads
        );
    }

    [Fact]
    public void LoginDeferredServiceIdentityFlowThrowsNotSupported()
    {
        var service = new AuthPhase14VerticalSliceService();

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            service.Login(
                new AuthPhase14LoginRequest { IdentityFlow = IdentityFlow.ManagedIdentity },
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("deferred for MVP", exception.Message);
    }

    public static TheoryData<CredentialRequest> MalformedPatRequests()
    {
        CredentialRequest request = CreatePatRequest();
        return new TheoryData<CredentialRequest>
        {
            request with
            {
                AccountHint = "account\u001B",
            },
            request with
            {
                TenantHint = "tenant\u009F",
            },
            request with
            {
                ContractMajor = ContractVersions.CredentialContractV2Major,
            },
            request with
            {
                Resource = null!,
            },
        };
    }

    private static CredentialRequest CreatePatRequest() =>
        new()
        {
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "phase14",
                new Uri("https://dev.azure.com/phase14")
            ),
            ServiceIdentity = "default",
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = CredentialKind.PatCompatibility,
            IdentityFlow = IdentityFlow.PatCompatibility,
            InteractivePolicy = InteractivePolicy.UserAllowed,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
        };

    private static CredentialRequest CreateAzurePipelinesRequest() =>
        new()
        {
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "phase14",
                new Uri("https://dev.azure.com/phase14")
            ),
            ServiceIdentity = "default",
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = CredentialKind.BearerToken,
            IdentityFlow = IdentityFlow.AzurePipelinesSystemAccessToken,
            InteractivePolicy = InteractivePolicy.Never,
            CachePolicy = CachePolicyMode.NonPersistentCi,
            CiContext = new CiContext
            {
                ExplicitCiMode = true,
                Provider = CiProviderNames.AzurePipelines,
                HasAzurePipelinesSystemAccessToken = true,
                AllowsPersistentWrites = false,
            },
        };

    private static List<MethodBase> GetCalledMethods(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        var calledMethods = new List<MethodBase>();
        int offset = 0;
        while (offset < il.Length)
        {
            OpCode opCode = ReadOpCode(il, ref offset);
            int operandOffset = offset;
            if (opCode.OperandType == OperandType.InlineMethod)
            {
                int metadataToken = BitConverter.ToInt32(il, operandOffset);
                MethodBase? calledMethod = method.Module.ResolveMethod(
                    metadataToken,
                    method.DeclaringType?.GetGenericArguments(),
                    method.GetGenericArguments()
                );
                if (calledMethod is not null)
                {
                    calledMethods.Add(calledMethod);
                }
            }

            offset += GetOperandSize(opCode.OperandType, il, operandOffset);
        }

        return calledMethods;
    }

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        byte first = il[offset++];
        if (first != 0xFE)
        {
            return SingleByteOpCodes[first];
        }

        return MultiByteOpCodes[il[offset++]];
    }

    private static int GetOperandSize(OperandType operandType, byte[] il, int operandOffset) =>
        operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget
            or OperandType.ShortInlineI
            or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget
            or OperandType.InlineField
            or OperandType.InlineI
            or OperandType.InlineMethod
            or OperandType.InlineSig
            or OperandType.InlineString
            or OperandType.InlineTok
            or OperandType.InlineType
            or OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, operandOffset) * 4),
            _ => throw new InvalidOperationException($"Unsupported IL operand: {operandType}."),
        };

    private static readonly OpCode[] SingleByteOpCodes = CreateOpCodeLookup(multibyte: false);

    private static readonly OpCode[] MultiByteOpCodes = CreateOpCodeLookup(multibyte: true);

    private static OpCode[] CreateOpCodeLookup(bool multibyte)
    {
        var lookup = new OpCode[256];
        foreach (
            FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        )
        {
            var opCode = (OpCode)field.GetValue(null)!;
            ushort value = unchecked((ushort)opCode.Value);
            if ((value > byte.MaxValue) == multibyte)
            {
                lookup[value & byte.MaxValue] = opCode;
            }
        }

        return lookup;
    }

    private sealed class CountingIdentityProvider : IIdentityProvider
    {
        public int InvocationCount { get; private set; }

        public IdentityMaterial GetIdentity(CredentialRequest request)
        {
            InvocationCount++;
            throw new InvalidOperationException("Identity provider must not execute.");
        }
    }

    private sealed class CountingDerivedCredentialCache : IDerivedCredentialCache
    {
        public int InvocationCount { get; private set; }

        public DerivedCredentialCacheAvailability GetPersistentAvailability(
            CredentialRequest request
        )
        {
            InvocationCount++;
            throw new InvalidOperationException("Credential cache must not execute.");
        }

        public DerivedCredentialCacheReadResult TryReadPersistent(
            CredentialRequest request,
            CacheKey cacheKey
        )
        {
            InvocationCount++;
            throw new InvalidOperationException("Credential cache must not execute.");
        }

        public DerivedCredentialCacheWriteResult TryWritePersistent(
            CredentialRequest request,
            CacheKey cacheKey,
            IdentityMaterial identity
        )
        {
            InvocationCount++;
            throw new InvalidOperationException("Credential cache must not execute.");
        }
    }

    private sealed class CapturingCredentialAcquisitionService : ICredentialAcquisitionService
    {
        public CredentialRequestV2? Request { get; private set; }

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            Request = request;
            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    Username = "AzureDevOps",
                    Password = "test-password",
                    DiagnosticsCorrelationId = "auth-request-capture",
                }
            );
        }
    }

    private sealed class BlockingCredentialAcquisitionService : ICredentialAcquisitionService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    [Fact]
    public void LoginWithDeviceCodeCreatesUserAllowedCredentialRequest()
    {
        var acquisition = new CapturingCredentialAcquisitionService();
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions
            {
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(acquisition),
            }
        );

        AuthPhase14LoginResult result = service.Login(
            new AuthPhase14LoginRequest
            {
                IdentityFlow = IdentityFlow.DeviceCode,
                AccountHint = " Device.User@Example ",
                TenantHint = " Tenant-Device ",
            },
            TestContext.Current.CancellationToken
        );

        CredentialRequestV2 request = Assert.IsType<CredentialRequestV2>(acquisition.Request);
        Assert.Equal(IdentityFlow.DeviceCode, request.IdentityFlow);
        Assert.Equal(AcquisitionMode.InteractionAllowed, request.AcquisitionMode);
        Assert.Equal(InteractivePolicy.UserAllowed, request.InteractivePolicy);
        Assert.Equal(CredentialKind.BasicPassword, request.CredentialKind);
        Assert.Equal(CachePolicyMode.ProductPersistentCacheDisabled, request.CachePolicy);
        Assert.Equal("Device.User@Example", request.AccountHint);
        Assert.Equal("Tenant-Device", request.TenantHint);
        Assert.Null(request.CiContext);
        Assert.Equal(CredentialResultStatus.Success, result.CredentialResult.Status);
        Assert.Equal(IdentityFlow.DeviceCode, result.IdentityFlow);
        Assert.False(result.PersistentDerivedCredentialsStored);
    }
}
