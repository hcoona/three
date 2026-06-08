using System.Reflection;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ConfigurationManagerTests
{
    [Fact]
    public void ValidatePlanBindsToFrozenConfigurationChangePlanContract()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateValidPlan();

        ConfigurationPlanValidationResult result = manager.ValidatePlan(plan);

        Assert.True(result.IsValid);
        Assert.Null(result.Violation);
        Assert.Same(plan, result.Plan);
        Assert.Equal(ContractVersions.ConfigurationChangePlanMajor, result.Plan.ContractMajor);
        Assert.True(ConfigurationChangePlanPolicy.IsValid(result.Plan));
    }

    [Fact]
    public void ValidatePlanReportsFrozenContractViolationsWithoutWritingConfiguration()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan invalidPlan = CreateValidPlan() with
        {
            Scope = ConfigurationScope.WorkspaceReadOnly,
        };

        ConfigurationPlanValidationResult result = manager.ValidatePlan(invalidPlan);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Violation);
        Assert.Contains(
            "workspace read-only",
            result.Violation,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidatePlanRejectsUnsupportedConfigurationChangePlanContractMajor(
        int contractMajor
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan invalidPlan = CreateValidPlan() with
        {
            ContractMajor = contractMajor,
        };

        ConfigurationPlanValidationResult result = manager.ValidatePlan(invalidPlan);

        Assert.NotEqual(ContractVersions.ConfigurationChangePlanMajor, contractMajor);
        Assert.False(result.IsValid);
        Assert.Same(invalidPlan, result.Plan);
        Assert.NotNull(result.Violation);
        Assert.Contains("contract major", result.Violation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptPlanAsyncIsAValidationOnlyAdapterBoundary()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateValidPlan();

        ConfigurationPlanValidationResult result = await manager.AcceptPlanAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsValid);
        Assert.Same(plan, result.Plan);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task AcceptPlanAsyncRejectsUnsupportedConfigurationChangePlanContractMajor(
        int contractMajor
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan invalidPlan = CreateValidPlan() with
        {
            ContractMajor = contractMajor,
        };

        ConfigurationPlanValidationResult result = await manager.AcceptPlanAsync(
            invalidPlan,
            TestContext.Current.CancellationToken
        );

        Assert.NotEqual(ContractVersions.ConfigurationChangePlanMajor, contractMajor);
        Assert.False(result.IsValid);
        Assert.Same(invalidPlan, result.Plan);
        Assert.NotNull(result.Violation);
        Assert.Contains("contract major", result.Violation, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public async Task ExecutionMethodsValidatePlansBeforeDeferredPhase4Engines(string methodName)
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan invalidPlan = CreateValidPlan() with
        {
            Scope = ConfigurationScope.WorkspaceReadOnly,
        };
        Func<ValueTask<ConfigurationPlanResult>> call = CreateExecutionCall(
            manager,
            methodName,
            invalidPlan
        );

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () => await call());

        Assert.Contains(
            "workspace read-only",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Theory]
    [InlineData(nameof(IConfigurationManager.DryRunAsync))]
    [InlineData(nameof(IConfigurationManager.ApplyAsync))]
    [InlineData(nameof(IConfigurationManager.RemoveAsync))]
    public async Task ExecutionMethodsRemainExplicitlyDeferredForLaterPhase4Groups(
        string methodName
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateValidPlan();
        Func<ValueTask<ConfigurationPlanResult>> call = CreateExecutionCall(
            manager,
            methodName,
            plan
        );

        var exception = await Assert.ThrowsAsync<NotImplementedException>(async () => await call());

        Assert.Contains("later Phase 4", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationManagerApiUsesDeclarativePlanContracts()
    {
        MethodInfo[] methods = typeof(IConfigurationManager)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(IConfigurationManager))
            .ToArray();

        Assert.Contains(
            methods,
            method =>
                method.Name == nameof(IConfigurationManager.ValidatePlan)
                && method.ReturnType == typeof(ConfigurationPlanValidationResult)
                && method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual([typeof(ConfigurationChangePlan)])
        );
        Assert.All(
            methods.Where(method => method.Name != nameof(IConfigurationManager.ValidatePlan)),
            method =>
            {
                Assert.Equal(typeof(ValueTask<ConfigurationPlanResult>), method.ReturnType);
                Assert.Contains(
                    method.GetParameters(),
                    parameter => parameter.ParameterType == typeof(ConfigurationChangePlan)
                );
            }
        );
    }

    [Fact]
    public void AdapterPlanSeamsDoNotExposeFileSystemMutationTypes()
    {
        Type[] adapterFacingSeams =
        [
            typeof(IConfigurationChangePlanFactory<>),
            typeof(IConfigurationChangePlanSink),
        ];

        Assert.All(
            adapterFacingSeams.SelectMany(GetPublicSignatureTypes),
            type =>
            {
                Assert.NotEqual(typeof(IFileSystem), type);
                Assert.False(
                    type.Namespace?.StartsWith(
                        "Hcoona.AzureAuth.CredProvider.Platform.FileSystem",
                        StringComparison.Ordinal
                    ) == true,
                    $"Adapter-facing configuration seam exposes filesystem type {type.FullName}."
                );
            }
        );
        Assert.Equal(
            typeof(ConfigurationChangePlan),
            typeof(IConfigurationChangePlanFactory<>).GetMethod(
                nameof(IConfigurationChangePlanFactory<object>.CreatePlan)
            )?.ReturnType
        );
    }

    private static ConfigurationChangePlan CreateValidPlan() =>
        ConfigurationChangePlanPolicy.Create(
            "plan-git-user-config",
            "changeset-git-user-config",
            "azureauth-credprovider",
            ConfigurationScope.User,
            CreateManifest(),
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = "global git config",
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    Value = "true",
                    RequiresOwnershipRecord = true,
                },
            ]
        );

    private static ConfigurationManifestMetadata CreateManifest() =>
        new()
        {
            ManifestId = "manifest-git-user-config",
            OwnerProductId = "azureauth-credprovider",
            EntrySelector = "git.credential.https://dev.azure.com.useHttpPath",
            ProductVersion = "0.0.0-test",
        };

    private static Func<ValueTask<ConfigurationPlanResult>> CreateExecutionCall(
        ConfigurationManager manager,
        string methodName,
        ConfigurationChangePlan plan
    ) =>
        methodName switch
        {
            nameof(IConfigurationManager.DryRunAsync) => () =>
                manager.DryRunAsync(plan, TestContext.Current.CancellationToken),
            nameof(IConfigurationManager.ApplyAsync) => () =>
                manager.ApplyAsync(plan, TestContext.Current.CancellationToken),
            nameof(IConfigurationManager.RemoveAsync) => () =>
                manager.RemoveAsync(plan, TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(methodName), methodName, null),
        };

    private static IEnumerable<Type> GetPublicSignatureTypes(Type type)
    {
        foreach (MethodInfo method in type.GetMethods())
        {
            yield return method.ReturnType;

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }
}
