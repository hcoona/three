using System.Text.Json;
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

    [TestMethod]
    [DataRow("buddy", "github-packages-ruby", "refs/heads/preparation-development", false)]
    [DataRow("official", "rubygems", "refs/heads/main", false)]
    [DataRow("official", "rubygems", "refs/tags/preparation-preview", true)]
    [OSCondition(OperatingSystems.Linux)]
    [Timeout(600000, CooperativeCancellation = true)]
    public async Task NativePreparationQualifiesCommittedOriginalAndInstalledOrigin(
        string channel,
        string destination,
        string selectedRef,
        bool dryRun
    )
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
                selectedRef,
                "hcoona-release-smoke-ruby",
                Product + "/workflow-delivery.release-unit.yml",
                channel,
                destination,
                dryRun
            );
            var request = new RubyPreparationRequest(
                subject,
                checkout,
                scratch,
                new(ruby, gem, dotnet)
            );
            RubyPreparationResult result = await RubyPreparation.PrepareAsync(
                request,
                async (command, token) =>
                {
                    Assert.DoesNotContain("push", command.Arguments);
                    Assert.DoesNotContain("fetch", command.Arguments);
                    if (command.Arguments[0] == gem && command.Arguments.Contains("install"))
                        Assert.Contains("--local", command.Arguments);
                    // Invalid user options must not enter native gem build/install or API facts.
                    string home = command.Environment!["HOME"]!;
                    await File.WriteAllTextAsync(
                        Path.Combine(home, ".gemrc"),
                        "gem: --definitely-invalid-preparation-option\n",
                        token
                    );
                    return await NativeProcess.ExecuteAsync(command, token);
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
                selectedRef == "refs/heads/main" ? "true" : "false",
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
            RubyPackageOperations.RequireOriginal(new(original, file.Size, file.Sha256));

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
                Assert.AreEqual(selectedRef, command.Command.Environment!["GITHUB_REF"]);
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
                Assert.AreEqual(
                    selectedRef == "refs/heads/main",
                    projection.RootElement.GetProperty("PublicRelease").GetBoolean()
                );
            }
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
