using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetPreparationNativeTests(TestContext context)
{
    [TestMethod]
    [Timeout(600000, CooperativeCancellation = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeReleasePreparationUsesOriginalPackageAndCleanConsumer(bool dryRun)
    {
        string source = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(source, "global.json")) ||
            !Directory.Exists(Path.Combine(source, "src/private/app/workflow-delivery")))
            source = Path.GetDirectoryName(source) ?? throw new
                InvalidDataException("Missing source root.");
        string root = Directory.CreateTempSubdirectory("nuget-release-native-").FullName;
        try
        {
            string checkout = Path.Combine(root, "source");
            string revision = (await NativeProcess.RunAsync("git", source, ["rev-parse", "HEAD"],
                context.CancellationToken)).Trim();
            await NativeProcess.RunAsync("git", source,
                ["clone", "--shared", "--no-checkout", "--", source, checkout],
                    context.CancellationToken);
            NativeCommandResult clone = await NativeProcess.ExecuteAsync(new("git", checkout,
                ["checkout", "--detach", revision], 120, new Dictionary<string, string?>
                { ["GIT_LFS_SKIP_SMUDGE"] = "1" }), context.CancellationToken);
            Assert.IsTrue(clone.Succeeded, clone.Stderr);
            string scratch = Directory.CreateDirectory(Path.Combine(root, "preparation")).FullName;
            const string product = "src/public/lib/hcoona-release-smoke-github-packages";
            var subject = new ReleaseSubject(revision, "refs/heads/development",
                "hcoona-release-smoke-github-packages", product +
                    "/workflow-delivery.release-unit.yml",
                "buddy", "github-packages-nuget", dryRun);
            NuGetPreparationResult result = await NuGetPreparation.PrepareAsync(new(subject,
                checkout, scratch), context.CancellationToken);
            string retained = Directory.CreateDirectory(Path.Combine(context.TestResultsDirectory!,
                "nuget-release-native", Guid.NewGuid().ToString("N"))).FullName;
            string binding = Path.Combine(retained, "preparation.json");
            await File.WriteAllTextAsync(binding, JsonSerializer.Serialize(result),
                context.CancellationToken);
            context.AddResultFile(binding);
            context.WriteLine(await File.ReadAllTextAsync(binding, context.CancellationToken));
            Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors));
            ReleaseCompletion.Validate(result.Prepared);
            Assert.IsTrue(result.Prepared.Checks.All(check => check.Status ==
                ReleaseCheckStatus.Passed));
            ReleaseFile file = Assert.ContainsSingle(result.Prepared.Files);
            string original = Path.Combine(scratch, file.Path);
            string archive = Path.Combine(retained, Path.GetFileName(original));
            File.Copy(original, archive);
            context.AddResultFile(archive);
            Assert.AreEqual(file.Size, new FileInfo(archive).Length);
            Assert.AreEqual(file.Sha256, DotNetPackageArchive.Digest(archive));
            Assert.AreEqual("Hcoona.ReleaseSmoke.GithubPackages", file.Name);
            Assert.AreEqual(subject, result.Prepared.Subject);
            Assert.IsFalse(result.Native.ContainsKey("MSBuildProjectFullPath"));
            Assert.IsFalse(result.Native.Values.Any(value => value.Contains(checkout,
                StringComparison.Ordinal)));
            Assert.AreEqual(DotNetPackageConsumer.Marker, result.Commands[^1].Result.Stdout.Trim());
            Assert.IsTrue(result.Commands.All(command => !command.Command.InheritEnvironment));
            Assert.IsTrue(result.Commands.All(command => command.Command.Environment is not null &&
                !command.Command.Environment.ContainsKey("GITHUB_TOKEN") &&
                !command.Command.Environment.ContainsKey("GH_TOKEN")));
            foreach (ReleaseCommand command in result.Commands.Where(command =>
                command.Check is "build" or "pack" ||
                    command.Command.Arguments.Contains("-target:GetBuildVersion")))
            {
                Assert.Contains("-property:Configuration=Release", command.Command.Arguments);
                Assert.Contains("-property:TargetFramework=net10.0", command.Command.Arguments);
                Assert.Contains("-property:ContinuousIntegrationBuild=true",
                    command.Command.Arguments);
                Assert.IsFalse(command.Command.Arguments.Any(argument => argument.Contains("Frozen",
                    StringComparison.OrdinalIgnoreCase) || argument.Contains("Witness",
                        StringComparison.OrdinalIgnoreCase)
                    || argument.Contains("NBGV_CacheMode", StringComparison.OrdinalIgnoreCase)));
            }
            NativeCommandResult caching = await NativeProcess.ExecuteAsync(new("dotnet", checkout,
                ["msbuild", Path.Combine(checkout, product,
                    "hcoona-release-smoke-github-packages.csproj"),
                    "-nologo", "-noAutoResponse", "-property:Configuration=Release",
                    "-getProperty:NBGV_CacheMode"], 60), context.CancellationToken);
            Assert.IsTrue(caching.Succeeded, caching.Stderr);
            Assert.AreEqual("MSBuildTargetCaching", caching.Stdout.Trim());
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(root, "*",
                SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
