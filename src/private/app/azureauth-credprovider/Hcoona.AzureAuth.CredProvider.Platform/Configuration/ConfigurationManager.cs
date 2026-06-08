using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

public interface IConfigurationManager : IConfigurationChangePlanSink
{
    ConfigurationPlanValidationResult ValidatePlan(ConfigurationChangePlan plan);

    ValueTask<ConfigurationPlanResult> DryRunAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    );

    ValueTask<ConfigurationPlanResult> ApplyAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    );

    ValueTask<ConfigurationPlanResult> RemoveAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    );
}

public interface IConfigurationChangePlanSink
{
    ValueTask<ConfigurationPlanValidationResult> AcceptPlanAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    );
}

public interface IConfigurationChangePlanFactory<in TRequest>
{
    ConfigurationChangePlan CreatePlan(TRequest request);
}

public sealed class ConfigurationManager : IConfigurationManager
{
    public ConfigurationPlanValidationResult ValidatePlan(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string? violation = ConfigurationChangePlanPolicy.GetViolation(plan);
        return new ConfigurationPlanValidationResult
        {
            Plan = plan,
            IsValid = violation is null,
            Violation = violation,
        };
    }

    public ValueTask<ConfigurationPlanValidationResult> AcceptPlanAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ValidatePlan(plan));
    }

    public ValueTask<ConfigurationPlanResult> DryRunAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    ) => ValidateAndDefer(plan, ConfigurationPlanOperation.DryRun, cancellationToken);

    public ValueTask<ConfigurationPlanResult> ApplyAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    ) => ValidateAndDefer(plan, ConfigurationPlanOperation.Apply, cancellationToken);

    public ValueTask<ConfigurationPlanResult> RemoveAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    ) => ValidateAndDefer(plan, ConfigurationPlanOperation.Remove, cancellationToken);

    private static ValueTask<ConfigurationPlanResult> ValidateAndDefer(
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfigurationChangePlanPolicy.EnsureValid(plan);
        return ValueTask.FromException<ConfigurationPlanResult>(
            new NotImplementedException(
                $"{operation} execution is owned by later Phase 4 "
                    + "configuration-manager implementation."
            )
        );
    }
}

public sealed record ConfigurationPlanValidationResult
{
    public required ConfigurationChangePlan Plan { get; init; }
    public required bool IsValid { get; init; }
    public string? Violation { get; init; }
}

public sealed record ConfigurationPlanResult
{
    public required ConfigurationChangePlan Plan { get; init; }
    public required ConfigurationPlanOperation Operation { get; init; }
    public required ConfigurationPlanState State { get; init; }
    public IReadOnlyList<ConfigurationChange> Changes { get; init; } =
        Array.Empty<ConfigurationChange>();
}

public enum ConfigurationPlanOperation
{
    DryRun,
    Apply,
    Remove,
}
