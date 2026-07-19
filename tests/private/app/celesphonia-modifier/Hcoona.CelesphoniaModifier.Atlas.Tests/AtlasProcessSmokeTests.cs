using System.Diagnostics;
using System.Reflection;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasProcessSmokeTests
{
    private static readonly byte[] ExpectedSurvey =
        "{\"schemaVersion\":\"atlas-empty-survey/v1\",\"observations\":[]}\n"u8.ToArray();

    private static readonly byte[] ExpectedGlobalHelp =
    [
        .. "Usage:\n"u8,
        .. "  celesphonia-atlas empty-survey\n"u8,
        .. "  celesphonia-atlas intake-discover <request-file>\n"u8,
        .. "  celesphonia-atlas intake-confirm <request-file>\n"u8,
        .. "  celesphonia-atlas intake-copy <request-file>\n"u8,
        .. "  celesphonia-atlas cleanup-preflight <request-file>\n"u8,
        .. "\n"u8,
        .. "Commands:\n"u8,
        .. "  empty-survey       Write a deterministic empty Atlas survey.\n"u8,
        .. "  intake-discover    Discover the approved Atlas intake scope.\n"u8,
        .. "  intake-confirm     Confirm an approved Atlas intake manifest.\n"u8,
        .. "  intake-copy        Create qualified Atlas research snapshots.\n"u8,
        .. "  cleanup-preflight  Report private-artifact cleanup eligibility.\n"u8,
        .. "\n"u8,
        .. "Options:\n"u8,
        .. "  -h, --help  Show help.\n"u8,
    ];

    [Fact]
    public async Task EmptySurveyProcessWritesExactBytes()
    {
        ProcessResult result = await RunAsync("empty-survey");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ExpectedSurvey, result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task HelpProcessWritesExactLfBytes()
    {
        ProcessResult result = await RunAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ExpectedGlobalHelp, result.StandardOutput);
        Assert.DoesNotContain((byte)'\r', result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task DiscoveryProcessWritesExactSuccessBytes()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();

        ProcessResult result = await RunAsync(
            "intake-discover",
            workspace.Layout.CanonicalDiscoverRequestPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Intake discovery completed.\n"u8.ToArray(), result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task ApprovalProcessWritesExactDiagnostic()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        AtlasIntakeDiscoveryRequest discoveryRequest = workspace.CreateDiscoveryRequest();
        workspace.WriteRequest(discoveryRequest);
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        AtlasIntakeConfirmationRequest confirmRequest = workspace.CreateConfirmationRequest();
        workspace.WriteRequest(confirmRequest);
        File.Delete(workspace.Layout.CanonicalDiscoveredStatePath);

        ProcessResult result = await RunAsync(
            "intake-confirm",
            workspace.Layout.CanonicalConfirmRequestPath);

        Assert.Equal(6, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Equal("Approval required.\n"u8.ToArray(), result.StandardError);
    }

    [Fact]
    public async Task CorruptStateProcessWritesExactSafetyDiagnostic()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        workspace.WriteRequest(workspace.CreateConfirmationRequest());
        await File.WriteAllTextAsync(
            workspace.Layout.CanonicalDiscoveredStatePath,
            "{}",
            TestContext.Current.CancellationToken);

        ProcessResult result = await RunAsync(
            "intake-confirm",
            workspace.Layout.CanonicalConfirmRequestPath);

        Assert.Equal(5, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Equal("Safety check failed.\n"u8.ToArray(), result.StandardError);
    }

    [Fact]
    public async Task MismatchedStateProcessWritesExactSafetyDiagnostic()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        await AtlasDiscovery.DiscoverAsync(
            workspace.Layout.CanonicalDiscoverRequestPath,
            TestContext.Current.CancellationToken);
        AtlasIntakeConfirmationRequest request = workspace.CreateConfirmationRequest() with
        {
            ExpectedDiscoveredStateSha256 = new string('0', 64),
        };
        workspace.WriteRequest(request);

        ProcessResult result = await RunAsync(
            "intake-confirm",
            workspace.Layout.CanonicalConfirmRequestPath);

        Assert.Equal(5, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Equal("Safety check failed.\n"u8.ToArray(), result.StandardError);
    }

    [Fact]
    public async Task SafetyProcessWritesExactDiagnostic()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        AtlasIntakeDiscoveryRequest request = workspace.CreateDiscoveryRequest() with
        {
            WorkspaceRoot = Path.Combine(workspace.ProjectRoot, "wrong", "workspace"),
        };
        workspace.WriteRequest(request);

        ProcessResult result = await RunAsync(
            "intake-discover",
            workspace.Layout.CanonicalDiscoverRequestPath);

        Assert.Equal(5, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Equal("Safety check failed.\n"u8.ToArray(), result.StandardError);
    }

    [Fact]
    public async Task MissingRequestProcessUsesIoDiagnostic()
    {
        string missingRequestPath = Path.Combine(
            Path.GetTempPath(),
            "atlas-a2-missing-request",
            Guid.NewGuid().ToString("N"),
            "src",
            "private",
            "app",
            "celesphonia-modifier",
            ".private",
            "atlas-v0",
            AtlasSyntheticWorkspace.SurveyAlias,
            AtlasIntakeContracts.GetCanonicalRequestRelativePath("discover")
                .Replace('/', Path.DirectorySeparatorChar));
        ProcessResult result = await RunAsync("intake-discover", missingRequestPath);

        Assert.Equal(4, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Equal("I/O failure.\n"u8.ToArray(), result.StandardError);
    }

    [Fact]
    public async Task LockedValidatedRequestProcessWritesExactIoDiagnostic()
    {
        await using AtlasSyntheticWorkspace workspace = await AtlasSyntheticWorkspace.CreateAsync();
        using FileStream lockStream = new(
            workspace.Layout.CanonicalDiscoverRequestPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        ProcessResult result = await RunAsync(
            "intake-discover",
            workspace.Layout.CanonicalDiscoverRequestPath);

        Assert.Equal(4, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Equal("I/O failure.\n"u8.ToArray(), result.StandardError);
    }

    [Fact]
    public async Task InvalidProcessArgumentsUseFixedDiagnostic()
    {
        ProcessResult result = await RunAsync("--version");

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Equal("Invalid arguments.\n"u8.ToArray(), result.StandardError);
    }

    [Fact]
    public async Task WhitespaceRequestPathUsesFixedDiagnostic()
    {
        ProcessResult result = await RunAsync("intake-discover", " ");

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Equal("Invalid arguments.\n"u8.ToArray(), result.StandardError);
    }

    private static async Task<ProcessResult> RunAsync(params string[] args)
    {
        string appHostPath = GetAppHostPath();
        ProcessStartInfo startInfo = new()
        {
            FileName = appHostPath,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (string argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        Assert.True(process.Start());
        using MemoryStream standardOutput = new();
        using MemoryStream standardError = new();
        Task copyOutput = process.StandardOutput.BaseStream.CopyToAsync(standardOutput);
        Task copyError = process.StandardError.BaseStream.CopyToAsync(standardError);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(copyOutput, copyError).WaitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        return new ProcessResult(
            process.ExitCode,
            standardOutput.ToArray(),
            standardError.ToArray());
    }

    private static string GetAppHostPath()
    {
        AssemblyMetadataAttribute? metadata = typeof(AtlasProcessSmokeTests)
            .Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute =>
                StringComparer.Ordinal.Equals(attribute.Key, "AtlasCliAppHostPath"));
        Assert.NotNull(metadata);
        Assert.False(string.IsNullOrWhiteSpace(metadata.Value));
        Assert.True(File.Exists(metadata.Value), $"CLI apphost not found: {metadata.Value}");
        return metadata.Value;
    }

    private sealed record ProcessResult(
        int ExitCode,
        byte[] StandardOutput,
        byte[] StandardError);
}
