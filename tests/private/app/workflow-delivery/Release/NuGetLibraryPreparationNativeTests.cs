using System.IO.Compression;
using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

// The shared library recipe against each registered product at the committed revision:
// the outer multi-target pack, its per-framework contents and a clean gallery consumer.
[TestClass]
public sealed class NuGetLibraryPreparationNativeTests(TestContext context)
{
    public static IEnumerable<object[]> Libraries => NuGetReleaseUnits.All
        .Where(unit => unit.Library).Select(unit => new object[] { unit.Id });

    [TestMethod]
    [TestCategory("NuGetNativePreparation")]
    [Timeout(900000, CooperativeCancellation = true)]
    [DynamicData(nameof(Libraries))]
    public async Task NativeLibraryPreparationPacksEveryFrameworkForACleanConsumer(string id)
    {
        CancellationToken token = context.CancellationToken;
        NuGetReleaseUnit unit = NuGetReleaseUnits.Named(id);
        string source = RepositoryToolSelection.SourceRoot;
        string root = Directory.CreateTempSubdirectory("nuget-library-").FullName;
        try
        {
            string checkout = Path.Combine(root, "checkout");
            string scratch = Directory.CreateDirectory(Path.Combine(root, "scratch")).FullName;
            string revision = (await RunAsync(new("git", source, ["rev-parse", "HEAD"], 30)))
                .Stdout.Trim();
            await RunAsync(new("git", source,
                ["clone", "--shared", "--no-checkout", "--", source, checkout], 120));
            await RunAsync(new("git", checkout, ["checkout", "--detach", revision], 120,
                new Dictionary<string, string?> { ["GIT_LFS_SKIP_SMUDGE"] = "1" }));
            var subject = new ReleaseSubject(revision, "refs/heads/main", id, unit.Declaration,
                "official", "nugetorg", false);

            NuGetPreparationResult result = await NuGetPreparation.PrepareAsync(new(subject,
                checkout, scratch), token);

            Assert.IsEmpty(result.Errors, JsonSerializer.Serialize(new
            {
                result.Errors,
                Failed = result.Commands.Where(command => !command.Result.Succeeded).Select(
                    command => new
                    {
                        command.Check,
                        command.Command.Arguments,
                        command.Result.Stdout,
                        command.Result.Stderr
                    }),
            }));
            NuGetRelease.Validate(result);
            ReleaseFile file = Assert.ContainsSingle(result.Prepared.Files);
            Assert.AreEqual(unit.PackageId, file.Name);
            Assert.IsFalse(file.Version.Contains(".g", StringComparison.Ordinal), file.Version);
            string archive = Path.Combine(scratch, file.Path);
            Assert.AreEqual(file.Sha256, DotNetPackageArchive.Digest(archive));
            using ZipArchive zip = ZipFile.OpenRead(archive);
            string[] entries = zip.Entries.Select(entry => entry.FullName).ToArray();
            foreach (string framework in unit.Frameworks)
                Assert.Contains("lib/" + framework + "/" + unit.AssemblyName + ".dll", entries);
            Assert.IsFalse(entries.Any(entry => entry.StartsWith("analyzers/",
                StringComparison.Ordinal)), string.Join('\n', entries));
            Assert.AreEqual(unit.AssemblyName, result.Commands[^1].Result.Stdout.Trim());
            Assert.IsTrue(result.Commands.All(command => command.Command.Environment is not null
                && !command.Command.Environment.ContainsKey("GITHUB_TOKEN")));
        }
        finally
        {
            Directory.Delete(root, true);
        }

        async Task<NativeCommandResult> RunAsync(NativeCommand command)
        {
            NativeCommandResult result = await NativeProcess.ExecuteAsync(command, token);
            Assert.IsTrue(result.Succeeded, result.Stderr);
            return result;
        }
    }
}
