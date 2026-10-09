using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

// One check contract is shared by fact assembly and original-plan execution validation.
internal static class PythonPytestChecks
{
    internal const string Kind = "python/pytest-v1";

    internal static CheckSpec[] Expand(PythonPytestConfiguration configuration,
        PythonPytestTargetSet targets, PythonPytestOperation operation)
    {
        if (configuration.ConfigurationFile != targets.ConfigurationFile ||
            !configuration.TestPaths.SequenceEqual(targets.Targets.Select(target => target.Target)))
            throw new InvalidDataException("Conflicting native pytest target bindings.");
        string[] options = configuration.Options;
        if (options.Length != 0 &&
            (options.Length != 1 || options[0] != "--import-mode=importlib"))
            throw new InvalidDataException("Unsupported supplied native pytest option scope.");
        return targets.Targets.Select(target =>
        {
            var dimensions = new Dictionary<string, string>(operation.Dimensions,
                StringComparer.Ordinal);
            if (!dimensions.TryAdd("testPath", target.Target) ||
                !dimensions.TryAdd("configuration", targets.ConfigurationFile) ||
                dimensions.ContainsKey("importMode"))
                throw new InvalidDataException("Conflicting native pytest operation dimensions.");
            if (options.Length != 0) dimensions.Add("importMode", "importlib");
            var check = new CheckSpec(new("pytest:" + target.Target, Kind, operation.Variant),
                dimensions, operation.Runner, true, []);
            ImpactPlanner.ValidateCheck(check);
            return check;
        }).ToArray();
    }
}
