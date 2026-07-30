using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AdapterHostBootstrapTests
{
    [Fact]
    public void ResolveInvocationPrefersMatchingProtocolEntrypoint()
    {
        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            CreateSharedDescriptor(),
            "/usr/local/bin/azureauth-credprovider",
            ["git", "credential-helper", "get"]
        );

        Assert.True(context.IsProtocolInvocation);
        Assert.Equal(["get"], context.PayloadArguments);
    }

    [Fact]
    public void ResolveInvocationFallsBackToHumanCommand()
    {
        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            CreateSharedDescriptor(),
            "/usr/local/bin/azureauth-credprovider",
            ["doctor", "--json"]
        );

        Assert.True(context.IsHumanCommandInvocation);
        Assert.Equal(["doctor", "--json"], context.PayloadArguments);
    }

    [Fact]
    public void ResolveInvocationSupportsDedicatedExecutable()
    {
        var descriptor = new AdapterDescriptor(
            "Git",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "Helper",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["git-credential-azureauth-credprovider"]
                ),
            ]
        );

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            "/usr/local/bin/git-credential-azureauth-credprovider",
            ["get"]
        );

        Assert.True(context.IsProtocolInvocation);
        Assert.Equal(["get"], context.PayloadArguments);
    }

    [Fact]
    public void ResolveInvocationUsesExactArgumentShape()
    {
        var descriptor = new AdapterDescriptor(
            "NuGet",
            AdapterProtocol.NuGetPlugin,
            [
                new AdapterEntrypointDescriptor(
                    "Plugin",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["-Plugin"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact
                ),
            ]
        );

        Assert.True(
            AdapterHostBootstrap.TryResolveInvocation(
                descriptor,
                "azureauth-credprovider",
                ["-Plugin"],
                out _
            )
        );
        Assert.False(
            AdapterHostBootstrap.TryResolveInvocation(
                descriptor,
                "azureauth-credprovider",
                ["-Plugin", "extra"],
                out _
            )
        );
    }

    [Fact]
    public void TryResolveInvocationReturnsFalseForUnknownExecutable()
    {
        Assert.False(
            AdapterHostBootstrap.TryResolveInvocation(
                CreateSharedDescriptor(),
                "/usr/local/bin/other-tool",
                ["git", "credential-helper", "get"],
                out AdapterInvocationContext? context
            )
        );
        Assert.Null(context);
    }

    [Fact]
    public void ResolveInvocationRejectsMissingMatch()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AdapterHostBootstrap.ResolveInvocation(
                CreateSharedDescriptor(),
                "/usr/local/bin/other-tool",
                []
            )
        );
    }

    [Fact]
    public void ConstructorRejectsUnconstrainedEntrypoint()
    {
        Assert.Throws<ArgumentException>(() =>
            new AdapterEntrypointDescriptor("Unconstrained", AdapterInvocationMode.Protocol)
        );
    }

    [Fact]
    public void ConstructorRejectsTokensWithAnyMatching()
    {
        Assert.Throws<ArgumentException>(() =>
            new AdapterEntrypointDescriptor(
                "Invalid",
                AdapterInvocationMode.Protocol,
                executableNames: ["tool"],
                argumentTokens: ["token"]
            )
        );
    }

    private static AdapterDescriptor CreateSharedDescriptor()
    {
        return new AdapterDescriptor(
            "Git",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "Protocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["git", "credential-helper"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix
                ),
                new AdapterEntrypointDescriptor(
                    "Human",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["azureauth-credprovider"]
                ),
            ]
        );
    }
}
