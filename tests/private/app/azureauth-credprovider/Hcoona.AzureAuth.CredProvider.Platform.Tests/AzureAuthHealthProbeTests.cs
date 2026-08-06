using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AzureAuthHealthProbeTests
{
    [Theory]
    [InlineData(AzureAuthHostPlatform.Windows)]
    [InlineData(AzureAuthHostPlatform.Wsl)]
    [InlineData(AzureAuthHostPlatform.NativeLinux)]
    public async Task ProbeUsesSelectedInstallationLaunchOptions(
        AzureAuthHostPlatform hostPlatform
    )
    {
        string executablePath = hostPlatform switch
        {
            AzureAuthHostPlatform.Windows => @"C:\Program Files\AzureAuth\azureauth.exe",
            AzureAuthHostPlatform.Wsl =>
                "/mnt/c/Users/User/AppData/Local/Programs/AzureAuth/0.9.5/azureauth.exe",
            _ => "/usr/lib/azureauth/azureauth",
        };
        AzureAuthProcessLaunchOptions launchOptions = Assert.IsType<
            AzureAuthProcessLaunchOptions
        >(
            AzureAuthProcessLaunchOptions.FromInstallation(
                AzureAuthInstallation.Available(
                    executablePath,
                    executablePath,
                    "0.9.5",
                    hostPlatform
                )
            )
        );
        var runner = new RecordingProcessRunner(new ProcessResult(0, "0.9.5", string.Empty));

        AzureAuthHealthProbeResult result = await AzureAuthHealthProbe.RunAsync(
            AzureAuthProviderConfig.CreateAzureAuth(),
            launchOptions,
            runner,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(AzureAuthHealthProbeStatus.Passed, result.Status);
        ProcessStartSpec startSpec = Assert.Single(runner.StartSpecs);
        Assert.Equal(executablePath, startSpec.FileName);
        Assert.Equal(["--version"], startSpec.Arguments);
        Assert.Equal(launchOptions.WorkingDirectory, startSpec.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(10), startSpec.Timeout);
        Assert.Equal(
            launchOptions.MaxStandardOutputBytes,
            startSpec.OutputCaptureOptions.StandardOutputByteLimit
        );
        Assert.Equal(
            launchOptions.MaxStandardErrorBytes,
            startSpec.OutputCaptureOptions.StandardErrorByteLimit
        );
        Assert.Empty(startSpec.Environment);
        Assert.Null(startSpec.StandardErrorTee);
    }

    private sealed class RecordingProcessRunner(ProcessResult result) : IProcessRunner
    {
        public List<ProcessStartSpec> StartSpecs { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartSpecs.Add(startSpec);
            return Task.FromResult(result);
        }
    }
}
