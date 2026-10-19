using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Release;

// Contract tests that run the mise-locked UV and CPython, and the root lock's PyPA build,
// packaging, Hatchling and nbgv-python, through the application's own readers. The Linux
// native-preparation job runs them under `mise exec --locked`; a pin change that alters a
// consumed fact fails here. Network effects are confined to loopback: every row that could
// reach a registry routes non-loopback traffic to a closed proxy port.
[TestClass]
[TestCategory("PythonNativeContract")]
public sealed class PythonNativeContractTests(TestContext context)
{
    private const string DeadProxy = "http://127.0.0.1:9";
    private static readonly string[] Contexts = [".", "a", "b", "c"];
    private static readonly string[] Groups = ["tools"];
    private static readonly string[] ToolsReach = ["b"];
    private static readonly string[] BuildNames = ["b", "hatchling", "nbgv-python"];
    private static readonly string[] BuildExtras = ["cli"];
    private static readonly string[] Exchange = ["GET /_/oidc/audience", "GET /token",
        "POST /_/oidc/mint-token", "POST /legacy/", "POST /_/oidc/burn-token"];
    private static string? uv, interpreter, tools;
    private static string Sources => Path.Combine(RepositoryToolSelection.SourceRoot,
        "src/private/app/workflow-delivery/Native/Python");

    [ClassInitialize]
    public static async Task PrepareAsync(TestContext classContext)
    {
        if (!OperatingSystem.IsLinux()) return;
        CancellationToken token = classContext.CancellationToken;
        uv = await WhichAsync("uv", token);
        interpreter = await WhichAsync("python", token);
        // The planning helper environment the Python group prepares: both root groups.
        tools = Directory.CreateTempSubdirectory("python-contract-tools-").FullName;
        NativeCommandResult sync = await NativeProcess.ExecuteAsync(new(uv,
            RepositoryToolSelection.SourceRoot, ["sync", "--frozen", "--only-group", "dev",
                "--only-group", "workflow-delivery-python", "--no-editable", "--python",
                interpreter], 600, new Dictionary<string, string?>
            {
                ["UV_PROJECT_ENVIRONMENT"] = Path.Combine(tools, "env"),
                ["UV_PYTHON_DOWNLOADS"] = "never",
                ["UV_NO_SYSTEM_CONFIG"] = "1",
            }), token);
        Assert.IsTrue(sync.Succeeded, NativeDiagnostic.Describe(sync));
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        if (tools is not null) ScratchRoots.TryDelete(tools);
    }

    private static string Helper => Path.Combine(tools!, "env", "bin", "python");

