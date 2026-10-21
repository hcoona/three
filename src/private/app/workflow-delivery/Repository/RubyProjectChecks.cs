using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal static class RubyProjectChecks
{
    internal const string Preset = "ruby/rspec-v1";
    internal const string RSpec = "ruby/project-rspec-v1";

    internal static CheckSpec[] Expand(QualitySelection? selection, string directory)
    {
        if (selection?.Preset != Preset)
            throw new InvalidDataException("Unresolved Ruby project quality preset.");
        return [Suite(directory)];
    }

    // The whole-project suite shared by every preset that adopts native RSpec.
    internal static CheckSpec Suite(string directory)
    {
        ImpactPlanner.ValidatePath(directory);
        return new(new(directory, RSpec, "default"), [], "ubuntu-latest", true, []);
    }
}
