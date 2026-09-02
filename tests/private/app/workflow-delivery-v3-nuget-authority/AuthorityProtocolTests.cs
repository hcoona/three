using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkflowDeliveryV3NuGetAuthority.Tests;

[TestClass]
public sealed class AuthorityProtocolTests
{
    private const string RequestSchema =
        "workflow-delivery/v3/static-reference-nuget-authority-request";
    private const string ResponseSchema =
        "workflow-delivery/v3/static-reference-nuget-authority-response";

    [TestMethod]
    [DataRow(
        "nuget-packages-config",
        @"literal\component/packages.config",
        "<packages><package id=\"Backslash.Config\" version=\"1.2.3.0\" /></packages>",
        "nuget-packages-config-entry",
        "Backslash.Config",
        "1.2.3")]
    [DataRow(
        "nuget-lock",
        @"literal\component/packages.lock.json",
        "{\"version\":2,\"dependencies\":{\"net8.0\":"
            + "{\"Backslash.Lock\":{\"type\":\"Direct\","
            + "\"resolved\":\"4.5.6.0\"}}}}",
        "nuget-lock-dependency",
        "Backslash.Lock",
        "4.5.6")]
    public async Task LogicalBackslashPathsRoundTripThroughBuiltProcess(
        string family,
        string logicalPath,
        string document,
        string expectedKind,
        string expectedId,
        string expectedVersion)
    {
        string repositoryRoot = FindRepositoryRoot();
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory);
        string configuration = testOutput.Parent?.Name
            ?? throw new InvalidOperationException("Test configuration is unavailable.");
        string authorityPath = Path.Combine(
            repositoryRoot,
            "src",
            "private",
            "app",
            "workflow-delivery-v3-nuget-authority",
            "bin",
            configuration,
            testOutput.Name,
            "WorkflowDeliveryV3NuGetAuthority.dll");
        Assert.IsTrue(
            File.Exists(authorityPath),
            $"NuGet authority output is missing: {authorityPath}");
        string runtimeConfigurationPath = Path.ChangeExtension(
            authorityPath,
            ".runtimeconfig.json");
        Assert.IsTrue(
            File.Exists(runtimeConfigurationPath),
            $"NuGet authority runtime configuration is missing: {runtimeConfigurationPath}");
        using JsonDocument runtimeConfiguration = JsonDocument.Parse(
            await File.ReadAllTextAsync(runtimeConfigurationPath));
        JsonElement runtimeOptions = runtimeConfiguration.RootElement
            .GetProperty("runtimeOptions");
        Assert.AreEqual(
            "Disable",
            runtimeOptions.GetProperty("rollForward").GetString());
        JsonElement framework = runtimeOptions.GetProperty("framework");
        Assert.AreEqual(
            "Microsoft.NETCore.App",
            framework.GetProperty("name").GetString());
        Assert.AreEqual(
            "10.0.8",
            framework.GetProperty("version").GetString());

        string content = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(document));
        string request = JsonSerializer.Serialize(
            new
            {
                contentBase64 = content,
                family,
                logicalPath,
                schema = RequestSchema,
            });
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(authorityPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("NuGet authority did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(
            timeout.Token);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(
            timeout.Token);
        await process.StandardInput.WriteAsync(request.AsMemory(), timeout.Token);
        process.StandardInput.Close();
        await process.WaitForExitAsync(timeout.Token);
        string output = await outputTask;
        string error = await errorTask;

        Assert.AreEqual(0, process.ExitCode, error);
        Assert.AreEqual(string.Empty, error);
        using JsonDocument response = JsonDocument.Parse(output);
        JsonElement root = response.RootElement;
        Assert.AreEqual(ResponseSchema, root.GetProperty("schema").GetString());
        Assert.AreEqual("facts", root.GetProperty("result").GetString());
        Assert.AreEqual("nuget-lock-v1", root.GetProperty("graph").GetString());
        string?[] identities = root.GetProperty("implementationIdentities")
            .EnumerateArray()
            .Select(identity => identity.GetString())
            .ToArray();
        Assert.AreEqual(3, identities.Length);
        Assert.AreEqual("NuGet.Packaging@7.9.0", identities[0]);
        Assert.AreEqual("NuGet.ProjectModel@7.9.0", identities[1]);
        Assert.AreEqual("dotnet-runtime@10.0.8", identities[2]);

        JsonElement facts = root.GetProperty("facts");
        Assert.AreEqual(1, facts.GetArrayLength());
        JsonElement fact = facts[0];
        Assert.AreEqual(
            expectedKind,
            fact.GetProperty("kind").GetString());
        Assert.AreEqual(expectedId, fact.GetProperty("id").GetString());
        if (family == "nuget-packages-config")
        {
            Assert.AreEqual(
                expectedVersion,
                fact.GetProperty("version").GetString());
        }
        else
        {
            Assert.AreEqual("net8.0", fact.GetProperty("target").GetString());
            Assert.AreEqual(
                "Direct",
                fact.GetProperty("dependencyType").GetString());
            Assert.AreEqual(
                JsonValueKind.Null,
                fact.GetProperty("requestedRange").ValueKind);
            Assert.AreEqual(
                expectedVersion,
                fact.GetProperty("resolvedVersion").GetString());
            Assert.AreEqual(
                0,
                fact.GetProperty("dependencies").GetArrayLength());
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "dirs.proj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root is unavailable.");
    }
}