    // Planning premises: UV metadata reports ordinary, extra and group edges with their
    // markers, but no build edge (a member needed only to build another gets none) and no node
    // for an empty or marker-disjoint group, in only the node kinds and sources the reader
    // admits. The build edge comes from PyPA passive extraction instead. The projection the
    // planner consumes is asserted on the composed answers.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task MetadataAndPassiveExtractionKeepTheGuardedJoinPremises()
    {
        using var workspace = new Scratch();
        string root = workspace.Path;
        Write(root, "pyproject.toml", """
            [project]
            name = "contract-root"
            version = "0.0.0"
            requires-python = ">=3.14"
            dependencies = []

            [dependency-groups]
            dev = []
            legacy = ["b ; python_version < '3.0'"]
            tools = ["b"]

            [tool.uv.workspace]
            members = ["a", "b", "c"]

            [tool.uv.sources]
            b = { workspace = true }
            c = { workspace = true }
            """);
        Write(root, "a/pyproject.toml", """
            [project]
            name = "a"
            version = "2.0.0"
            requires-python = ">=3.14"
            dependencies = ["c[feat] ; sys_platform != 'emscripten'"]

            [build-system]
            requires = ["hatchling", "B", "nbgv-python[cli]"]
            build-backend = "hatchling.build"

            [tool.hatch.version]
            source = "nbgv"

            [tool.hatch.version.nbgv]
            write = { file = "src/a/_version.py" }

            [tool.uv.sources]
            B = { workspace = true }
            """);
        Write(root, "b/pyproject.toml", """
            [project]
            name = "b"
            version = "1.0.0"
            requires-python = ">=3.14"
            dependencies = []

            [build-system]
            requires = ["hatchling"]
            build-backend = "hatchling.build"
            """);
        Write(root, "c/pyproject.toml", """
            [project]
            name = "c"
            version = "3.0.0"
            requires-python = ">=3.14"
            dependencies = []

            [project.optional-dependencies]
            feat = ["b"]

            [build-system]
            requires = ["hatchling"]
            build-backend = "hatchling.build"
            """);
        Dictionary<string, string?> environment = Isolated(workspace);
        NativeCommandResult locked = await NativeProcess.ExecuteAsync(new(uv!, root,
            ["lock", "--offline", "--python", interpreter!], 120, environment),
            context.CancellationToken);
        Assert.IsTrue(locked.Succeeded, NativeDiagnostic.Describe(locked));

        PythonMetadata metadata = await new PythonMetadataReader().ReadAsync(root, uv!,
            interpreter!, environment, context.CancellationToken);
        CollectionAssert.AreEquivalent(Contexts,
            metadata.Members.Select(member => member.Directory).ToArray());
        PythonMetadataMember a = metadata.Members.Single(member => member.Name == "a");
        PythonMetadataMember b = metadata.Members.Single(member => member.Name == "b");
        PythonMetadataMember c = metadata.Members.Single(member => member.Name == "c");
        // a's only edge is its ordinary marked dependency on c's extra; b, needed only to
        // build a, has none.
        PythonMetadataEdge ordinary = Assert.ContainsSingle(metadata.Nodes[a.Id].Dependencies,
            "UV now reports a build edge or drops the ordinary one.");
        Assert.AreEqual("sys_platform != 'emscripten'", ordinary.Marker);
        PythonMetadataEntry extra = Assert.ContainsSingle(metadata.Nodes[c.Id].Extras);
        Assert.AreEqual("feat", extra.Name);
        Assert.AreEqual(extra.Id, ordinary.Id);
        Assert.AreEqual("extra", metadata.Nodes[extra.Id].Kind);
        CollectionAssert.AreEquivalent(new[] { c.Id, b.Id }.Order().ToArray(), metadata.Nodes[
            extra.Id].Dependencies.Select(edge => edge.Id).Order().ToArray());
        string[] groups = metadata.Nodes.Values.Where(node => node.Kind == "group")
            .Select(node => node.Dimension!).ToArray();
        CollectionAssert.AreEqual(Groups, groups,
            "UV now reports an empty or marker-disjoint group.");
        PythonMetadataEntry tools = Assert.ContainsSingle(metadata.Nodes[
            metadata.Members.Single(member => member.Directory == ".").Id].Groups);
        Assert.AreEqual(b.Id, Assert.ContainsSingle(metadata.Nodes[tools.Id].Dependencies).Id);

        var reader = new PythonPassiveReader();
        string[] configuration = await reader.ReadConfigurationAsync(root, Helper,
            Path.Combine(Sources, "passive.py"), Directories(root), environment,
            context.CancellationToken);
        Assert.HasCount(4, configuration);
        PythonPassiveInputs[] passive = await Task.WhenAll(Directories(root).Select(directory =>
                reader.ReadAsync(directory, Helper, Path.Combine(Sources, "passive.py"),
                    environment, context.CancellationToken)));
        PythonPassiveInputs consumer = passive.Single(item => item.Project.Directory ==
            Path.Combine(root, "a"));
        CollectionAssert.AreEqual(BuildNames,
            consumer.Project.BuildRequirements!.Select(item => item.Name).ToArray());
        CollectionAssert.AreEqual(BuildExtras, consumer.Project.BuildRequirements!
            .Single(item => item.Name == "nbgv-python").Extras);
        Assert.AreEqual("workspace", consumer.Project.Sources["b"]);
        Assert.AreEqual(new PythonNbgvCoordinates(Path.Combine(root, "a"),
            Path.Combine(root, "a", "src", "a", "_version.py")), consumer.Nbgv);
        Assert.IsNull(passive.Single(item => item.Project.Directory == root).Project
            .BuildRequirements);

        PythonBuildAssociation[] builds = PythonDependencyProjection.ReadBuildAssociations(
            metadata, passive.Select(item => item.Project).ToArray());
        PythonBuildAssociation association = Assert.ContainsSingle(builds);
        Assert.AreEqual(("a", "b"), (association.Consumer, association.Producer));
        Assert.IsEmpty(association.Extras);

        // The graph the planner consumes: ordinary edges through the extra, the default dev
        // group (declared but empty, so no entry) and the build leg.
        PythonGroupOperation[] operations = Directories(root).Select(directory =>
            new PythonGroupOperation(directory, directory, [], false)).ToArray();
        PythonProjectDependencies[] projected = PythonDependencyProjection.Project(metadata,
            PythonDependencyProjection.SelectGroups(metadata, operations), builds);
        string Edges(string directory)
        {
            PythonProjectDependencies project = projected.Single(item =>
                item.Directory == directory);
            return string.Join(",", project.Ordinary) + "|" + string.Join(",", project.Build);
        }
        Assert.AreEqual("b,c|b", Edges("a"));
        Assert.AreEqual("|", Edges("b"));
        Assert.AreEqual("|", Edges("c"));
        Assert.AreEqual("|", Edges("."));
        // A root operation that selects the tools group reaches b through the group edge.
        PythonProjectDependencies withTools = PythonDependencyProjection.Project(metadata,
            [.. PythonDependencyProjection.SelectGroups(metadata, operations),
                new(".", ["tools"])], builds).Single(item => item.Directory == ".");
        CollectionAssert.AreEqual(ToolsReach, withTools.Ordinary);
    }

