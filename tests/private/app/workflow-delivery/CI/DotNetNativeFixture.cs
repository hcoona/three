using System.Text.Json;
using WorkflowDelivery.Platform;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

// Disposable native projects, settings and restore state for this fixed recipe.
internal sealed class DotNetNativeFixture(GitFixture repo, string candidate) : IDisposable
{
    internal GitFixture Repo { get; } = repo;
    internal string Candidate { get; } = candidate;
    internal string Root => Repo.Directory;
    internal string Scratch { get; } =
        Directory.CreateTempSubdirectory("workflow-native-check-").FullName;
    internal string RunScratch => Directory.CreateDirectory(Path.Combine(Scratch, "run")).FullName;
    internal const string Project = "tests/NativeTests.csproj";

    internal static async Task<DotNetNativeFixture> CreateAsync(string capability,
        CancellationToken token)
    {
        GitFixture repo = await GitFixture.CreateAsync(token);
        try
        {
            var files = new Dictionary<string, string>
            {
                ["global.json"] = """
                    {"sdk":{"version":"10.0.401","rollForward":"disable"},
                     "msbuild-sdks":{"MSTest.Sdk":"4.3.3"}}
                    """,
                ["nuget.config"] = """
                    <configuration><packageSources><clear />
                      <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                    </packageSources></configuration>
                    """,
                ["Directory.Build.props"] = """
                    <Project><PropertyGroup>
                      <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
                      <NuGetAudit>false</NuGetAudit>
                      <ImplicitUsings>enable</ImplicitUsings>
                      <BaseOutputPath>$(MSBuildProjectDirectory)/native-bin/</BaseOutputPath>
                    </PropertyGroup></Project>
                    """,
                ["Directory.Build.targets"] = """
                    <Project>
                      <Target Name="SetNativeBuildState" AfterTargets="Build">
                        <PropertyGroup><FixtureBuildState>ready</FixtureBuildState></PropertyGroup>
                        <ItemGroup><TestingPlatformEnvironmentVariable
                          Include="WORKFLOW_NATIVE_BUILD_STATE" Value="build-in-this-process" />
                        </ItemGroup>
                      </Target>
                      <Target Name="RequireNativeBuildState"
                        BeforeTargets="InvokeTestingPlatform;VSTest">
                        <Error Condition="'$(FixtureBuildState)' != 'ready'"
                          Text="Native test requires Build in this process." />
                      </Target>
                    </Project>
                    """,
                ["analyzer/Generator.csproj"] = """
                    <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
                      <TargetFramework>netstandard2.0</TargetFramework>
                      <LangVersion>latest</LangVersion>
                      <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
                    </PropertyGroup><ItemGroup>
                      <PackageReference Include="Microsoft.CodeAnalysis.CSharp"
                        Version="4.14.0" PrivateAssets="all" />
                    </ItemGroup></Project>
                    """,
                ["analyzer/Generator.cs"] = """
                    using Microsoft.CodeAnalysis;
                    [Generator]
                    public sealed class ValueGenerator : IIncrementalGenerator
                    {
                        public void Initialize(IncrementalGeneratorInitializationContext context)
                            => context.RegisterPostInitializationOutput(output => output.AddSource(
                                "Value.g.cs", "public static class GeneratedValue " +
                                    "{ public const int Answer = 7; }"));
                    }
                    """,
                ["library/Dependency.csproj"] = """
                    <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
                      <TargetFramework>net8.0</TargetFramework>
                    </PropertyGroup><ItemGroup>
                      <ProjectReference Include="../analyzer/Generator.csproj"
                        OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
                    </ItemGroup></Project>
                    """,
                ["library/Value.cs"] = "public static class DependencyValue { " +
                    "public static int Answer => 35 + GeneratedValue.Answer; }",
                ["tests/NativeTests.csproj"] = TestProject(capability),
                ["tests/NativeTests.cs"] = """
                    using Microsoft.VisualStudio.TestTools.UnitTesting;
                    [assembly: DoNotParallelize]
                    [TestClass]
                    public sealed class NativeFixtureTests(TestContext context)
                    {
                        [TestMethod]
                        public void SelectedPasses()
                        {
                            Assert.AreEqual(42, DependencyValue.Answer);
                            Assert.AreEqual("native-environment",
                                Environment.GetEnvironmentVariable("WORKFLOW_NATIVE_PROBE"));
                            Assert.AreEqual("from native settings", context.Properties["Probe"]);
                            if (context.Properties["ExpectedCapability"]?.ToString() == "MTP")
                            {
                                Assert.AreEqual("true",
                                    Environment.GetEnvironmentVariable("WORKFLOW_NATIVE_MTP"));
                                Assert.AreEqual("build-in-this-process",
                                    Environment.GetEnvironmentVariable(
                                        "WORKFLOW_NATIVE_BUILD_STATE"));
                            }
                        }
                        [TestMethod]
                        public void ShouldNeverRun() =>
                            Assert.Fail("Native filter was not respected.");
                        [TestMethod]
                        [Ignore("Native intentional skip")]
                        public void SelectedSkipped() =>
                            Assert.Fail("An ignored test was executed.");
                        [TestMethod]
                        public async Task SlowProbe()
                        {
                            await File.WriteAllTextAsync(
                                Environment.GetEnvironmentVariable("WORKFLOW_NATIVE_SIGNAL")!,
                                "started");
                            await Task.Delay(TimeSpan.FromSeconds(30), context.CancellationToken);
                        }
                    }
                    """,
                ["tests/native.runsettings"] = $$"""
                    <RunSettings><RunConfiguration>
                      <TreatNoTestsAsError>true</TreatNoTestsAsError>
                      <EnvironmentVariables>
                        <WORKFLOW_NATIVE_PROBE>native-environment</WORKFLOW_NATIVE_PROBE>
                      </EnvironmentVariables>
                    </RunConfiguration><TestRunParameters>
                      <Parameter name="Probe" value="from native settings" />
                      <Parameter name="ExpectedCapability" value="{{capability}}" />
                    </TestRunParameters></RunSettings>
                    """,
            };
            foreach ((string path, string content) in files)
                await repo.SetAsync(path, content);
            string initial = await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", initial);
            NativeCommandResult seed = await NativeProcess.ExecuteAsync(new("dotnet",
                repo.Directory,
                ["restore", "tests/NativeTests.csproj", "--use-lock-file"], 60), token);
            RequireSuccess(seed, "Fixture restore seed");
            NativeCommandResult locked = await NativeProcess.ExecuteAsync(new("dotnet",
                repo.Directory,
                ["restore", "tests/NativeTests.csproj", "--locked-mode", "--force"], 60), token);
            RequireSuccess(locked, "Fixture locked restore");
            foreach (string path in new[] { "tests/packages.lock.json",
                         "library/packages.lock.json",
                         "analyzer/packages.lock.json" })
                await repo.SetAsync(path, await File.ReadAllTextAsync(Path.Combine(repo.Directory,
                    path), token));
            string candidate = await repo.CommitAsync(initial);
            await repo.GitAsync("reset", "--hard", candidate);
            return new(repo, candidate);
        }
        catch { repo.Dispose(); throw; }
    }

