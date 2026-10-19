using System.Text.Json;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Ecosystems;

// The concrete nbgv-python distribution. Hatchling owns its static version and build; UV owns
// frozen resolution of its runtime dependencies from the committed root lock.
internal sealed class PythonNbgvPackageOperations(PythonPackageTools tools, string root)
    : PythonSmokePackageOperations(tools, root)
{
    private const string Name = PythonReleaseUnits.Nbgv;

    internal override async Task<JsonDocument> VersionFactsAsync(string directory,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token) => JsonDocument.Parse((await execute(Helper(ToolInterpreter,
            this.tools.Checkout, ["static-version-facts",
                Path.Combine(this.tools.Checkout, directory)]), token)).Stdout);

    // Hash-checked locked runtime closure; the original itself is still installed alone.
    private protected override async Task InstallRuntimeAsync(string name, string consumer,
        string venv, Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        if (name != Name)
            throw new InvalidDataException("Unsupported Python Release distribution.");
        string locked = Path.Combine(consumer, "runtime-requirements.txt");
        await execute(Uv(this.tools.Checkout, ["export", "--frozen", "--package", Name,
            "--no-dev", "--no-emit-workspace", "--no-header", "--output-file", locked]), token);
        await execute(Uv(consumer, ["pip", "install", "--python", venv, "--require-hashes",
            "--no-deps", "-r", locked]), token);
    }

    // The adopted python/pytest-v1 suite of this member, as CI runs it, from the exact checkout.
    internal async Task TestReleaseAsync(string tests,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        string environment = Path.Combine(this.root, "test-environment");
        var synced = new Dictionary<string, string?>(this.environment, StringComparer.Ordinal)
        {
            ["UV_PROJECT_ENVIRONMENT"] = environment,
        };
        await execute(new(this.tools.Uv, this.tools.Checkout, ["sync", "--frozen", "--python",
            this.tools.Interpreter, "--package", "hcoona-three-monorepo", "--package", Name],
            this.tools.DeadlineSeconds, synced, this.tools.InheritEnvironment), token);
        var run = new Dictionary<string, string?>(this.environment, StringComparer.Ordinal)
        {
            ["UV_PROJECT_ENVIRONMENT"] = null,
            ["PYTHONSAFEPATH"] = null,
            ["PYTEST_ADDOPTS"] = null,
            ["PYTEST_PLUGINS"] = null,
            ["PYTEST_DISABLE_PLUGIN_AUTOLOAD"] = null,
        };
        await execute(new(this.tools.Uv, this.tools.Checkout, ["run", "--no-project", "--python",
            environment, "python", "-m", "pytest", "-c", "pyproject.toml",
            "--junitxml=" + Path.Combine(this.root, "tests.xml"), tests],
            this.tools.DeadlineSeconds, run, this.tools.InheritEnvironment), token);
    }
}
