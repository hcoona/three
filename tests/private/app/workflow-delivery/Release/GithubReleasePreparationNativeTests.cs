using System.IO.Compression;
using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class GithubReleasePreparationNativeTests(TestContext context)
{
    // The real ImageOcclusionEditor recipe on Windows: the product's scripts, ISCC, the
    // silent install and the portable extraction. CI retains the originals as evidence.
    [TestMethod]
    [TestCategory("GithubReleaseNativePreparation")]
    [Timeout(4200000, CooperativeCancellation = true)]
    public async Task NativePreparationBuildsAndConsumesBothAssets()
    {
        CancellationToken token = context.CancellationToken;
        string source = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(source, "global.json")) ||
            !Directory.Exists(Path.Combine(source, GithubReleaseUnits.Product)))
            source = Path.GetDirectoryName(source) ??
                throw new InvalidDataException("Missing source root.");
        string root = Directory.CreateTempSubdirectory("github-release-native-").FullName;
        string evidence = Directory.CreateDirectory(Path.Combine(
            context.TestResultsDirectory!, "github-release-native")).FullName;
        try
        {
            string checkout = Path.Combine(root, "c");
            string scratch = Directory.CreateDirectory(Path.Combine(root, "s")).FullName;
            string revision = (await Git(source, "rev-parse", "HEAD")).Trim();
            await Git(source, "clone", "--shared", "--no-checkout", "--", source, checkout);
            await Git(checkout, "checkout", "--detach", revision);
            var subject = new ReleaseSubject(revision, "refs/heads/development",
                GithubReleaseUnits.ImageOcclusionEditor, GithubReleaseUnits.Declaration, "buddy",
                GithubReleaseUnits.Buddy, false);
            GithubReleasePreparationResult result = await GithubReleasePreparation.PrepareAsync(
                new(subject, checkout, scratch), token);
            await File.WriteAllTextAsync(Path.Combine(evidence, "preparation.json"),
                JsonSerializer.Serialize(result,
                    TransferJson.Default.GithubReleasePreparationResult), token);
            foreach (ReleaseFile file in result.Prepared.Files)
                File.Copy(Path.Combine(scratch, file.Path), Path.Combine(evidence, file.Name));

            Assert.IsEmpty(result.Errors, JsonSerializer.Serialize(result.Commands
                .Where(command => !command.Result.Succeeded).Select(command => new
                {
                    command.Check, command.Command.Executable, command.Command.Arguments,
                    command.Result.Termination, command.Result.ExitCode, command.Result.Error,
                    command.Result.Stdout, command.Result.Stderr,
                })));
            GithubRelease.Validate(result);
            GithubRelease.Originals(result, scratch);
            string version = result.Native["version"];
            Assert.AreEqual("buddy/image-occlusion-editor/v" + version, result.Native["tag"]);
            CollectionAssert.AreEquivalent(new[]
            {
                GithubReleaseUnits.Asset("inno-installer", version),
                GithubReleaseUnits.Asset("portable-zip", version),
            }, result.Prepared.Files.Select(file => file.Name).ToArray());
            using ZipArchive portable = ZipFile.OpenRead(Path.Combine(evidence,
                GithubReleaseUnits.Asset("portable-zip", version)));
            Assert.IsNotNull(portable.GetEntry(GithubReleaseUnits.Executable));
            Assert.IsNotNull(portable.GetEntry(GithubReleaseUnits.Editor));
            Assert.IsTrue(result.Commands.Any(command => command.Check == "installer-consumer"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        async Task<string> Git(string directory, params string[] arguments)
        {
            // Match the release caller's checkout, which does not fetch LFS content.
            NativeCommandResult outcome = await NativeProcess.ExecuteAsync(new("git", directory,
                arguments, 300,
                new Dictionary<string, string?> { ["GIT_LFS_SKIP_SMUDGE"] = "1" }), token);
            Assert.IsTrue(outcome.Succeeded, outcome.Stderr);
            return outcome.Stdout;
        }
    }
}