    private static string[] Directories(string root) => [root, Path.Combine(root, "a"),
        Path.Combine(root, "b"), Path.Combine(root, "c")];

    // The planning identity reads the real Hatchling metadata through the package helper.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task HatchlingIdentityNormalizesTheDistributionName()
    {
        using var project = new Scratch();
        Write(project.Path, "pyproject.toml", """
            [project]
            name = "Contract_Probe.X"
            version = "1.2.3"

            [build-system]
            requires = ["hatchling"]
            build-backend = "hatchling.build"
            """);
        NativeCommandResult identity = await NativeProcess.ExecuteAsync(new(Helper,
            project.Path, ["-I", Path.Combine(Sources, "packages.py"), "identity",
                project.Path], 60, Isolated(project)), context.CancellationToken);
        Assert.IsTrue(identity.Succeeded, NativeDiagnostic.Describe(identity));
        using JsonDocument answer = JsonDocument.Parse(identity.Stdout);
        Assert.AreEqual("contract-probe-x", answer.RootElement.GetProperty("name").GetString());
        Assert.AreEqual("1.2.3", answer.RootElement.GetProperty("version").GetString());
    }

    // The index-tool recipe the smoke workflow exports from the root lock: hash-pinned,
    // registry-only and including the locked packaging used by the index helper.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task ExportedIndexToolRecipeIsHashPinnedRegistryOnly()
    {
        NativeCommandResult export = await NativeProcess.ExecuteAsync(new(uv!,
            RepositoryToolSelection.SourceRoot, ["--no-config", "export", "--frozen",
                "--only-group", "dev", "--no-emit-workspace", "--no-emit-local",
                "--no-annotate", "--no-header", "--format", "requirements.txt"], 120),
            context.CancellationToken);
        Assert.IsTrue(export.Succeeded, NativeDiagnostic.Describe(export));
        string[] requirements = Requirements(export.Stdout);
        Assert.IsNotEmpty(requirements);
        foreach (string requirement in requirements)
        {
            Assert.IsFalse(requirement.StartsWith('-') || requirement.Contains(" @ ") ||
                requirement.StartsWith('.') || requirement.StartsWith('/'), requirement);
            Assert.Contains("==", requirement.Split(" \\")[0]);
            Assert.Contains("--hash=sha256:", requirement);
        }
        string packaging = LockedPackage("packaging");
        Assert.IsTrue(requirements.Any(requirement =>
            requirement.StartsWith("packaging==" + packaging + " ", StringComparison.Ordinal)),
            "The exported recipe omits the locked packaging " + packaging + ".");
    }

