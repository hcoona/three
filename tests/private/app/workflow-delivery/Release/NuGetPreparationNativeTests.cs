using System.IO.Compression;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Build.Evaluation;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetPreparationNativeTests(TestContext context)
{
    [TestMethod]
    [TestCategory("NuGetNativePreparation")]
    [Timeout(600000, CooperativeCancellation = true)]
    // Preparation does not read DryRun; NuGetPreparationTests asserts its subject round-trip.
    [DataRow("refs/heads/development")]
    [DataRow("refs/heads/main")]
    public async Task NativeReleasePreparationUsesOriginalPackageAndCleanConsumer(
        string selectedRef)
    {
        using var fixture = new NativeFixture(context);
        Preparation preparation = await fixture.PrepareAsync(0, selectedRef: selectedRef);
        NuGetPreparationResult result = preparation.Result;
        string revision = result.Prepared.Subject.Revision;
        string checkout = preparation.Checkout;
        ReleaseSubject subject = result.Prepared.Subject;
        Assert.IsEmpty(result.Errors, JsonSerializer.Serialize(new
        {
            Errors = result.Errors, Revision = revision, SelectedRef = selectedRef,
            FailedCommands = result.Commands.Where(command => !command.Result.Succeeded)
                .Select(command => new
                {
                    command.Check, command.Command.Executable, command.Command.Arguments,
                    command.Command.DeadlineSeconds, command.Result.Termination,
                    command.Result.ExitCode, command.Result.DurationSeconds,
                    command.Result.Error, command.Result.Stdout, command.Result.Stderr,
                }),
        }));
        ReleaseCompletion.Validate(result.Prepared);
        Assert.IsTrue(result.Prepared.Checks.All(check => check.Status ==
            ReleaseCheckStatus.Passed));
        ReleaseFile file = Assert.ContainsSingle(result.Prepared.Files);
        string archive = preparation.Archive;
        Assert.AreEqual(file.Size, new FileInfo(archive).Length);
        Assert.AreEqual(file.Sha256, DotNetPackageArchive.Digest(archive));
        Assert.AreEqual("Hcoona.ReleaseSmoke.GithubPackages", file.Name);
        Assert.AreEqual(subject, result.Prepared.Subject);
        Assert.AreEqual(selectedRef == "refs/heads/main",
            !file.Version.Contains(".g", StringComparison.Ordinal));
        Assert.IsTrue(result.Commands.All(command =>
            command.Command.Environment!["GITHUB_REF"] == selectedRef &&
            command.Command.Environment["GITHUB_SHA"] == revision));
        Assert.IsFalse(result.Native.ContainsKey("MSBuildProjectFullPath"));
        Assert.IsFalse(result.Native.Values.Any(value => value.Contains(checkout,
            StringComparison.Ordinal)));
        Assert.AreEqual(DotNetPackageConsumer.Marker,
            result.Commands[^1].Result.Stdout.Trim());
        Assert.IsTrue(result.Commands.All(command => !command.Command.InheritEnvironment));
        Assert.IsTrue(result.Commands.All(command => command.Command.Environment is not
            null &&
            !command.Command.Environment.ContainsKey("GITHUB_TOKEN") &&
            !command.Command.Environment.ContainsKey("GH_TOKEN")));
        if (OperatingSystem.IsWindows())
            foreach (string name in new[] { "ProgramFiles", "ProgramFiles(x86)" })
            {
                if (Environment.GetEnvironmentVariable(name) is { } expected)
                    foreach (ReleaseCommand command in result.Commands)
                        Assert.AreEqual(expected, command.Command.Environment![name], name);
            }
        foreach (ReleaseCommand command in result.Commands.Where(command =>
            command.Check is "build" or "pack" ||
                command.Command.Arguments.Contains("-target:GetBuildVersion")))
        {
            Assert.Contains("-property:Configuration=Release", command.Command.Arguments);
            Assert.Contains("-property:TargetFramework=net10.0", command.Command.Arguments);
            Assert.Contains("-property:ContinuousIntegrationBuild=true",
                command.Command.Arguments);
            Assert.IsFalse(command.Command.Arguments.Any(argument =>
                argument.Contains("Frozen",
                StringComparison.OrdinalIgnoreCase) || argument.Contains("Witness",
                    StringComparison.OrdinalIgnoreCase)
                || argument.Contains("NBGV_CacheMode",
                    StringComparison.OrdinalIgnoreCase)));
        }
        Assert.IsTrue(preparation.ActualCommands.Where(command => command.Check != "consumer")
            .All(command => command.Command.Environment!["NUGET_PACKAGES"] ==
                preparation.Cache));
        Assert.IsTrue(preparation.ActualCommands.Where(command => command.Check == "consumer")
            .All(command => command.Command.Environment!["NUGET_PACKAGES"]!
                .StartsWith(preparation.Scratch + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)));
    }

    [TestMethod]
    [TestCategory("NuGetNativeScenarios")]
    [Timeout(600000, CooperativeCancellation = true)]
    public async Task NativeSourceSensitivityChangesOriginalPayload()
    {
        using var fixture = new NativeFixture(context);
        Preparation baseline = await fixture.PrepareAsync(0);
        Preparation changed = await fixture.PrepareAsync(1, changeSource: true);
        fixture.Attach("source-control-versions.json", new
        {
            Baseline = baseline.Result.Native,
            DerivedCommit = changed.Result.Prepared.Subject.Revision,
            DerivedNativeVersions = changed.Result.Native,
            EqualityClaim = "No cross-commit version or byte equality claim.",
        });
        Assert.IsFalse(HasSourceControl(baseline.Archive));
        Assert.IsTrue(HasSourceControl(changed.Archive));
        Assert.AreNotEqual(baseline.SourceSha256, changed.SourceSha256);
        Assert.AreNotEqual(baseline.Result.Prepared.Subject.Revision,
            changed.Result.Prepared.Subject.Revision);
        Assert.AreNotEqual(baseline.Result.Prepared.Files[0].Sha256,
            changed.Result.Prepared.Files[0].Sha256);
        Assert.IsFalse(Payload(baseline.Archive, ".dll").SequenceEqual(
            Payload(changed.Archive, ".dll")), "Consumed source must change the original DLL.");
        Assert.IsFalse(File.ReadAllBytes(baseline.Pdb)
            .SequenceEqual(File.ReadAllBytes(changed.Pdb)),
            "The portable PDB must record the changed consumed source.");
    }

    [TestMethod]
    [TestCategory("NuGetNativeScenarios")]
    [Timeout(600000, CooperativeCancellation = true)]
    public async Task NativeTimestampOverrideChangesOnlyArchiveMetadata()
    {
        using var fixture = new NativeFixture(context);
        Preparation baseline = await fixture.PrepareAsync(0);
        Preparation changed = await fixture.PrepareAsync(1, timestamp: "315532804");
        Assert.AreEqual(baseline.Result.Prepared.Subject, changed.Result.Prepared.Subject);
        Assert.AreEqual(baseline.SourceSha256, changed.SourceSha256);
        AssertNativeEqual(baseline.Result.Native, changed.Result.Native);
        Assert.AreNotEqual(baseline.Result.Prepared.Files[0].Sha256,
            changed.Result.Prepared.Files[0].Sha256);
        using var first = ZipFile.OpenRead(baseline.Archive);
        using var second = ZipFile.OpenRead(changed.Archive);
        CollectionAssert.AreEqual(first.Entries.Select(entry => entry.FullName).ToArray(),
            second.Entries.Select(entry => entry.FullName).ToArray());
        foreach (ZipArchiveEntry entry in first.Entries)
        {
            ZipArchiveEntry other = second.GetEntry(entry.FullName)!;
            CollectionAssert.AreEqual(Read(entry), Read(other), entry.FullName);
            Assert.AreEqual(new DateTime(1980, 1, 1, 0, 0, 0), entry.LastWriteTime.DateTime);
            Assert.AreEqual(new DateTime(1980, 1, 1, 0, 0, 4), other.LastWriteTime.DateTime);
        }
        CollectionAssert.AreEqual(File.ReadAllBytes(baseline.Pdb), File.ReadAllBytes(changed.Pdb));
        CollectionAssert.AreEqual(baseline.Paths.Documents, changed.Paths.Documents);
        CollectionAssert.AreEqual(baseline.Paths.CodeView, changed.Paths.CodeView);
    }

    [TestMethod]
    [TestCategory("NuGetNativeScenarios")]
    [Timeout(600000, CooperativeCancellation = true)]
    public async Task NativeFreshRecoveryUsesIndependentOriginals()
    {
        using var fixture = new NativeFixture(context);
        Preparation first = await fixture.PrepareAsync(0);
        byte[] firstBytes = File.ReadAllBytes(first.Archive);
        using var absentFeed = new Feed(first.Result.Prepared.Files[0].Version, null);
        using var absentClient = new HttpClient(absentFeed);
        GithubNuGetObservationResult absent = await new GithubNuGetObservation(absentClient)
            .ObserveAsync(first.Result.Prepared, "controlled-read", context.CancellationToken);
        Assert.AreEqual(ReleaseFileState.Absent, absent.Observation.Files[0].State);
        GithubNuGetCompletionResult missing = NuGetRelease.Complete(first.Result.Prepared,
            absent, absent, null);
        Assert.AreEqual(ReleaseBusinessState.Unknown, missing.Outcome.State);
        Assert.IsNull(missing.MutationAttempted);
        Assert.IsNull(missing.Invocation);
        string originalResult = fixture.Attach("original-missing-result.json", missing);
        byte[] originalResultBytes = File.ReadAllBytes(originalResult);

        Preparation fresh = await fixture.PrepareAsync(1);
        AssertEquivalent(first, fresh);
        Assert.AreNotEqual(first.Scratch, fresh.Scratch);
        Assert.AreNotEqual(first.Cache, fresh.Cache);
        // The same fresh originals meet an exact and a conflicting served archive. Each
        // controlled outcome is checked independently so one cannot hide the other.
        var failures = new List<string>();
        foreach (bool conflict in new[] { false, true })
        {
            string label = conflict ? "conflict" : "exact";
            try
            {
                await RecoverAsync(conflict, label);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(label + ": " + exception);
            }
        }
        Assert.IsEmpty(failures, string.Join("\n", failures));

        async Task RecoverAsync(bool conflict, string label)
        {
            using var feed = new Feed(fresh.Result.Prepared.Files[0].Version,
                conflict ? [.. firstBytes, 0] : firstBytes);
            using var client = new HttpClient(feed);
            var observer = new GithubNuGetObservation(client);
            int writes = 0;
            try
            {
                GithubNuGetPublicationResult publication = await NuGetRelease.PublishAsync(
                    fresh.Result, fresh.Scratch, "controlled-read", observer.ObserveAsync,
                    (_, _, _, _, _) =>
                    {
                        writes++;
                        throw new AssertFailedException("Recovery must not call the publisher.");
                    }, context.CancellationToken);
                fixture.Attach(label + "-fresh-publication.json", publication);
                Assert.AreEqual(0, writes);
                Assert.IsNull(publication.Invocation);
                Assert.AreEqual(conflict ? ReleaseFileState.Conflict : ReleaseFileState.Exact,
                    publication.Before.Observation.Files[0].State);
                // Completion is the independent registry readback of the served original bytes.
                GithubNuGetObservationResult final = await observer.ObserveAsync(
                    fresh.Result.Prepared, "controlled-read", context.CancellationToken);
                GithubNuGetCompletionResult complete = NuGetRelease.Complete(
                    fresh.Result.Prepared, absent, final, publication);
                fixture.Attach(label + "-fresh-completion.json", complete);
                Assert.AreEqual(conflict ? ReleaseBusinessState.Unsatisfied :
                    ReleaseBusinessState.Satisfied, complete.Outcome.State);
                Assert.AreEqual(false, complete.MutationAttempted);
                Assert.IsTrue(feed.Requests.Any(request => request.Uri.EndsWith(".nupkg",
                    StringComparison.Ordinal)));
                Assert.IsTrue(feed.Requests.All(request => request.Method == "GET"));
                Assert.IsTrue(absentFeed.Requests.All(request => request.Method == "GET"));
                CollectionAssert.AreEqual(originalResultBytes, File.ReadAllBytes(originalResult));
                fixture.Attach(label + "-recovery-history-binding.json", new
                {
                    OriginalBytes = originalResultBytes.LongLength,
                    OriginalSha256 = Convert.ToHexStringLower(
                        SHA256.HashData(originalResultBytes)),
                    AfterBytes = new FileInfo(originalResult).Length,
                    AfterSha256 = DotNetPackageArchive.Digest(originalResult),
                });
                Assert.AreEqual(ReleaseBusinessState.Unknown, missing.Outcome.State);
                Assert.IsNull(missing.MutationAttempted);
            }
            finally
            {
                fixture.Attach(label + "-fresh-reads.json", feed.Requests);
                fixture.Attach(label + "-initial-reads.json", absentFeed.Requests);
            }
        }
    }

    private static void AssertEquivalent(Preparation first, Preparation second)
    {
        Assert.AreEqual(first.Result.Prepared.Subject, second.Result.Prepared.Subject);
        Assert.AreEqual(first.SourceSha256, second.SourceSha256);
        AssertNativeEqual(first.Result.Native, second.Result.Native);
        ReleaseFile original = first.Result.Prepared.Files[0];
        ReleaseFile rebuilt = second.Result.Prepared.Files[0];
        Assert.AreEqual(original.Size, rebuilt.Size);
        Assert.AreEqual(original.Sha256, rebuilt.Sha256);
        CollectionAssert.AreEqual(File.ReadAllBytes(first.Archive),
            File.ReadAllBytes(second.Archive));
        CollectionAssert.AreEqual(Payload(first.Archive, ".dll"), Payload(second.Archive, ".dll"));
        CollectionAssert.AreEqual(File.ReadAllBytes(first.Pdb), File.ReadAllBytes(second.Pdb));
        using var left = ZipFile.OpenRead(first.Archive);
        using var right = ZipFile.OpenRead(second.Archive);
        CollectionAssert.AreEqual(left.Entries.Select(entry => entry.FullName).ToArray(),
            right.Entries.Select(entry => entry.FullName).ToArray());
        // This product has IncludeSymbols=false: compare its actual separate native portable PDB.
        Assert.IsEmpty(left.Entries.Where(entry =>
            entry.FullName.EndsWith(".pdb", StringComparison.Ordinal)));
        Assert.IsEmpty(right.Entries.Where(entry =>
            entry.FullName.EndsWith(".pdb", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(first.Paths.Documents, second.Paths.Documents);
        CollectionAssert.AreEqual(first.Paths.CodeView, second.Paths.CodeView);
    }

    private static void AssertNativeEqual(Dictionary<string, string> first,
        Dictionary<string, string> second)
    {
        CollectionAssert.AreEqual(first.Keys.Order(StringComparer.Ordinal).ToArray(),
            second.Keys.Order(StringComparer.Ordinal).ToArray());
        foreach ((string key, string value) in first) Assert.AreEqual(value, second[key], key);
    }

    private static bool HasSourceControl(string archive)
    {
        using var pe = new PEReader(new MemoryStream(Payload(archive, ".dll")));
        MetadataReader metadata = pe.GetMetadataReader();
        TypeDefinition smoke = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(type => metadata.GetString(type.Name) == "Smoke");
        FieldDefinition[] fields = smoke.GetFields().Select(metadata.GetFieldDefinition)
            .Where(field => metadata.GetString(field.Name) == "SourceControl").ToArray();
        if (fields.Length == 0) return false;
        Constant constant = metadata.GetConstant(Assert.ContainsSingle(fields).GetDefaultValue());
        Assert.AreEqual(ConstantTypeCode.Int32, constant.TypeCode);
        Assert.AreEqual(42, metadata.GetBlobReader(constant.Value).ReadInt32());
        return true;
    }

    private static byte[] Payload(string path, string suffix)
    {
        using var zip = ZipFile.OpenRead(path);
        return Read(Assert.ContainsSingle(zip.Entries.Where(entry => entry.FullName.EndsWith(suffix,
            StringComparison.Ordinal))));
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using Stream input = entry.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private sealed record DebugPaths(string[] Documents, string[] CodeView);
    private sealed record Preparation(string Checkout, string Scratch, string Cache,
        DateTime SourceMtime, string SourceSha256, NuGetPreparationResult Result, string Archive,
        string Pdb, DebugPaths Paths, ReleaseCommand[] ActualCommands);

    // Owned native state and evidence retention for this bounded scenario only.
    private sealed class NativeFixture(TestContext context) : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("nuget-native-").FullName;
        private string Retained { get; } = Directory.CreateDirectory(Path.Combine(
            context.TestResultsDirectory!, "nuget-native", Guid.NewGuid().ToString("N"))).FullName;
        private const string Product = "tests/private/app/workflow-delivery/" +
            "fixtures/products/hcoona-release-smoke-github-packages";
        internal const string Project = Product + "/hcoona-release-smoke-github-packages.csproj";
        private static readonly string[] RootInputs = ["global.json", "Directory.Build.props",
            "Directory.Packages.props", "nuget.config", "version.json",
            ".editorconfig", "stylecop.json"];
        private readonly List<ReleaseCommand> commands = [];

        internal async Task<Preparation> PrepareAsync(int index,
            string selectedRef = "refs/heads/development", bool changeSource = false,
            string? timestamp = null)
        {
            CancellationToken token = context.CancellationToken;
            string source = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(source, "global.json")) ||
                !Directory.Exists(Path.Combine(source, "src/private/app/workflow-delivery")))
                source = Path.GetDirectoryName(source) ??
                    throw new InvalidDataException("Missing source root.");
            string checkout = Path.Combine(Root, index == 0 ? "a" : "longer-source-root");
            string scratch = Directory.CreateDirectory(Path.Combine(Root, "preparation-" + index))
                .FullName;
            string cache = Path.Combine(Root, "owned-cache-" + index);
            string revision = (await RunAsync(new("git", source, ["rev-parse", "HEAD"], 30))).Stdout
                .Trim();
            await RunAsync(new("git", source,
                ["clone", "--shared", "--no-checkout", "--", source, checkout], 120));
            await RunAsync(new("git", checkout, ["checkout", "--detach", revision], 120,
                new Dictionary<string, string?> { ["GIT_LFS_SKIP_SMUDGE"] = "1" }));
            string smoke = Path.Combine(checkout, Product, "Smoke.cs");
            if (changeSource)
            {
                string text = await File.ReadAllTextAsync(smoke, token);
                string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                await File.WriteAllTextAsync(smoke, text.Replace(
                    "public static class Smoke" + newline + "{",
                    "public static class Smoke" + newline + "{" + newline +
                        "    /// <summary>Identifies the source sensitivity control.</summary>" +
                        newline +
                        "    public const int SourceControl = 42;" + newline,
                    StringComparison.Ordinal), token);
                string blob = (await RunAsync(new("git", checkout,
                    ["hash-object", "--no-filters", "-w", "--", Product + "/Smoke.cs"], 30)))
                    .Stdout.Trim();
                await RunAsync(new("git", checkout,
                    ["update-index", "--cacheinfo", "100644", blob, Product + "/Smoke.cs"], 30));
                string tree = (await RunAsync(new("git", checkout, ["write-tree"], 30)))
                    .Stdout.Trim();
                revision = (await RunAsync(new("git", checkout,
                    ["-c", "user.name=Workflow fixture", "-c", "user.email=fixture@example.invalid",
                        "-c", "commit.gpgSign=false", "commit-tree", tree, "-p", revision,
                        "-m", "Native source sensitivity fixture"], 30))).Stdout.Trim();
                await RunAsync(new("git", checkout, ["update-ref", "HEAD", revision], 30));
            }
            DateTime mtime = new(2001 + index, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(smoke, mtime);
            string sourceHash = DotNetPackageArchive.Digest(smoke);
            SeedCache(source, cache);
            var subject = new ReleaseSubject(revision, selectedRef,
                "hcoona-release-smoke-github-packages",
                Product + "/workflow-delivery.release-unit.yml",
                "buddy", "github-packages-nuget", false);
            int firstCommand = commands.Count;
            NuGetPreparationResult result = await NuGetPreparation.PrepareAsync(new(subject,
                checkout, scratch), async (command, cancellation) =>
                {
                    var environment = new Dictionary<string, string?>(command.Environment!,
                        StringComparer.Ordinal);
                    // Preserve the existing clean consumer's separate native cache.
                    if (!environment.TryGetValue("NUGET_PACKAGES", out string? packages) ||
                        packages is null || !packages.StartsWith(scratch +
                            Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        environment["NUGET_PACKAGES"] = cache;
                    string[] arguments = command.Arguments;
                    if (command.Directory == checkout &&
                        command.Arguments[0] is "restore" or "msbuild")
                    {
                        if (timestamp is not null)
                        {
                            arguments = [.. arguments,
                                "-property:DeterministicTimestamp=" + timestamp];
                            environment["SOURCE_DATE_EPOCH"] = "315532802";
                        }
                        if (Environment.GetEnvironmentVariable("WORKFLOW_NATIVE_OFFLINE_CONFIG") is
                            { Length: > 0 } config)
                            arguments = [.. arguments, "-property:RestoreConfigFile=" +
                                ProjectCollection.Escape(config), "-property:NuGetAudit=false"];
                    }
                    NativeCommand actual = WithBinlog(command with { Arguments = arguments,
                        Environment = environment }, scratch, commands.Count);
                    NativeCommandResult outcome =
                        await NativeProcess.ExecuteAsync(actual, cancellation);
                    commands.Add(new("preparation", actual, outcome));
                    return outcome;
                }, token);
            ReleaseCommand[] executed = result.Commands.Select((command, offset) => command with
                { Command = commands[firstCommand + offset].Command }).ToArray();
            Attach("preparation-" + index + ".json", result);
            Attach("executed-preparation-" + index + ".json", executed);
            Attach("inputs-" + index + ".json", new
            {
                SourceRoot = source, Checkout = checkout, Scratch = scratch, Cache = cache,
                Subject = subject, SourceSha256 = sourceHash,
                SourceMtime = File.GetLastWriteTimeUtc(smoke),
                TimestampOverride = timestamp,
                SourceDateEpoch = timestamp is null ? null : "315532802",
                NativeDefaultTimestamp = "315532800", DerivedCommit = changeSource,
                SourceInputs = RootInputs.Select(name => Path.Combine(checkout, name))
                    .Concat(Directory.EnumerateFiles(Path.Combine(checkout, Product)))
                    .Where(File.Exists).Select(path => new
                    {
                        Path = Path.GetRelativePath(checkout, path).Replace('\\', '/'),
                        Bytes = new FileInfo(path).Length,
                        Sha256 = DotNetPackageArchive.Digest(path),
                    }).ToArray(),
            });
            RetainTree(checkout, "checkout-" + index);
            RetainTree(scratch, "preparation-" + index);
            Assert.IsEmpty(result.Errors, JsonSerializer.Serialize(result));
            NuGetRelease.Validate(result);
            string archive = Path.Combine(scratch, result.Prepared.Files[0].Path);
            string pdb = Assert.ContainsSingle(Directory.EnumerateFiles(Path.Combine(checkout,
                Product, "bin", "Release"), "Hcoona.ReleaseSmoke.GithubPackages.pdb",
                SearchOption.AllDirectories));
            using var provider = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdb));
            MetadataReader metadata = provider.GetMetadataReader();
            string[] documents = metadata.Documents.Select(handle => metadata.GetString(
                metadata.GetDocument(handle).Name)).ToArray();
            using var pe = new PEReader(new MemoryStream(Payload(archive, ".dll")));
            string[] codeView = pe.ReadDebugDirectory().Where(entry => entry.Type ==
                DebugDirectoryEntryType.CodeView).Select(entry => pe.ReadCodeViewDebugDirectoryData(
                    entry).Path).ToArray();
            var paths = new DebugPaths(documents, codeView);
            Attach("pdb-documents-" + index + ".json", metadata.Documents.Select(handle =>
            {
                Document document = metadata.GetDocument(handle);
                return new { Name = metadata.GetString(document.Name),
                    Algorithm = metadata.GetGuid(document.HashAlgorithm),
                    Hash = Convert.ToHexStringLower(metadata.GetBlobBytes(document.Hash)) };
            }).ToArray());
            Attach("paths-" + index + ".json", paths);
            Assert.IsNotEmpty(documents);
            Assert.ContainsSingle(codeView);
            Assert.IsTrue(documents.Any(path => path.Replace('\\', '/')
                .EndsWith("/Smoke.cs", StringComparison.Ordinal)));
            foreach (string path in documents.Concat(codeView))
            {
                Assert.IsFalse(path.Replace('\\', '/').Contains(Root.Replace('\\', '/'),
                    StringComparison.OrdinalIgnoreCase), path);
                Assert.IsFalse(path.Replace('\\', '/').Contains(checkout.Replace('\\', '/'),
                    StringComparison.OrdinalIgnoreCase), path);
            }
            return new(checkout, scratch, cache, File.GetLastWriteTimeUtc(smoke),
                sourceHash, result,
                archive, pdb, paths, executed);
        }

        private void SeedCache(string source, string cache)
        {
            string shared = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nuget", "packages");
            using JsonDocument locked = JsonDocument.Parse(File.ReadAllText(Path.Combine(source,
                Product, "packages.lock.json")));
            var dependencies = locked.RootElement.GetProperty("dependencies").GetProperty("net10.0")
                .EnumerateObject().Select(package => (Name: package.Name.ToLowerInvariant(),
                    Version: package.Value.GetProperty("resolved").GetString()!)).ToList();
            // The committed global.json selects the MSBuild SDK packages the product restores.
            foreach (string sdk in new[] { "DotNet.ReproducibleBuilds.Isolated",
                "Microsoft.Build.CopyOnWrite" })
                dependencies.Add((sdk.ToLowerInvariant(), RepositoryToolSelection.MsBuildSdk(sdk,
                    source)));
            var inputs = new List<object>();
            foreach ((string name, string version) in dependencies)
            {
                string from = Path.Combine(shared, name, version);
                Assert.IsTrue(Directory.Exists(from),
                    "Prepare pinned native dependency first: " + from);
                foreach (string file in Directory.EnumerateFiles(from, "*",
                    SearchOption.AllDirectories))
                {
                    string destination = Path.Combine(cache, name, version,
                        Path.GetRelativePath(from, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination);
                    if (file.EndsWith(".nupkg", StringComparison.Ordinal))
                        inputs.Add(new { Path = file, OwnedPath = destination,
                            Bytes = new FileInfo(file).Length,
                            Sha256 = DotNetPackageArchive.Digest(file) });
                }
            }
            Attach(Path.GetFileName(cache) + "-tools.json", inputs);
        }

        private async Task<NativeCommandResult> RunAsync(NativeCommand command)
        {
            NativeCommandResult result =
                await NativeProcess.ExecuteAsync(command, context.CancellationToken);
            commands.Add(new("fixture", command, result));
            Assert.IsTrue(result.Succeeded, JsonSerializer.Serialize(commands[^1]));
            return result;
        }

        internal static NativeCommand WithBinlog(NativeCommand command, string directory, int index)
            => command.Arguments[0] is "msbuild" or "restore" ? command with
            { Arguments = [.. command.Arguments, "-binaryLogger:" + Path.Combine(directory,
                "native-" + index + ".binlog")] } : command;

        internal string Attach<T>(string name, T value)
        {
            string path = Path.Combine(Retained, name);
            File.WriteAllText(path, JsonSerializer.Serialize(value));
            context.AddResultFile(path);
            return path;
        }

        internal void RetainTree(string directory, string label)
        {
            bool checkout = Directory.Exists(Path.Combine(directory, ".git"));
            IEnumerable<string> candidates = checkout ?
                Directory.EnumerateFiles(Path.Combine(directory,
                Product), "*", SearchOption.AllDirectories).Concat(RootInputs.Select(name =>
                    Path.Combine(directory, name)).Where(File.Exists)) :
                Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
            foreach (string file in candidates.Where(file =>
                    !file.Contains(Path.DirectorySeparatorChar + ".git" +
                    Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                    !file.Contains(Path.DirectorySeparatorChar + "profile" +
                        Path.DirectorySeparatorChar,
                        StringComparison.Ordinal) &&
                    (file.EndsWith(".nupkg", StringComparison.Ordinal) ||
                     file.EndsWith(".binlog", StringComparison.Ordinal) ||
                     file.EndsWith(".pdb", StringComparison.Ordinal) ||
                     file.EndsWith(".dll", StringComparison.Ordinal) ||
                     file.EndsWith(".json", StringComparison.Ordinal) &&
                         file.Contains(Path.DirectorySeparatorChar + "obj" +
                             Path.DirectorySeparatorChar,
                             StringComparison.Ordinal) ||
                     new[] { "global.json", "Directory.Build.props", "Directory.Packages.props",
                         "nuget.config", "version.json", "Smoke.cs", "packages.lock.json",
                         "hcoona-release-smoke-github-packages.csproj", "README.md",
                         "workflow-delivery.release-unit.yml", "workflow-delivery.quality.yml" }
                         .Contains(Path.GetFileName(file)) ||
                     Path.GetDirectoryName(file) == directory)))
            {
                // Whole checkout imports outside this product are not submitted as evidence.
                string relative = Path.GetRelativePath(directory, file);
                if (checkout && relative.Contains(
                    Path.DirectorySeparatorChar) && !relative.StartsWith(Product.Replace('/',
                        Path.DirectorySeparatorChar), StringComparison.Ordinal)) continue;
                string destination = Path.Combine(Retained, label, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, true);
                context.AddResultFile(destination);
            }
        }

        public void Dispose()
        {
            try
            {
                Attach("all-commands.json", commands);
                foreach (string directory in Directory.EnumerateDirectories(Root))
                    if (!Path.GetFileName(directory).StartsWith("owned-cache",
                        StringComparison.Ordinal))
                        RetainTree(directory, "final-" + Path.GetFileName(directory));
            }
            finally
            {
                foreach (string file in Directory.EnumerateFiles(Root, "*",
                    SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(Root, true);
            }
        }
    }

    private sealed record FeedRead(string Method, string Uri);

    private sealed class Feed(string version, byte[]? original) : HttpMessageHandler
    {
        internal List<FeedRead> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add(new(request.Method.Method, request.RequestUri!.AbsoluteUri));
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Uri uri = request.RequestUri!;
            HttpResponseMessage response;
            if (uri == new Uri(GithubNuGetObservation.ServiceIndex))
                response = Json("""
                    {"version":"3.0.0","resources":[
                      {"@id":"https://nuget.pkg.github.com/hcoona/flat/",
                       "@type":"PackageBaseAddress/3.0.0"},
                      {"@id":"https://nuget.pkg.github.com/hcoona/",
                       "@type":"PackagePublish/2.0.0"}]}
                    """);
            else if (uri.Host == "api.github.com" && uri.AbsolutePath.EndsWith("/versions",
                StringComparison.Ordinal))
                response = Json(original is null ? "[]" : JsonSerializer.Serialize(new[]
                    { new { id = 1, name = version } }));
            else if (uri.Host == "api.github.com")
                response = Json("""
                    {"name":"Hcoona.ReleaseSmoke.GithubPackages","package_type":"nuget",
                     "visibility":"public","repository":{"full_name":"hcoona/three"}}
                    """);
            else if (uri.AbsolutePath.EndsWith(".nupkg", StringComparison.Ordinal))
                response = new(original is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                    { Content = new ByteArrayContent(original ?? []) };
            else if (uri.AbsolutePath.EndsWith("/index.json", StringComparison.Ordinal))
                response = Json(JsonSerializer.Serialize(new { versions = original is null ?
                    Array.Empty<string>() : new[] { version } }));
            else throw new AssertFailedException("Unexpected controlled read: " + uri);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