    internal static Dictionary<string, string> Globals(string framework = "net10.0",
        bool appHost = true)
        => new(StringComparer.Ordinal)
        {
            ["Configuration"] = "Debug",
            ["ContinuousIntegrationBuild"] = "true",
            ["RestoreLockedMode"] = "true",
            ["TargetFramework"] = framework,
            ["IsGraphBuild"] = "true",
            ["UseAppHost"] = appHost ? "true" : "false",
        };

    internal async Task<JsonDocument> QueryAsync(Dictionary<string, string> globals,
        string[] names, CancellationToken token)
    {
        string path = Path.Combine(Scratch, "query.json");
        NativeCommandResult query = await NativeProcess.ExecuteAsync(new("dotnet", Root,
            ["msbuild", Project, "-nologo", "-noAutoResponse",
                "-getProperty:" + string.Join(',', names),
                "-getResultOutputFile:" + path,
                .. WorkflowDelivery.Repository.DotNetChecks.Properties(globals)], 60), token);
        RequireSuccess(query, "Native property query");
        return JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
    }

    internal static void RequireSuccess(NativeCommandResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidDataException(operation + ": " + result.Termination + ", exit " +
                result.ExitCode + "\n" + result.Error + "\n" + result.Stdout + result.Stderr);
    }