    // The index-tool installation's real flags: a wrong hash or a missing hash stops
    // preparation and a matching hash installs. Only a local file is requested.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow("matching", true)]
    [DataRow("different", false)]
    [DataRow("missing", false)]
    public async Task IndexToolInstallationRequiresMatchingHashes(string hash, bool installs)
    {
        using var scratch = new Scratch();
        string wheel = Wheel(scratch.Path, "contract_probe", "0.0.1");
        string digest = hash switch
        {
            "matching" => " --hash=sha256:" + Convert.ToHexStringLower(SHA256.HashData(
                File.ReadAllBytes(wheel))),
            "different" => " --hash=sha256:" + new string('0', 64),
            _ => "",
        };
        string recipe = Path.Combine(scratch.Path, "requirements.txt");
        System.IO.File.WriteAllText(recipe, "contract-probe @ " +
            new Uri(wheel).AbsoluteUri + digest +
            "\n");
        string state = Directory.CreateDirectory(Path.Combine(scratch.Path, "state")).FullName;
        PythonIndexToolResult result = await PythonIndexToolPreparation.PrepareAsync(new(uv!,
            interpreter!, recipe, state), (command, token) => NativeProcess.ExecuteAsync(
                command with { Environment = Loopback(command.Environment) }, token),
            context.CancellationToken);

        Assert.AreEqual(installs, result.Errors.Length == 0, JsonSerializer.Serialize(
            result.Commands.Select(item => NativeDiagnostic.Describe(item.Result))));
        Assert.HasCount(2, result.Commands);
        Assert.IsTrue(result.Commands[0].Result.Succeeded);
        Assert.AreEqual(installs, result.Commands[1].Result.Succeeded);
    }

    // The real UV trusted-publishing exchange against a loopback registry and token service
    // with fake identities: the publisher's exact flags are accepted, one upload of the
    // original bytes uses the minted token, and its mask line reaches the runner.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task TrustedPublishingUploadsOnceAndForwardsTheTokenMask()
    {
        using var publication = await Publication.RunAsync(uv!, "accept",
            context.CancellationToken);
        PythonPublishResult result = publication.Result;

        Assert.IsTrue(result.Upload.Command.Succeeded, NativeDiagnostic.Describe(
            result.Upload.Command));
        Assert.AreEqual("::add-mask::" + LoopbackRegistry.Minted + Environment.NewLine,
            publication.Masks);
        LoopbackRegistry.Request[] requests = publication.Requests;
        CollectionAssert.AreEqual(Exchange, requests.Select(request =>
            request.Method + " " + request.Path).ToArray());
        Assert.AreEqual("Bearer " + LoopbackRegistry.RequestToken, requests[1].Authorization);
        Assert.Contains("audience=" + LoopbackRegistry.Audience, requests[1].Query);
        LoopbackRegistry.Request posted = requests[3];
        Assert.AreEqual(Publication.Credential, posted.Authorization);
        Assert.IsTrue(posted.Body.AsSpan().IndexOf(publication.Wheel) >= 0);
        Assert.IsFalse(posted.Body.AsSpan().IndexOf(publication.SdistName) >= 0);
    }

    // UV's native upload handling the publisher relies on instead of an application retry:
    // a returned failure status, including the registry's existing-file answer, ends the
    // invocation after one upload; a transport failure is retried a bounded number of times;
    // a same-realm redirect is followed with the token and another realm is refused. Every
    // failure is a failed command, never a success.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow("unavailable", 1, false)]
    [DataRow("server-error", 1, false)]
    [DataRow("existing-file", 1, false)]
    [DataRow("reset", 4, false)]
    [DataRow("same-realm-redirect", 2, true)]
    [DataRow("other-realm-redirect", 1, false)]
    public async Task NativeUploadHandlingStaysBoundedAndFailsClosed(string answer, int uploads,
        bool succeeds)
    {
        using var publication = await Publication.RunAsync(uv!, answer,
            context.CancellationToken);
        NativeCommandResult command = publication.Result.Upload.Command;

        Assert.AreEqual(succeeds, command.Succeeded, NativeDiagnostic.Describe(command));
        LoopbackRegistry.Request[] posts = publication.Requests.Where(request =>
            request.Method == "POST" && request.Path.StartsWith("/legacy",
                StringComparison.Ordinal)).ToArray();
        Assert.HasCount(uploads, posts);
        Assert.IsTrue(posts.All(post => post.Authorization == Publication.Credential &&
            post.Body.AsSpan().IndexOf(publication.Wheel) >= 0));
        // Another realm (here "localhost" on the same port) never receives the token.
        Assert.IsTrue(publication.Requests.All(request => request.Host.StartsWith(
            "127.0.0.1:", StringComparison.Ordinal)));
    }

    // One publisher invocation against the loopback registry, retargeted only in its URL.
    private sealed class Publication : IDisposable
    {
        internal static readonly string Credential = "Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes("__token__:" + LoopbackRegistry.Minted));
        private readonly Scratch scratch = new();
        internal PythonPublishResult Result { get; private set; } = null!;
        internal string Masks { get; private set; } = "";
        internal LoopbackRegistry.Request[] Requests { get; private set; } = [];
        internal byte[] Wheel { get; private set; } = [];
        internal byte[] SdistName { get; private set; } = [];

