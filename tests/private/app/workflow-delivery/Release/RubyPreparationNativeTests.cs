using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class RubyPreparationNativeTests(TestContext context)
{
    private const string Product =
        "tests/private/app/workflow-delivery/fixtures/products/hcoona-release-smoke-ruby";
    private const string Helper = "src/private/app/workflow-delivery/Native/Ruby/facts.rb";
    private const string JobInputs =
        "src/private/app/workflow-delivery/Release/ReleaseJobInputs.cs";
    private static readonly string[] CompositionInputs =
    [
        JobInputs,
        "src/private/app/workflow-delivery/Release/RubyReleaseCli.cs",
        "src/private/app/workflow-delivery/Release/RubyRelease.cs",
        "src/private/app/workflow-delivery/Program.cs",
        "src/private/app/workflow-delivery/Destinations/RubyDestinationObservation.cs",
    ];
    private static readonly string[] NativeInputs = [Product + "/version.json", Helper];
    private static readonly string[] Contents =
    [
        "LICENSE",
        "README.md",
        "lib/hcoona_release_smoke_ruby.rb",
        "lib/hcoona_release_smoke_ruby/version.rb",
    ];

    private static readonly string[] Checks =
    [
        "native-input",
        "build",
        "contents",
        "consumer",
        "original-bytes",
    ];
    private static readonly string[] RequirePaths = ["lib"];

    // One real Official public-release row proves the native journey. Channel, ref and
    // dry-run admission are app decisions covered by RubyPreparationTests substitutes.
    private const string Channel = "official";
    private const string Destination = "rubygems";
    private const string SelectedRef = "refs/heads/main";

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [Timeout(600000, CooperativeCancellation = true)]
    public async Task NativePreparationQualifiesCommittedOriginalAndInstalledOrigin()
    {
        string source = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(source, "global.json")))
            source =
                Path.GetDirectoryName(source)
                ?? throw new InvalidDataException("Missing source root.");
        string root = Directory.CreateTempSubdirectory("ruby-release-native-").FullName;
        string retained = Directory
            .CreateDirectory(
                Path.Combine(
                    context.TestResultsDirectory!,
                    "ruby-release-preparation-tests1-native",
                    Guid.NewGuid().ToString("N")
                )
            )
            .FullName;
        var fixtureCommands = new List<(NativeCommand Command, NativeCommandResult Result)>();
        var frozenInputs = CompositionInputs.ToDictionary(
            input => input,
            input => Hash(Path.Combine(source, input))
        );
        try
        {
            string checkout = Path.Combine(root, "source");
            string basis = (
                await RunAsync(new("git", source, ["rev-parse", "HEAD"], 30))
            ).Stdout.Trim();
            await RunAsync(
                new(
                    "git",
                    source,
                    ["clone", "--shared", "--no-checkout", "--", source, checkout],
                    120
                )
            );
            await RunAsync(
                new(
                    "git",
                    checkout,
                    ["checkout", "--detach", basis],
                    120,
                    new Dictionary<string, string?> { ["GIT_LFS_SKIP_SMUDGE"] = "1" }
                )
            );
            // The parent's uncommitted helper is an actual committed fixture input.
            foreach (string input in NativeInputs)
            {
                File.Copy(Path.Combine(source, input), Path.Combine(checkout, input), true);
                await RetainAsync(
                    Path.GetFileName(input),
                    await File.ReadAllTextAsync(
                        Path.Combine(checkout, input),
                        context.CancellationToken
                    )
                );
            }
            string hooks = Directory.CreateDirectory(Path.Combine(root, "empty-hooks")).FullName;
            await RunAsync(new("git", checkout, ["add", "--", .. NativeInputs], 30));
            string tree = (await RunAsync(new("git", checkout, ["write-tree"], 30))).Stdout.Trim();
            string revision = (
                await RunAsync(
                    new(
                        "git",
                        checkout,
                        [
                            "-c",
                            "user.name=Workflow fixture",
                            "-c",
                            "user.email=fixture@example.invalid",
                            "-c",
                            "commit.gpgSign=false",
                            "-c",
                            "core.hooksPath=" + hooks,
                            "commit-tree",
                            tree,
                            "-p",
                            basis,
                            "-m",
                            "Native Ruby preparation fixture",
                        ],
                        30
                    )
                )
            ).Stdout.Trim();
            await RunAsync(new("git", checkout, ["reset", "--hard", revision], 120));
            string scratch = Directory.CreateDirectory(Path.Combine(root, "preparation")).FullName;
            string ruby = await PreparedToolAsync("ruby");
            string gem = await PreparedToolAsync("gem");
            string dotnet = await PreparedToolAsync("dotnet");
            var subject = new ReleaseSubject(
                revision,
                SelectedRef,
                "hcoona-release-smoke-ruby",
                Product + "/workflow-delivery.release-unit.yml",
                Channel,
                Destination,
                false
            );
            var request = new RubyPreparationRequest(
                subject,
                checkout,
                scratch,
                new(ruby, gem, dotnet)
            );
            RubyPreparationResult result = await RubyPreparation.PrepareAsync(
                request,
                (command, token) =>
                {
                    Assert.DoesNotContain("push", command.Arguments);
                    Assert.DoesNotContain("fetch", command.Arguments);
                    if (command.Arguments[0] == gem && command.Arguments.Contains("install"))
                        Assert.Contains("--local", command.Arguments);
                    return NativeProcess.ExecuteAsync(command, token);
                },
                context.CancellationToken
            );
            await RetainAsync(
                "request.json",
                JsonSerializer.Serialize(request, TransferJson.Default.RubyPreparationRequest)
            );
            await RetainAsync(
                "preparation.json",
                JsonSerializer.Serialize(result, TransferJson.Default.RubyPreparationResult)
            );
            for (int index = 0; index < result.Commands.Length; index++)
            {
                await RetainAsync(
                    $"{index:D2}-{result.Commands[index].Check}.stdout.txt",
                    result.Commands[index].Result.Stdout
                );
                await RetainAsync(
                    $"{index:D2}-{result.Commands[index].Check}.stderr.txt",
                    result.Commands[index].Result.Stderr
                );
            }
            string built = Path.Combine(scratch, "package", "original.gem");
            if (File.Exists(built))
            {
                string retainedArchive = Path.Combine(retained, "original.gem");
                File.Copy(built, retainedArchive);
                context.AddResultFile(retainedArchive);
            }
            Assert.IsEmpty(
                result.Errors,
                JsonSerializer.Serialize(
                    new
                    {
                        result.Errors,
                        FailedCommands = result.Commands.Where(command =>
                            !command.Result.Succeeded
                        ),
                    }
                )
            );
            ReleaseCompletion.Validate(result.Prepared);
            Assert.AreEqual(subject, result.Prepared.Subject);
            CollectionAssert.AreEqual(Checks, result.Prepared.RequiredChecks);
            foreach (ReleaseCheck check in result.Prepared.Checks)
                Assert.AreEqual(ReleaseCheckStatus.Passed, check.Status, check.Id);
            Assert.HasCount(5, result.Prepared.Checks);
            Assert.AreEqual(revision, result.Native["commit"]);
            Assert.AreEqual(
                "true",
                result.Native["public-release"]
            );
            Assert.AreEqual("4.0.7", result.Native["ruby"]);
            Assert.IsFalse(result.Native["version"].Contains('+', StringComparison.Ordinal));
            ReleaseFile file = Assert.ContainsSingle(result.Prepared.Files);
            Assert.AreEqual("ruby-gem", file.Kind);
            Assert.AreEqual("ruby-gem", file.Build);
            Assert.AreEqual("gem", file.Output);
            Assert.AreEqual("hcoona-release-smoke-ruby", file.Name);
            Assert.AreEqual(result.Native["version"], file.Version);
            string original = Path.Combine(scratch, file.Path);
            RubySmokePackageOperations.RequireOriginal(new(original, file.Size, file.Sha256));

            NativeCommand factsCommand = Assert
                .ContainsSingle(result.Commands.Where(command => command.Check == "contents"))
                .Command;
            RubyArchiveFacts facts = RubyNativeFactsReader.ParseArchive(
                Assert
                    .ContainsSingle(result.Commands.Where(command => command.Check == "contents"))
                    .Result.Stdout,
                original
            );
            CollectionAssert.AreEquivalent(Contents, facts.Contents);
            Assert.IsEmpty(facts.Gem.RuntimeDependencies);
            Assert.IsNotNull(facts.Extensions);
            Assert.IsEmpty(facts.Extensions);
            Assert.AreEqual("https://github.com/hcoona/three", facts.Repository);
            CollectionAssert.AreEqual(RequirePaths, facts.Gem.RequirePaths);
            Assert.AreEqual(Path.Combine(checkout, Helper), factsCommand.Arguments[0]);
            foreach (ReleaseCommand command in result.Commands)
            {
                Assert.IsTrue(command.Result.Succeeded, command.Result.Stderr);
                Assert.IsFalse(command.Command.InheritEnvironment);
                Assert.IsTrue(command.Command.CloseStandardInput);
                Assert.AreEqual(SelectedRef, command.Command.Environment!["GITHUB_REF"]);
                Assert.AreEqual(revision, command.Command.Environment["GITHUB_SHA"]);
                foreach (
                    string excluded in new[]
                    {
                        "SOURCE_DATE_EPOCH",
                        "GITHUB_TOKEN",
                        "GH_TOKEN",
                        "GEM_HOST_API_KEY",
                        "HTTP_PROXY",
                        "HTTPS_PROXY",
                    }
                )
                    Assert.IsFalse(command.Command.Environment.ContainsKey(excluded), excluded);
                Assert.DoesNotContain("push", command.Command.Arguments);
                Assert.DoesNotContain("fetch", command.Command.Arguments);
            }
            NativeCommand install = Assert
                .ContainsSingle(
                    result.Commands.Where(command =>
                        command.Check == "consumer" && command.Command.Arguments.Contains("install")
                    )
                )
                .Command;
            CollectionAssert.AreEqual(
                new[] { gem, "install", "--norc", original, "--local", "--no-document" },
                install.Arguments
            );
            NativeCommand consumer = result.Commands[^1].Command;
            Assert.AreEqual("consumer", result.Commands[^1].Check);
            Assert.Contains("Gem.loaded_specs", consumer.Arguments[1]);
            Assert.Contains("loaded.length == 2", consumer.Arguments[1]);
            Assert.IsFalse(Directory.Exists(Path.Combine(consumer.Directory, ".git")));
            Assert.StartsWith(scratch, consumer.Environment!["GEM_HOME"]!);
            Assert.IsNull(consumer.Environment["RUBYLIB"]);
            Assert.IsNull(consumer.Environment["RUBYOPT"]);
            var nbgv = result
                .Commands.Where(command => command.Command.Arguments.Contains("get-version"))
                .ToArray();
            Assert.IsNotEmpty(nbgv);
            foreach (ReleaseCommand command in nbgv)
            {
                using JsonDocument projection = JsonDocument.Parse(command.Result.Stdout);
                Assert.AreEqual(
                    revision,
                    projection.RootElement.GetProperty("GitCommitId").GetString()
                );
                Assert.IsTrue(projection.RootElement.GetProperty("PublicRelease").GetBoolean());
            }

            // Controlled destination state follows complete actual native qualification.
            // This seam does not execute a remote source or establish live feed evidence.
            var fixtureInputs = Directory
                .EnumerateFiles(Path.Combine(checkout, Product), "*", SearchOption.AllDirectories)
                .Concat(new[]
                {
                    Path.Combine(checkout, Helper),
                    Path.Combine(checkout, "global.json"),
                    Path.Combine(checkout, ".config/dotnet-tools.json"),
                })
                .ToDictionary(path => Path.GetRelativePath(checkout, path), Hash);
            await RetainAsync("native-transfer-inputs.json", JsonSerializer.Serialize(new
            {
                Basis = basis,
                Tree = tree,
                Subject = subject,
                Tools = request.Tools,
                FixtureInputs = fixtureInputs,
                FrozenInputs = frozenInputs,
                JobInputsSha256 = frozenInputs[JobInputs],
                CompositionAssemblySha256 = Hash(typeof(ReleaseSubject).Assembly.Location),
                Original = file,
                ActualOriginalSha256 = Hash(original),
                EvidenceLimit = "Controlled destination state; no live source/feed proof.",
            }));
            foreach ((ReleaseFileState state, string expectedOutput) in new[]
            {
                (ReleaseFileState.Absent, "write-needed=true\n"),
                (ReleaseFileState.Exact, "write-needed=false\n"),
            })
            {
                string phase = Directory.CreateDirectory(Path.Combine(root, state.ToString()))
                    .FullName;
                string outputPath = Path.Combine(phase, "github-output.txt");
                string run = Guid.NewGuid().ToString("N");
                var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["PATH"] = string.Join(Path.PathSeparator,
                        Path.GetDirectoryName(ruby), "/usr/bin", "/bin"),
                    ["HOME"] = Directory.CreateDirectory(Path.Combine(phase, "home")).FullName,
                    ["LANG"] = "C.UTF-8",
                    ["LC_ALL"] = "C.UTF-8",
                    ["RUNNER_TEMP"] = phase,
                    ["GITHUB_SHA"] = subject.Revision,
                    ["GITHUB_REF"] = subject.SelectedRef,
                    ["DESTINATION"] = subject.Destination == "github-packages-ruby"
                        ? "github-packages" : subject.Destination,
                    ["DRY_RUN"] = subject.DryRun ? "true" : "false",
                    ["GITHUB_RUN_ID"] = run,
                    ["GITHUB_RUN_ATTEMPT"] = "1",
                    ["GITHUB_OUTPUT"] = outputPath,
                };
                // The observation job receives the actual retained preparation once.
                Directory.CreateDirectory(Path.Combine(phase, "prepared"));
                File.Copy(Path.Combine(retained, "preparation.json"),
                    Path.Combine(phase, "prepared", "preparation.json"));
                Assert.AreEqual(0, ReleaseJobInputs.Run(["release", "job-inputs", "ruby",
                    "receive", "observation"],
                    name => environment.GetValueOrDefault(name)));
                Assert.IsTrue(Directory.Exists(Path.Combine(phase, "observation")));
                var tools = new RubySourceTools(ruby,
                    Path.Combine(checkout,
                        "src/private/app/workflow-delivery/Native/Ruby/source.rb"),
                    Directory.CreateDirectory(Path.Combine(phase, "controlled-read")).FullName);
                string toolsPath = Path.Combine(phase, "source-tools.json");
                await File.WriteAllTextAsync(toolsPath,
                    JsonSerializer.Serialize(tools, TransferJson.Default.RubySourceTools),
                    context.CancellationToken);
                await RetainAsync($"{state}-source-tools.json", await
                    File.ReadAllTextAsync(toolsPath));
                RubySourceOriginal? observedOriginal = state == ReleaseFileState.Exact
                    ? new(original, file.Name, file.Version, facts.Gem.Platform, file.Size,
                        file.Sha256)
                    : null;
                var controlled = new RubyObservationResult(
                    new(subject, [new(file.Output, file.Name, file.Version, state,
                        observedOriginal?.Size, observedOriginal?.Sha256)],
                        ["native-source-original"],
                        [new("native-source-original", ReleaseCheckStatus.Passed)]),
                    new(state == ReleaseFileState.Exact ? RubySourceState.Present :
                        RubySourceState.Absent,
                        observedOriginal, null, []));
                using var cliOutput = new StringWriter();
                int calls = 0;
                int? exit = RubyReleaseCli.Run(
                    ["release", "observe-ruby", Path.Combine(retained, "preparation.json"),
                        scratch, toolsPath], cliOutput,
                    observe: (prepared, capturedTools, _, _) =>
                    {
                        calls++;
                        Assert.AreEqual(subject, prepared.Subject);
                        Assert.AreEqual(file, Assert.ContainsSingle(prepared.Files));
                        Assert.AreEqual(tools, capturedTools);
                        return Task.FromResult(controlled);
                    });
                string observationJson = cliOutput.ToString();
                await RetainAsync($"{state}-observation.json", observationJson);
                Assert.AreEqual(0, exit);
                Assert.AreEqual(1, calls);
                RubyObservationResult transfer = JsonSerializer.Deserialize(observationJson,
                    TransferJson.Default.RubyObservationResult)!;
                Assert.AreEqual(subject, transfer.Observation.Subject);
                Assert.AreEqual(controlled.Observation.Files[0],
                    Assert.ContainsSingle(transfer.Observation.Files));
                CollectionAssert.AreEqual(controlled.Observation.Checks,
                    transfer.Observation.Checks);
                CollectionAssert.AreEqual(controlled.Observation.RequiredChecks,
                    transfer.Observation.RequiredChecks);
                Assert.AreEqual(controlled.Read.State, transfer.Read.State);
                Assert.AreEqual(observedOriginal, transfer.Read.Original);
                Assert.IsNull(transfer.Read.Command);
                Assert.IsEmpty(transfer.Read.Errors);
                using (JsonDocument json = JsonDocument.Parse(observationJson))
                    Assert.AreEqual(state.ToString(), json.RootElement.GetProperty("observation")
                        .GetProperty("files")[0].GetProperty("state").GetString());
                string observationPath = Path.Combine(phase, "observation", "observation.json");
                await File.WriteAllTextAsync(observationPath, observationJson,
                    context.CancellationToken);
                Assert.AreEqual(0, ReleaseJobInputs.Run(["release", "job-inputs", "ruby", "state"],
                    name => environment.GetValueOrDefault(name)));
                string transferredOutput = await File.ReadAllTextAsync(outputPath);
                await RetainAsync($"{state}-github-output.txt", transferredOutput);
                Assert.AreEqual(expectedOutput, transferredOutput);
                Assert.AreEqual(Hash(Path.Combine(retained, $"{state}-observation.json")),
                    Hash(observationPath));
                RubySmokePackageOperations.RequireOriginal(new(original, file.Size, file.Sha256));
            }
            foreach ((string input, string digest) in frozenInputs)
                Assert.AreEqual(digest, Hash(Path.Combine(source, input)), input);
            Assert.AreEqual(
                "",
                (
                    await RunAsync(
                        new("git", checkout, ["status", "--porcelain", "--untracked-files=no"], 30)
                    )
                ).Stdout.Trim()
            );
        }
        finally
        {
            await RetainAsync("composition-freeze.json", JsonSerializer.Serialize(new
            {
                Before = frozenInputs,
                After = CompositionInputs.ToDictionary(input => input,
                    input => Hash(Path.Combine(source, input))),
            }));
            await RetainAsync(
                "fixture-commands.json",
                JsonSerializer.Serialize(
                    fixtureCommands.Select(receipt => new { receipt.Command, receipt.Result })
                )
            );
            foreach (
                string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            )
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(root, true);
        }

        static string Hash(string path) => Convert.ToHexStringLower(
            SHA256.HashData(File.ReadAllBytes(path)));

        async Task<string> PreparedToolAsync(string tool)
        {
            string path = (await RunAsync(new("mise", source, ["which", tool], 30))).Stdout.Trim();
            Assert.IsTrue(Path.IsPathFullyQualified(path), tool);
            Assert.IsTrue(File.Exists(path), path);
            return path;
        }

        async Task<NativeCommandResult> RunAsync(NativeCommand command)
        {
            NativeCommandResult result = await NativeProcess.ExecuteAsync(
                command,
                context.CancellationToken
            );
            fixtureCommands.Add((command, result));
            Assert.IsTrue(result.Succeeded, result.Stderr + result.Error);
            return result;
        }

        async Task RetainAsync(string name, string text)
        {
            string path = Path.Combine(retained, name);
            await File.WriteAllTextAsync(path, text, CancellationToken.None);
            context.AddResultFile(path);
        }
    }
}
