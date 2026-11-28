using System.Reflection;
using System.Text.Json;
using WorkflowDelivery.Core;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Ecosystems;

[TestClass]
public sealed class NativeProviderBoundaryTests
{
    [TestMethod]
    public void CompiledEndpointProvidersDoNotLoadTheHostOrEachOther()
    {
        Assembly host = typeof(Program).Assembly;
        Assembly dotnet = typeof(DotNetRepositoryReader).Assembly;
        Assembly node = typeof(PnpmGraphReader).Assembly;
        Assembly python = typeof(PythonNativeGraphReader).Assembly;
        Assembly ruby = typeof(RubyNativeFactCollector).Assembly;
        Assembly[] providers = [dotnet, node, python, ruby];
        Assert.AreSame(dotnet, typeof(DotNetPackRecipe).Assembly);
        Assert.AreSame(dotnet, typeof(WindowsAssetsRecipe).Assembly);
        Assert.AreSame(dotnet, typeof(DotNetPackageConsumer).Assembly);
        Assert.AreSame(node, typeof(NpmPackRecipe).Assembly);
        Assert.AreSame(node, typeof(BrowserExtensionPackage).Assembly);
        Assert.AreSame(node, typeof(NpmPackageOperations).Assembly);
        Assert.AreSame(python, typeof(PythonPackageOperations).Assembly);
        Assert.AreSame(ruby, typeof(RubyPackageOperations).Assembly);
        Assert.HasCount(providers.Length, providers.Distinct().ToArray());
        Assert.DoesNotContain(host, providers);

        // All projects restore the repository build tooling. Only deployed dependency edges
        // matter here: a lock-file search would misclassify NBGV and SourceLink on every project.
        using JsonDocument deps = JsonDocument.Parse(File.ReadAllBytes(
            Path.ChangeExtension(host.Location, ".deps.json")));
        JsonElement target = deps.RootElement.GetProperty("targets").GetProperty(
            deps.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString()!);
        var graph = target.EnumerateObject().ToDictionary(
            item => item.Name[..item.Name.LastIndexOf('/')], item => item.Value,
            StringComparer.Ordinal);
        string[] nativeSdk = ["Microsoft.Build", "NuGet.Commands", "NuGet.ProjectModel",
            "Nerdbank.GitVersioning"];
        foreach (Assembly provider in providers)
        {
            var dependencies = new HashSet<string>(StringComparer.Ordinal);
            Visit(provider.GetName().Name!);
            Assert.DoesNotContain(host.GetName().Name!, dependencies);
            foreach (Assembly peer in providers.Where(peer => peer != provider))
                Assert.DoesNotContain(peer.GetName().Name!, dependencies);
            if (provider != dotnet)
                Assert.IsEmpty(dependencies.Where(name => nativeSdk.Any(sdk =>
                    name == sdk || name.StartsWith(sdk + ".", StringComparison.Ordinal))));

            void Visit(string name)
            {
                if (!dependencies.Add(name) || !graph[name].TryGetProperty("dependencies",
                        out JsonElement edges)) return;
                foreach (JsonProperty edge in edges.EnumerateObject()) Visit(edge.Name);
            }
        }

        string[] nativeReferences =
            [.. dotnet.GetReferencedAssemblies().Select(reference => reference.Name!)];
        string[] hostReferences =
            [.. host.GetReferencedAssemblies().Select(reference => reference.Name!)];
        foreach (string sdk in nativeSdk)
        {
            Assert.Contains(sdk, nativeReferences);
            Assert.DoesNotContain(sdk, hostReferences);
        }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("aliased")]
    [DataRow("unmeasured")]
    public void IncompleteOrAliasedOriginalsCannotBecomeAQualifiedUnit(string failure)
    {
        var wheel = new UnitOutput("wheel", "primary-package", "python-wheel");
        var sdist = new UnitOutput("sdist", "source-package", "python-sdist");
        var subject = new UnitBuildSubject(
            "rev", "pkg", "decl", "dist", "pyproject.toml", [wheel, sdist]);
        var first = new UnitOriginal(wheel, "pkg", "1.0",
            Path.Combine(AppContext.BaseDirectory, "a.whl"), 1, new string('a', 64));
        var second = first with
        {
            Output = sdist,
            Path = Path.Combine(AppContext.BaseDirectory, "b.tar.gz"),
        };
        UnitOriginal[] files = failure switch
        {
            "missing" => [first],
            "aliased" => [first, second with { Path = first.Path }],
            "unmeasured" => [first, second with { Sha256 = "" }],
            _ => throw new InvalidOperationException()
        };
        Assert.ThrowsExactly<InvalidDataException>(() => subject.Complete(files));
    }
}
