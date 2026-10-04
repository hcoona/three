using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal static class NodeQualityChecks
{
    internal static CheckSpec[] Expand(QualitySelection? selection, PnpmScripts project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (selection?.Preset != "node/hcoona-release-smoke-npm-v1")
            throw new InvalidDataException("Unresolved Node project quality preset.");
        string directory = project.Directory;
        if (string.IsNullOrWhiteSpace(directory) || directory.Contains('\\') ||
            directory.Contains(':') || directory.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException(
                "Node project checks require a discovered repository-relative source directory.");

        return [Required("build", "node/project-build-v1"),
            Required("test", "node/project-test-v1")];

        CheckSpec Required(string script, string check)
        {
            if (!project.Scripts.ContainsKey(script))
                throw new InvalidDataException(
                    $"Required Node script '{script}' is absent from {directory}.");
            // PNPM retains the script body and its execution semantics.
            return new(new(directory, check, "default"), [], "ubuntu-latest", true, []);
        }
    }
}
