using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasProcessSmokeTests
{
    private static readonly byte[] ExpectedSurvey =
        "{\"schemaVersion\":\"atlas-empty-survey/v1\",\"observations\":[]}\n"u8.ToArray();

    private static readonly byte[] ExpectedHelp =
    [
        .. "Usage:\n"u8,
        .. "  celesphonia-atlas empty-survey\n"u8,
        .. "\n"u8,
        .. "Commands:\n"u8,
        .. "  empty-survey  Write a deterministic empty Atlas survey.\n"u8,
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
    public async Task InvalidProcessArgumentsUseFixedDiagnostic()
    {
        ProcessResult result = await RunAsync("--version");

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Equal("Invalid arguments.\n"u8.ToArray(), result.StandardError);
    }

    [Fact]
    public async Task HelpProcessWritesExactLfBytes()
    {
        ProcessResult result = await RunAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ExpectedHelp, result.StandardOutput);
        Assert.DoesNotContain((byte)'\r', result.StandardOutput);
        Assert.Empty(result.StandardError);
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