    private static string TestProject(string capability)
    {
        string sdk = capability == "MTP" ? "MSTest.Sdk" : "Microsoft.NET.Sdk";
        string packages = capability == "MTP" ? """
            <ItemGroup>
              <PackageReference Include="Microsoft.Testing.Platform.MSBuild" Version="2.4.0" />
            </ItemGroup>
            """ : """
            <ItemGroup>
              <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
              <PackageReference Include="MSTest.TestAdapter" Version="4.3.3" />
              <PackageReference Include="MSTest.TestFramework" Version="4.3.3" />
            </ItemGroup>
            """;
        string results = "--results-directory " +
            "\"$(MSBuildProjectDirectory)/receipts/$(TargetFramework)\"";
        string report = "--report-trx --report-trx-filename native.trx " + results;
        string selected = "--filter FullyQualifiedName~Selected --settings " +
            "\"$(MSBuildProjectDirectory)/native.runsettings\" " + report;
        string noTests = "--filter FullyQualifiedName~DoesNotExist " +
            "--minimum-expected-tests 1 " + report;
        string slow = "--filter FullyQualifiedName~SlowProbe " + results;
        return $$"""
            <Project Sdk="{{sdk}}"><PropertyGroup>
              <TargetFrameworks>net10.0;net8.0</TargetFrameworks>
              <IsTestProject>true</IsTestProject>
              <EnableMSTestRunner>{{(capability == "MTP" ? "true" : "false")}}</EnableMSTestRunner>
              <UseAppHost>true</UseAppHost>
              <TestingPlatformShowTestsFailure>true</TestingPlatformShowTestsFailure>
              <TestingPlatformCaptureOutput
                Condition="'$(FixtureNoTests)' == 'true'">false</TestingPlatformCaptureOutput>
              <TestingPlatformCommandLineArguments
                >{{selected}}</TestingPlatformCommandLineArguments>
              <TestingPlatformCommandLineArguments Condition="'$(FixtureNoTests)' == 'true'"
                >{{noTests}}</TestingPlatformCommandLineArguments>
              <TestingPlatformCommandLineArguments Condition="'$(FixtureSlow)' == 'true'"
                >{{slow}}</TestingPlatformCommandLineArguments>
              <RunSettingsFilePath
                >$(MSBuildProjectDirectory)/native.runsettings</RunSettingsFilePath>
              <VSTestTestCaseFilter>FullyQualifiedName~Selected</VSTestTestCaseFilter>
              <VSTestTestCaseFilter Condition="'$(FixtureNoTests)' == 'true'"
                >FullyQualifiedName~DoesNotExist</VSTestTestCaseFilter>
              <VSTestLogger>trx%3BLogFileName=native.trx</VSTestLogger>
              <VSTestResultsDirectory
                >$(MSBuildProjectDirectory)/receipts/$(TargetFramework)</VSTestResultsDirectory>
            </PropertyGroup>{{packages}}<ItemGroup>
              <ProjectReference Include="../library/Dependency.csproj" />
              <TestingPlatformEnvironmentVariable Include="WORKFLOW_NATIVE_PROBE"
                Value="native-environment" />
              <TestingPlatformEnvironmentVariable Include="WORKFLOW_NATIVE_MTP" Value="true" />
              <TestingPlatformEnvironmentVariable Include="WORKFLOW_NATIVE_SIGNAL"
                Value="$(MSBuildProjectDirectory)/slow-started.txt" />
            </ItemGroup></Project>
            """;
    }

    public void Dispose()
    {
        Repo.Dispose();
        Directory.Delete(Scratch, true);
    }
}