        internal static async Task<Publication> RunAsync(string uv, string answer,
            CancellationToken token)
        {
            var publication = new Publication();
            try { await publication.PublishAsync(uv, answer, token); }
            catch { publication.Dispose(); throw; }
            return publication;
        }

        private async Task PublishAsync(string uv, string answer, CancellationToken token)
        {
            using var authority = new LoopbackAuthority(scratch.Path);
            await using var registry = new LoopbackRegistry(authority.Leaf, answer);
            string originals = Directory.CreateDirectory(System.IO.Path.Combine(scratch.Path,
                "originals")).FullName;
            string wheel = PythonNativeContractTests.Wheel(originals,
                "hcoona_release_smoke_python", "0.1.0b1");
            string sdist = System.IO.Path.Combine(originals,
                "hcoona_release_smoke_python-0.1.0b1.tar.gz");
            File.WriteAllBytes(sdist, [1, 2, 3]);
            Wheel = File.ReadAllBytes(wheel);
            SdistName = Encoding.UTF8.GetBytes(System.IO.Path.GetFileName(sdist));
            static ReleaseFile Original(string output, string kind, string path) =>
                new("python-distributions", output, kind, "hcoona-release-smoke-python",
                    "0.1.0b1", System.IO.Path.GetFileName(path), new FileInfo(path).Length,
                    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            var prepared = new PreparedRelease(new(new string('a', 40),
                "refs/heads/development", "hcoona-release-smoke-python", "unit.yml", "buddy",
                "testpypi", false), [Original("wheel", "python-wheel", wheel),
                    Original("sdist", "python-sdist", sdist)], ["qualified"],
                [new("qualified", ReleaseCheckStatus.Passed)]);
            string upload = "https://127.0.0.1:" + registry.Port + "/legacy/";
            var platform = new Dictionary<string, string?>
            {
                ["GITHUB_ACTIONS"] = "true",
                ["ACTIONS_ID_TOKEN_REQUEST_URL"] = "https://127.0.0.1:" + registry.Port +
                    "/token?api-version=2.0",
                ["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = LoopbackRegistry.RequestToken,
            };
            using var masks = new StringWriter();
            string state = Directory.CreateDirectory(System.IO.Path.Combine(scratch.Path,
                "publisher")).FullName;
            Result = await PythonNativePublisher.PublishAsync(prepared, originals, "wheel",
                new(uv, state), platform, masks, (command, cancellation) =>
                {
                    // The production command, retargeted to the loopback registry only.
                    int url = Array.IndexOf(command.Arguments, "--publish-url") + 1;
                    Assert.AreEqual("https://test.pypi.org/legacy/", command.Arguments[url]);
                    string[] arguments = [.. command.Arguments];
                    arguments[url] = upload;
                    var environment = new Dictionary<string, string?>(command.Environment!,
                        StringComparer.Ordinal) { ["SSL_CERT_FILE"] = authority.Bundle };
                    return NativeProcess.ExecuteAsync(command with
                    {
                        Arguments = arguments,
                        Environment = Loopback(environment),
                    }, cancellation);
                }, token);
            Masks = masks.ToString();
            Requests = registry.Requests;
        }

        public void Dispose() => scratch.Dispose();
    }

    private static Dictionary<string, string?> Loopback(
        IReadOnlyDictionary<string, string?>? environment)
    {
        var values = new Dictionary<string, string?>(environment ??
            new Dictionary<string, string?>(), StringComparer.Ordinal);
        foreach (string name in new[] { "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY",
                     "https_proxy", "http_proxy", "all_proxy" })
            values[name] = DeadProxy;
        values["NO_PROXY"] = values["no_proxy"] = "127.0.0.1,localhost";
        return values;
    }

    private static Dictionary<string, string?> Isolated(Scratch scratch) => new()
    {
        ["HOME"] = Directory.CreateDirectory(Path.Combine(scratch.State, "home")).FullName,
        ["UV_CACHE_DIR"] = Path.Combine(scratch.State, "uv-cache"),
        ["UV_PYTHON_DOWNLOADS"] = "never",
        ["UV_NO_SYSTEM_CONFIG"] = "1",
        ["UV_NO_PROGRESS"] = "1",
        ["PYTHONNOUSERSITE"] = "1",
        ["PYTHONDONTWRITEBYTECODE"] = "1",
        ["HTTPS_PROXY"] = DeadProxy,
        ["HTTP_PROXY"] = DeadProxy,
        ["ALL_PROXY"] = DeadProxy,
    };

    private static string[] Requirements(string export) => export.Replace("\\\n", " \\")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string LockedPackage(string name)
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepositoryToolSelection.SourceRoot,
            "uv.lock"));
        int index = Array.IndexOf(lines, "name = \"" + name + "\"");
        Assert.IsGreaterThanOrEqualTo(0, index, name);
        return lines[index + 1]["version = \"".Length..^1];
    }

    private static void Write(string root, string path, string text)
    {
        string full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    internal static string Wheel(string directory, string name, string version)
    {
        string path = Path.Combine(directory, $"{name}-{version}-py3-none-any.whl");
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        string info = $"{name}-{version}.dist-info/";
        foreach ((string entry, string text) in new[]
        {
            (name + "/__init__.py", ""),
            (info + "METADATA", $"Metadata-Version: 2.1\nName: {name.Replace('_', '-')}\n" +
                $"Version: {version}\n"),
            (info + "WHEEL", "Wheel-Version: 1.0\nGenerator: contract\nRoot-Is-Purelib: true\n" +
                "Tag: py3-none-any\n"),
            (info + "RECORD", ""),
        })
        {
            using var stream = new StreamWriter(zip.CreateEntry(entry).Open());
            stream.Write(text);
        }
        return path;
    }

    private static async Task<string> WhichAsync(string tool, CancellationToken token)
    {
        string path = (await NativeProcess.RunAsync("mise", RepositoryToolSelection.SourceRoot,
            ["which", tool], token)).Trim();
        Assert.IsTrue(Path.IsPathFullyQualified(path) && File.Exists(path), tool);
        return path;
    }

    private sealed class Scratch : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("python-contract-")
            .FullName;
        internal string Path => Directory.CreateDirectory(System.IO.Path.Combine(Root,
            "workspace")).FullName;
        internal string State => Directory.CreateDirectory(System.IO.Path.Combine(Root,
            "state")).FullName;
        public void Dispose() => ScratchRoots.TryDelete(Root);
    }

    // A disposable CA and a loopback leaf; UV's rustls rejects a self-signed end entity.
    private sealed class LoopbackAuthority : IDisposable
    {
        internal string Bundle { get; }
        internal X509Certificate2 Leaf { get; }

        internal LoopbackAuthority(string directory)
        {
            using var caKey = RSA.Create(2048);
            var caRequest = new CertificateRequest("CN=python-contract-ca", caKey,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false,
                0, true));
            caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign, true));
            caRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(
                caRequest.PublicKey, false));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            using X509Certificate2 ca = caRequest.CreateSelfSigned(now.AddMinutes(-5),
                now.AddHours(1));
            using var leafKey = RSA.Create(2048);
            var leafRequest = new CertificateRequest("CN=127.0.0.1", leafKey,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(IPAddress.Loopback);
            // The other-realm redirect row targets localhost; a request UV forwarded there must
            // complete TLS and reach the registry, so the row can observe it.
            names.AddDnsName("localhost");
            leafRequest.CertificateExtensions.Add(names.Build());
            leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,
                false, 0, true));
            leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")], false));
            leafRequest.CertificateExtensions.Add(
                X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false));
            using X509Certificate2 signed = leafRequest.Create(ca, now.AddMinutes(-5),
                now.AddHours(1), RandomNumberGenerator.GetBytes(16));
            using X509Certificate2 paired = signed.CopyWithPrivateKey(leafKey);
            Leaf = X509CertificateLoader.LoadPkcs12(paired.Export(X509ContentType.Pkcs12),
                null);
            Bundle = System.IO.Path.Combine(directory, "loopback-ca.pem");
            File.WriteAllText(Bundle, ca.ExportCertificatePem() + "\n");
        }

        public void Dispose() => Leaf.Dispose();
    }

    // A minimal HTTPS/1.1 registry and token service; it records every request it receives.
    private sealed class LoopbackRegistry : IAsyncDisposable
    {
        internal const string RequestToken = "fake-actions-request-token";
        internal const string Audience = "fake-audience";
        internal const string Minted = "pypi-fake-minted-token";
        internal sealed record Request(string Method, string Path, string Query,
            string? Authorization, string Host, byte[] Body);

        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly List<Request> requests = [];
        private readonly List<Task> connections = [];
        private readonly Task loop;
        private readonly string answer;

        internal LoopbackRegistry(X509Certificate2 certificate, string answer)
        {
            this.answer = answer;
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            loop = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    TcpClient socket = await listener.AcceptTcpClientAsync(stop.Token);
                    lock (connections) connections.Add(ServeAsync(socket, certificate));
                }
            });
        }

        internal int Port { get; }

        internal Request[] Requests
        {
            get { lock (requests) return [.. requests]; }
        }

        private async Task ServeAsync(TcpClient socket, X509Certificate2 certificate)
        {
            using (socket)
            await using (var secure = new SslStream(socket.GetStream()))
            {
                try
                {
                    await secure.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = certificate,
                        ApplicationProtocols = [SslApplicationProtocol.Http11],
                    }, stop.Token);
                    while (await ReadAsync(secure) is { } request)
                    {
                        lock (requests) requests.Add(request);
                        (string status, string body, string headers) = Answer(request);
                        if (status == "reset") return;
                        byte[] content = Encoding.UTF8.GetBytes(body);
                        await secure.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 " + status +
                            "\r\nContent-Type: application/json\r\n" + headers +
                            "Content-Length: " + content.Length + "\r\n\r\n"), stop.Token);
                        await secure.WriteAsync(content, stop.Token);
                    }
                }
                catch (Exception error) when (error is IOException or
                    OperationCanceledException or System.Security.Authentication
                        .AuthenticationException) { }
            }
        }

        // The token service always succeeds; the upload answers with the row's behavior.
        private (string Status, string Body, string Headers) Answer(Request request) =>
            request.Path switch
            {
                "/_/oidc/audience" => ("200 OK", "{\"audience\":\"" + Audience + "\"}", ""),
                "/token" => ("200 OK", "{\"value\":\"fake-oidc-jwt\"}", ""),
                "/_/oidc/mint-token" => ("200 OK", "{\"success\":true,\"token\":\"" + Minted +
                    "\"}", ""),
                "/_/oidc/burn-token" => ("200 OK", "{}", ""),
                "/legacy/" => answer switch
                {
                    "accept" => ("200 OK", "OK", ""),
                    "unavailable" => ("503 Service Unavailable", "unavailable", ""),
                    "server-error" => ("500 Internal Server Error", "error", ""),
                    "existing-file" => ("400 File already exists", "400 File already " +
                        "exists. See https://pypi.org/help/#file-name-reuse", ""),
                    "reset" => ("reset", "", ""),
                    "same-realm-redirect" => ("307 Temporary Redirect", "", "Location: " +
                        "https://127.0.0.1:" + Port + "/legacy/moved/\r\n"),
                    "other-realm-redirect" => ("307 Temporary Redirect", "", "Location: " +
                        "https://localhost:" + Port + "/legacy/moved/\r\n"),
                    _ => throw new InvalidOperationException(answer),
                },
                "/legacy/moved/" => ("200 OK", "OK", ""),
                _ => ("404 Not Found", "", ""),
            };

        private async Task<Request?> ReadAsync(Stream stream)
        {
            var header = new StringBuilder();
            byte[] one = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await stream.ReadAsync(one, stop.Token) != 1) return null;
                header.Append((char)one[0]);
            }
            string[] lines = header.ToString().Split("\r\n");
            string? Field(string name) => lines.Skip(1).Where(line => line.StartsWith(
                    name + ":", StringComparison.OrdinalIgnoreCase))
                .Select(line => line[(name.Length + 1)..].Trim()).SingleOrDefault();
            string[] start = lines[0].Split(' ');
            string target = start[1];
            int query = target.IndexOf('?');
            byte[] body = new byte[int.Parse(Field("Content-Length") ?? "0",
                System.Globalization.CultureInfo.InvariantCulture)];
            await stream.ReadExactlyAsync(body, stop.Token);
            return new(start[0], query < 0 ? target : target[..query],
                query < 0 ? "" : target[(query + 1)..], Field("Authorization"),
                Field("Host") ?? "", body);
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            listener.Stop();
            try { await loop; }
            catch (Exception error) when (error is OperationCanceledException or
                SocketException or ObjectDisposedException) { }
            Task[] pending;
            lock (connections) pending = [.. connections];
            await Task.WhenAll(pending);
            stop.Dispose();
        }
    }
}
