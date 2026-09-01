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
    public async Task PackagesConfigProtocolRoundTripsThroughBuiltProcess()
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
            Encoding.UTF8.GetBytes(
                """
                <packages>
                  <package id="Windows.Smoke" version="1.2.3.0" />
                </packages>
                """));
        string request = JsonSerializer.Serialize(
            new
            {
                contentBase64 = content,
                family = "nuget-packages-config",
                logicalPath = "packages.config",
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
            "nuget-packages-config-entry",
            fact.GetProperty("kind").GetString());
        Assert.AreEqual("Windows.Smoke", fact.GetProperty("id").GetString());
        Assert.AreEqual("1.2.3", fact.GetProperty("version").GetString());
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
