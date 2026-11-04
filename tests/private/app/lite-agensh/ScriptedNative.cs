using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

internal sealed class ScriptedNative
{
    private ScriptedNative(string directory) => DirectoryPath = directory;

    public string DirectoryPath { get; }

    public static Task<ScriptedNative> BuildAsync(
        GitScenario fixture,
        string mode,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        string directory = Path.Combine(fixture.Fixture.RootPath, "controlled-native");
        Directory.CreateDirectory(directory);
        ScriptedNative native = new(directory);
        native.SetMode(mode);
        fixture.Database.SaveConfiguration(
            fixture.Database.Configuration with
            {
                NativeExecutable = Path.Combine(directory, "never-launch-native.exe"),
                RunAiCreditAllowance = 120,
                CampaignAiCreditAllowance = 600,
                DurationSeconds = 120,
            }
        );
        return Task.FromResult(native);
    }

    public void SetMode(string mode) =>
        File.WriteAllText(Path.Combine(DirectoryPath, "mode.txt"), mode);

    public Coordinator CreateCoordinator(GitScenario fixture) =>
        new(
            fixture.Database,
            fixture.Collaboration,
            fixture.Operations,
            (database, _, credits) => new ControlledNativeFactory(database, credits, DirectoryPath)
        );

    public async Task PrepareAsync(Coordinator coordinator, CancellationToken token)
    {
        Scenario.Data(await coordinator.PreflightAsync(token));
        Scenario.Data(await coordinator.CalibrateAsync(token));
        Assert.IsEmpty(OrdinaryPrompts());
    }

    public HashSet<string> CaptureOperations() =>
        Directory
            .EnumerateFiles(DirectoryPath, "operation-*.json")
            .ToHashSet(StringComparer.Ordinal);

    public JsonObject[] Operations(IReadOnlySet<string>? before = null) =>
        Directory
            .EnumerateFiles(DirectoryPath, "operation-*.json")
            .Where(path => before is null || !before.Contains(path))
            .Select(ReadOperation)
            .ToArray();

    private static JsonObject ReadOperation(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        return JsonNode.Parse(stream)!.AsObject();
    }

    public string[] OrdinaryPrompts(IReadOnlySet<string>? before = null) =>
        Operations(before)
            .Where(message => message["operation"]?.GetValue<string>() == "prompt")
            .Select(message => message["value"]!.GetValue<string>())
            .ToArray();

    public async Task<string> BuildCrashHostAsync(GitScenario fixture, CancellationToken token)
    {
        string host = Path.Combine(DirectoryPath, "crash-host");
        Directory.CreateDirectory(host);
        File.WriteAllText(
            Path.Combine(host, "global.json"),
            """{"sdk":{"version":"10.0.403","rollForward":"disable"}}"""
        );
        File.WriteAllText(
            Path.Combine(host, "nuget.config"),
            """<configuration><packageSources><clear /></packageSources></configuration>"""
        );
        string source = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "private",
            "app",
            "lite-agensh",
            "ControlledNativeWorker.cs"
        );
        string app = typeof(RunDatabase).Assembly.Location;
        string sqlite = typeof(Microsoft.Data.Sqlite.SqliteConnection).Assembly.Location;
        File.WriteAllText(
            Path.Combine(host, "CrashHost.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <AssemblyName>LiteAgensh.Tests</AssemblyName>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <NuGetAudit>false</NuGetAudit>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="LiteAgensh">
                  <HintPath>{{SecurityElement.Escape(app)}}</HintPath>
                </Reference>
                <Reference Include="Microsoft.Data.Sqlite">
                  <HintPath>{{SecurityElement.Escape(sqlite)}}</HintPath>
                </Reference>
                <Reference Include="{{SecurityElement.Escape(Path.Combine(
                        Path.GetDirectoryName(app)!, "SQLitePCLRaw.*.dll"))}}" />
                <Compile Include="{{SecurityElement.Escape(source)}}" />
              </ItemGroup>
            </Project>
            """
        );
        File.WriteAllText(
            Path.Combine(host, "Program.cs"),
            """
            using LiteAgensh;
            using LiteAgensh.Tests;
            SQLitePCL.Batteries_V2.Init();
            RunDatabase db = new(args[0]);
            CollaborationOperations collaboration = new(db);
            GitOperations git = new(db, collaboration);
            Coordinator coordinator = new(db, collaboration, git,
                (database, _, credits) => new ControlledNativeFactory(database, credits, args[1]));
            Console.WriteLine((await coordinator.RunAsync("run")).ToJsonString());
            """
        );
        ProcessResult build = await new ProcessRunner(fixture.Database).RunAsync(
            Scenario.DotnetHost,
            ["build", "CrashHost.csproj", "--output", "bin", "--verbosity", "quiet", "--nologo"],
            host,
            TimeSpan.FromSeconds(60),
            token
        );
        Assert.AreEqual(0, build.ExitCode, build.StandardOutput + build.StandardError);
        string output = Path.Combine(host, "bin");
        foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(app)!))
        {
            if (Path.GetFileName(file).StartsWith("LiteAgensh.Tests", StringComparison.Ordinal))
            {
                continue;
            }

            File.Copy(file, Path.Combine(output, Path.GetFileName(file)), overwrite: true);
        }

        string runtimes = Path.Combine(Path.GetDirectoryName(app)!, "runtimes");
        if (Directory.Exists(runtimes))
        {
            foreach (
                string file in Directory.EnumerateFiles(runtimes, "*", SearchOption.AllDirectories)
            )
            {
                string destination = Path.Combine(
                    output,
                    Path.GetRelativePath(Path.GetDirectoryName(app)!, file)
                );
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }
        }

        string nativeSqlite = Path.Combine(
            runtimes,
            "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            "native",
            "e_sqlite3.dll"
        );
        File.Copy(nativeSqlite, Path.Combine(output, "e_sqlite3.dll"), overwrite: true);
        return Path.Combine(output, "LiteAgensh.Tests.dll");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dirs.proj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate owned test fixture source.");
    }
}
