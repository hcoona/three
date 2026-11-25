using System.Text;
using System.Text.Json;
using LiteAgensh.Tests.Support;
using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Configurations;
using Microsoft.Testing.Platform.Extensions.TestHost;
using Microsoft.Testing.Platform.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Workers = 2, Scope = ExecutionScope.ClassLevel)]

namespace LiteAgensh.Tests;

[TestClass]
public sealed class TestAssembly
{
    public static void AddExtensions(ITestApplicationBuilder builder, string[] arguments) =>
        builder.TestHost.AddTestHostApplicationLifetime(
            services => new ResultsLifetime(services.GetConfiguration())
        );

    private sealed class ResultsLifetime(IConfiguration configuration)
        : ITestHostApplicationLifetime
    {
        private string? original;
        private bool changed;

        public string Uid => "LiteAgensh.Tests.FixtureResults";
        public string Version => typeof(TestAssembly).Assembly.GetName().Version!.ToString();
        public string DisplayName => "LiteAgensh Fixture Results";
        public string Description => "Keep owned fixture evidence with the test host results.";

        public Task<bool> IsEnabledAsync() => Task.FromResult(true);

        public Task BeforeRunAsync(CancellationToken cancellationToken)
        {
            original = Environment.GetEnvironmentVariable("ThreeTestResultsDirectory");
            if (string.IsNullOrWhiteSpace(original))
            {
                Environment.SetEnvironmentVariable(
                    "ThreeTestResultsDirectory", configuration.GetTestResultDirectory()
                );
                changed = true;
            }
            return Task.CompletedTask;
        }

        public Task AfterRunAsync(int exitCode, CancellationToken cancellationToken)
        {
            if (changed)
            {
                Environment.SetEnvironmentVariable("ThreeTestResultsDirectory", original);
            }
            return Task.CompletedTask;
        }
    }

    private static readonly string[] EnvironmentNames =
    [
        "GIT_CONFIG_GLOBAL",
        "GIT_CONFIG_SYSTEM",
        "GIT_CONFIG_NOSYSTEM",
        "HOME",
        "USERPROFILE",
        "XDG_CONFIG_HOME",
        "GIT_CONFIG",
        "GIT_CONFIG_COUNT",
        "GIT_CONFIG_PARAMETERS",
        "GIT_DIR",
        "GIT_COMMON_DIR",
        "GIT_WORK_TREE",
        "GIT_INDEX_FILE",
        "GIT_OBJECT_DIRECTORY",
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_NAMESPACE",
        "GIT_CEILING_DIRECTORIES",
    ];
    private static Dictionary<string, string?> original = [];
    private static Dictionary<string, string?> isolated = [];
    private static ScratchDirectory? isolation;

    internal static string GlobalConfig => Owned.PathFor("global.gitconfig");
    internal static string SystemConfig => Owned.PathFor("system.gitconfig");
    internal static string PolicyFile => Owned.PathFor("home", ".copilot", "config.json");
    internal static string OwnedPathFor(params string[] parts) => Owned.PathFor(parts);
    private static ScratchDirectory Owned =>
        isolation ?? throw new InvalidOperationException("Assembly Git isolation is unavailable.");

    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        try
        {
            original = EnvironmentNames.ToDictionary(
                name => name,
                Environment.GetEnvironmentVariable,
                StringComparer.Ordinal
            );
            isolation = new();
            Directory.CreateDirectory(Owned.PathFor("home", ".copilot"));
            Directory.CreateDirectory(Owned.PathFor("xdg"));
            File.WriteAllText(
                GlobalConfig,
                "[user]\n\tname = Owned Global Author\n\temail = owned-global@localhost\n"
                    + "[core]\n\tautocrlf = false\n[commit]\n\tgpgSign = false\n",
                new UTF8Encoding(false)
            );
            File.WriteAllText(
                SystemConfig,
                "[liteFixture]\n\tmarker = owned-system\n",
                new UTF8Encoding(false)
            );
            File.WriteAllText(
                PolicyFile,
                "{\"fixture\":\"owned-user-policy-not-a-credential\"}\n",
                new UTF8Encoding(false)
            );
            foreach (string file in new[] { GlobalConfig, SystemConfig, PolicyFile })
            {
                File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
            }
            foreach (string name in EnvironmentNames)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", GlobalConfig);
            Environment.SetEnvironmentVariable("GIT_CONFIG_SYSTEM", SystemConfig);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
            Environment.SetEnvironmentVariable("HOME", Owned.PathFor("home"));
            Environment.SetEnvironmentVariable("USERPROFILE", Owned.PathFor("home"));
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Owned.PathFor("xdg"));
            isolated = EnvironmentNames.ToDictionary(
                name => name,
                Environment.GetEnvironmentVariable,
                StringComparer.Ordinal
            );
            context.WriteLine("ASSEMBLY_GIT_ENVIRONMENT_INITIALIZED " + isolated.Count);
        }
        catch (Exception primary)
        {
            try
            {
                Cleanup();
            }
            catch (Exception secondary)
            {
                primary.Data["AssemblyGitCleanupFailure"] = secondary;
            }
            throw;
        }
    }

    internal static void AssertEnvironmentUnchanged()
    {
        Assert.AreEqual(EnvironmentNames.Length, isolated.Count);
        foreach ((string name, string? value) in isolated)
        {
            Assert.IsTrue(
                string.Equals(
                    value,
                    Environment.GetEnvironmentVariable(name),
                    StringComparison.Ordinal
                ),
                "Inherited isolated environment changed: " + name
            );
        }
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        List<Exception> failures = [];
        foreach ((string name, string? value) in original)
        {
            try
            {
                Environment.SetEnvironmentVariable(name, value);
                Assert.IsTrue(
                    string.Equals(
                        value,
                        Environment.GetEnvironmentVariable(name),
                        StringComparison.Ordinal
                    ),
                    "Original process environment was not restored: " + name
                );
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        string? root = isolation?.Root;
        try
        {
            if (root is not null && Directory.Exists(root))
            {
                foreach (
                    string file in Directory.EnumerateFiles(
                        root,
                        "*",
                        SearchOption.AllDirectories
                    )
                )
                {
                    File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                }
                isolation!.Dispose();
            }
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        Console.WriteLine(
            "ASSEMBLY_GIT_ENVIRONMENT_CLEANUP "
                + JsonSerializer.Serialize(
                    new
                    {
                        capturedNames = original.Count,
                        originalEnvironmentRestored = original.All(pair =>
                            string.Equals(
                                pair.Value,
                                Environment.GetEnvironmentVariable(pair.Key),
                                StringComparison.Ordinal
                            )
                        ),
                        ownedRootAbsent = root is null || !Directory.Exists(root),
                        secondaryFailures = failures.Count,
                    }
                )
        );
        if (failures.Count != 0)
        {
            throw new AggregateException("Assembly Git isolation cleanup failed.", failures);
        }
    }
}
