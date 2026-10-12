using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class PythonPreparationNativeTests(TestContext context)
{
    private const string Product = "src/public/lib/hcoona-release-smoke-python";
    private static readonly string[] NativeInputs = [Product + "/version.json",
        "src/private/app/workflow-delivery/Native/Python/packages.py"];

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [Timeout(600000, CooperativeCancellation = true)]
    public async Task NativeBranchPreparationBuildsCompleteOriginalPairAndGitFreeConsumer()
    {
        string source = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(source, "global.json")))
            source = Path.GetDirectoryName(source) ??
                throw new InvalidDataException("Missing source root.");
        string root = Directory.CreateTempSubdirectory("python-release-native-").FullName;
        try
        {
            string checkout = Path.Combine(root, "source");
            string basis = (await NativeProcess.RunAsync("git", source, ["rev-parse", "HEAD"],
                context.CancellationToken)).Trim();
            await NativeProcess.RunAsync("git", source,
                ["clone", "--shared", "--no-checkout", "--", source, checkout],
                context.CancellationToken);
            NativeCommandResult materialized = await NativeProcess.ExecuteAsync(new("git",
                checkout, ["checkout", "--detach", basis], 120,
                new Dictionary<string, string?> { ["GIT_LFS_SKIP_SMUDGE"] = "1" }),
                context.CancellationToken);
            Assert.IsTrue(materialized.Succeeded, materialized.Stderr);
            // Bind native fixture inputs even before the author's first source commit.
            foreach (string input in NativeInputs)
                File.Copy(Path.Combine(source, input), Path.Combine(checkout, input), true);
            string hooks = Directory.CreateDirectory(Path.Combine(root, "empty-hooks")).FullName;
            await NativeProcess.RunAsync("git", checkout, ["add", "--", .. NativeInputs],
                context.CancellationToken);
            string tree = (await NativeProcess.RunAsync("git", checkout, ["write-tree"],
                context.CancellationToken)).Trim();
            NativeCommandResult committed = await NativeProcess.ExecuteAsync(new("git", checkout,
                ["-c", "user.name=Workflow fixture", "-c", "user.email=fixture@example.invalid",
                    "-c", "commit.gpgSign=false", "-c", "core.hooksPath=" + hooks,
                    "commit-tree", tree, "-p", basis, "-m", "Native Python preparation fixture"],
                30), context.CancellationToken);
            Assert.IsTrue(committed.Succeeded, committed.Stderr);
            string revision = committed.Stdout.Trim();
            await NativeProcess.RunAsync("git", checkout, ["reset", "--hard", revision],
                context.CancellationToken);
            string scratch = Directory.CreateDirectory(Path.Combine(root, "preparation")).FullName;
            string miseData = Environment.GetEnvironmentVariable("MISE_DATA_DIR") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "share", "mise");
            var subject = new ReleaseSubject(revision, "refs/heads/preparation-development",
                "hcoona-release-smoke-python", Product + "/workflow-delivery.release-unit.yml",
                "buddy", "testpypi", false);
            PythonPreparationResult result = await PythonPreparation.PrepareAsync(new(subject,
                checkout, scratch, miseData), context.CancellationToken);
            string retained = Directory.CreateDirectory(Path.Combine(context.TestResultsDirectory!,
                "python-release-native", Guid.NewGuid().ToString("N"))).FullName;
            string binding = Path.Combine(retained, "preparation.json");
            await File.WriteAllTextAsync(binding, JsonSerializer.Serialize(result),
                context.CancellationToken);
            context.AddResultFile(binding);
            Assert.IsEmpty(result.Errors, JsonSerializer.Serialize(new { result.Errors,
                FailedCommands = result.Commands.Where(command => !command.Result.Succeeded) }));
            ReleaseCompletion.Validate(result.Prepared);
            Assert.HasCount(2, result.Prepared.Files);
            Assert.IsTrue(result.Prepared.Checks.All(check =>
                check.Status == ReleaseCheckStatus.Passed));
            Assert.AreEqual("true", result.Native["public-release"]);
            Assert.AreEqual(revision, result.Native["commit"]);
            Assert.IsFalse(result.Native["version"].Contains('+', StringComparison.Ordinal));
            foreach (ReleaseFile file in result.Prepared.Files)
            {
                string path = Path.Combine(scratch, file.Path);
                PythonPackageOperations.RequireOriginal(
                    new(file.Kind, path, file.Size, file.Sha256));
                string archive = Path.Combine(retained, Path.GetFileName(path));
                File.Copy(path, archive);
                context.AddResultFile(archive);
                Assert.AreEqual(result.Native["version"], file.Version);
            }
            Assert.HasCount(2, result.Commands.Where(command =>
                command.Command.Arguments.Contains("consume")));
            Assert.IsTrue(result.Commands.All(command => command.Result.Succeeded &&
                !command.Command.InheritEnvironment));
            Assert.AreEqual("", (await NativeProcess.RunAsync("git", checkout,
                ["status", "--porcelain", "--untracked-files=no"],
                context.CancellationToken)).Trim());
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(
                root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
