using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.ObjectModelRemoting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class DotNetNativeInputsTests
{
    private const string Locator = "Microsoft.NET.SDK.WorkloadAutoImportPropsLocator";

    [TestMethod]
    [DataRow(Locator, "", false, "Import")]
    [DataRow(Locator, "native-version", true, "Import")]
    [DataRow("microsoft.net.sdk.workloadautoimportpropslocator", "", true, "AdditionalFiles")]
    [DataRow("MICROSOFT.NET.SDK.WORKLOADAUTOIMPORTPROPSLOCATOR", "native-version", false,
        "Analyzer")]
    public void ClassifyObservedAutoImportClassifiesSiblingWithSdkIdentity(
        string identity, string version, bool trailingSeparator, string role)
    {
        using var fixture = new InputFixture();
        string pack = fixture.PathOf("packs", "TestPack", "9.8.7");
        string root = Path.Combine(pack, "Sdk");
        var result = new ObservedSdkResult(identity,
            trailingSeparator ? root + Path.DirectorySeparatorChar : root, version);
        ResolvedImport import = fixture.Import(Path.Combine(root, "AutoImport.props"), result);
        DotNetNativeInputs inputs = fixture.Inputs([import]);
        DotNetGraphInput input = fixture.Input(
            InputFixture.FileAt(pack, "targets", "Sibling.props"), role);

        DotNetGraphInput actual = inputs.Classify(input);

        Assert.AreEqual(new DotNetInputProvider("Sdk", pack, identity, version), actual.Provider);
        Assert.AreEqual(input, actual with { Provider = null });
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ClassifyReturnedRootsExpandsOnlyEachObservedImport(
        bool observePrimary, bool observeAdditional)
    {
        using var fixture = new InputFixture();
        string[] packs = [fixture.PathOf("primary"), fixture.PathOf("additional"),
            fixture.PathOf("unobserved")];
        string[] roots = packs.Select(pack => Path.Combine(pack, "Sdk")).ToArray();
        var result = new ObservedSdkResult(Locator, roots[0], "native-version", roots[1..]);
        var imports = new List<ResolvedImport>();
        if (observePrimary)
            imports.Add(fixture.Import(Path.Combine(roots[0], "AutoImport.props"), result));
        if (observeAdditional)
            imports.Add(fixture.Import(Path.Combine(roots[1], "AutoImport.props"), result));
        DotNetNativeInputs inputs = fixture.Inputs([.. imports]);
        bool[] observed = [observePrimary, observeAdditional, false];

        for (int index = 0; index < packs.Length; index++)
        {
            DotNetGraphInput sibling = fixture.Input(
                InputFixture.FileAt(packs[index], "targets", "Sibling.targets"));
            DotNetGraphInput actual = inputs.Classify(sibling);
            DotNetInputProvider? expected = observed[index]
                ? new("Sdk", packs[index], Locator, "native-version") : null;
            Assert.AreEqual(expected, actual.Provider, packs[index]);
            Assert.AreEqual(sibling, actual with { Provider = null });
            DotNetGraphInput direct = fixture.Input(
                InputFixture.FileAt(roots[index], "Direct.props"));
            Assert.AreEqual(new DotNetInputProvider("Sdk", roots[index], Locator,
                "native-version"), inputs.Classify(direct).Provider);
        }
    }

    [TestMethod]
    [DataRow("Microsoft.NET.Sdk")]
    [DataRow("Microsoft.NET.SDK.WorkloadManifestTargetsLocator")]
    [DataRow("Microsoft.NET.SDK.WorkloadAutoImportPropsLocator.Extra")]
    [DataRow("Prefix.Microsoft.NET.SDK.WorkloadAutoImportPropsLocator")]
    public void ClassifyUnrelatedSdkDoesNotExpandPack(string identity)
    {
        using var fixture = new InputFixture();
        string pack = fixture.PathOf("pack");
        string root = Path.Combine(pack, "Sdk");
        var result = new ObservedSdkResult(identity, root, "native-version");
        DotNetNativeInputs inputs = fixture.Inputs(
            [fixture.Import(Path.Combine(root, "AutoImport.props"), result)]);

        DotNetGraphInput sibling = fixture.Input(
            InputFixture.FileAt(pack, "targets", "Hook.props"));
        DotNetGraphInput direct = fixture.Input(InputFixture.FileAt(root, "Direct.props"));

        Assert.IsNull(inputs.Classify(sibling).Provider);
        Assert.AreEqual(new DotNetInputProvider("Sdk", root, identity, "native-version"),
            inputs.Classify(direct).Provider);
    }

    [TestMethod]
    [DataRow("different-root")]
    [DataRow("different-import")]
    [DataRow("wrong-folder")]
    [DataRow("prefix-folder")]
    [DataRow("failed")]
    [DataRow("folder-case")]
    [DataRow("import-case")]
    public void ClassifyInvalidObservationDoesNotExpandPack(string scenario)
    {
        using var fixture = new InputFixture();
        string pack = fixture.PathOf("pack");
        string folder = scenario switch
        {
            "wrong-folder" => "tools",
            "prefix-folder" => "Sdk-extra",
            "folder-case" => "sdk",
            _ => "Sdk",
        };
        string root = Path.Combine(pack, folder);
        var result = new ObservedSdkResult(Locator, root, "", success: scenario != "failed");
        string imported = scenario switch
        {
            "different-root" => fixture.PathOf("other", "Sdk", "AutoImport.props"),
            "different-import" => Path.Combine(root, "Sdk.props"),
            "import-case" => Path.Combine(root, "autoimport.props"),
            _ => Path.Combine(root, "AutoImport.props"),
        };
        DotNetNativeInputs inputs = fixture.Inputs([fixture.Import(imported, result)]);
        DotNetGraphInput sibling = fixture.Input(
            InputFixture.FileAt(pack, "targets", "Hook.props"));
        bool matchesOnWindows = OperatingSystem.IsWindows() &&
            scenario is "folder-case" or "import-case";
        DotNetInputProvider? expected = matchesOnWindows ? new("Sdk", pack, Locator, "") : null;

        DotNetGraphInput actual = inputs.Classify(sibling);

        Assert.AreEqual(expected, actual.Provider);
        Assert.AreEqual(sibling, actual with { Provider = null });
        if (scenario == "failed")
            Assert.IsNull(inputs.Classify(fixture.Input(InputFixture.FileAt(root, "Direct.props")))
                .Provider);
    }

    [TestMethod]
    [DataRow("escape")]
    [DataRow("pack-prefix")]
    [DataRow("sdk-prefix")]
    public void ClassifyEscapedOrPrefixSiblingLeavesProviderNull(string scenario)
    {
        using var fixture = new InputFixture();
        string pack = fixture.PathOf("pack");
        string root = Path.Combine(pack, "Sdk");
        var result = new ObservedSdkResult(
            scenario == "sdk-prefix" ? "Microsoft.NET.Sdk" : Locator, root, "");
        DotNetNativeInputs inputs = fixture.Inputs(
            [fixture.Import(Path.Combine(root, "AutoImport.props"), result)]);
        string outside = scenario switch
        {
            "escape" => Path.GetFullPath(Path.Combine(pack, "..", "hooks", "Hook.props")),
            "pack-prefix" => fixture.PathOf("pack-extra", "targets", "Hook.props"),
            _ => Path.Combine(pack, "Sdk-extra", "Hook.props"),
        };
        DotNetGraphInput input = fixture.Input(InputFixture.FileAt(outside));

        DotNetGraphInput actual = inputs.Classify(input);

        Assert.IsNull(actual.Provider);
        Assert.AreEqual(input, actual);
    }

    [TestMethod]
    [DataRow("Import", "Evaluation", true, false)]
    [DataRow("Import", "Evaluation", true, true)]
    [DataRow("Project", "Execution", false, true)]
    public void ClassifyCommittedInputClearsProviderBeforeEligibilityGuards(
        string role, string stage, bool exists, bool preclassified)
    {
        using var fixture = new InputFixture();
        string pack = fixture.PathOf("pack");
        string root = Path.Combine(pack, "Sdk");
        var result = new ObservedSdkResult(Locator, root, "");
        string path = Path.Combine(pack, "targets", "Committed.props");
        if (exists) InputFixture.FileAt(path);
        var prior = new DotNetInputProvider("LockedPackage", pack, "Prior.Package", "1.2.3");
        DotNetGraphInput input = fixture.Input(path, role, stage,
            preclassified ? prior : null);
        ResolvedImport import = fixture.Import(Path.Combine(root, "AutoImport.props"), result);
        DotNetNativeInputs inputs = fixture.Inputs([import], [path]);

        DotNetGraphInput actual = inputs.Classify(input);

        Assert.IsNull(actual.Provider);
        Assert.AreEqual(input with { Provider = null }, actual);
        if (preclassified)
            Assert.AreSame(prior, fixture.Inputs([import]).Classify(input).Provider);
    }

    [TestMethod]
    [DataRow("Import", "Evaluation", false, false)]
    [DataRow("Import", "Execution", true, false)]
    [DataRow("Project", "Evaluation", true, false)]
    [DataRow("CentralPackageConfiguration", "Evaluation", true, false)]
    [DataRow("ImportCandidate", "Evaluation", true, false)]
    [DataRow("EditorConfigCandidate", "Evaluation", true, false)]
    [DataRow("Import", "Evaluation", true, true)]
    public void ClassifyIneligibleInputRetainsInput(
        string role, string stage, bool exists, bool preclassified)
    {
        using var fixture = new InputFixture();
        string pack = fixture.PathOf("pack");
        string root = Path.Combine(pack, "Sdk");
        var result = new ObservedSdkResult(Locator, root, "");
        DotNetNativeInputs inputs = fixture.Inputs(
            [fixture.Import(Path.Combine(root, "AutoImport.props"), result)]);
        string path = Path.Combine(pack, "targets", "Sibling.props");
        if (exists) InputFixture.FileAt(path);
        DotNetInputProvider? prior = preclassified
            ? new("LockedPackage", pack, "Prior.Package", "1.2.3") : null;
        DotNetGraphInput input = fixture.Input(path, role, stage, prior);

        DotNetGraphInput actual = inputs.Classify(input);

        Assert.AreSame(input, actual);
        Assert.AreEqual(prior, actual.Provider);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ConstructorRelativeReturnedPathThrows(bool relativeAdditional)
    {
        using var fixture = new InputFixture();
        string root = fixture.PathOf("pack", "Sdk");
        var result = new ObservedSdkResult(Locator, relativeAdditional ? root : "relative/Sdk",
            "", relativeAdditional ? ["relative/Sdk"] : []);
        ResolvedImport import = fixture.Import(Path.Combine(root, "AutoImport.props"), result);

        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Inputs([import]));
    }

    // A controlled public-contract substitute, not a native resolver implementation.
    private sealed class ObservedSdkResult : SdkResult
    {
        internal ObservedSdkResult(string identity, string path, string version,
            string[]? additionalPaths = null, bool success = true)
        {
            Success = success;
            Path = path;
            Version = version;
            SdkReference = new(identity, "requested-version", "");
            AdditionalPaths = additionalPaths;
        }
    }

    private sealed class InputFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(),
            "workflow-workload-inputs-" + Guid.NewGuid().ToString("N"));
        private readonly ProjectCollection collection = new();
        private readonly ProjectRootElement project;
        private readonly ProjectInstance instance;
        private readonly DotNetNodeIdentity consumer;

        internal InputFixture()
        {
            Directory.CreateDirectory(directory);
            project = ProjectRootElement.Create(collection);
            project.FullPath = PathOf("consumer.proj");
            instance = new ProjectInstance(project);
            consumer = new(project.FullPath, new(StringComparer.Ordinal)
            {
                ["Configuration"] = "Debug",
            });
        }

        internal string PathOf(params string[] parts) => Path.Combine([directory, .. parts]);

        internal static string FileAt(params string[] parts)
        {
            string path = Path.Combine(parts);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<Project />");
            return path;
        }

        internal ResolvedImport Import(string path, SdkResult result)
        {
            var imported = ProjectRootElement.Create(collection);
            imported.FullPath = FileAt(path);
            ProjectImportElement element = project.AddImport(path);
            ResolvedImport import = LinkedObjectsFactory.Get(collection).Create(
                element, imported, 0, result, true);
            Assert.AreSame(result, import.SdkResult);
            Assert.AreEqual(path, import.ImportedProject.FullPath);
            return import;
        }

        internal DotNetNativeInputs Inputs(ResolvedImport[] imports,
            string[]? committed = null) => new(instance, imports, [], committed ?? []);

        internal DotNetGraphInput Input(string path, string role = "Import",
            string stage = "Evaluation", DotNetInputProvider? provider = null) =>
            new(path, role, stage, consumer, provider);

        public void Dispose()
        {
            collection.Dispose();
            Directory.Delete(directory, true);
        }
    }
}
