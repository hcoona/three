using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AdapterHostBootstrapTests
{
    [Fact]
    public void ResolveInvocationPrefersProtocolEntrypointRegardlessOfDeclarationOrder()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(humanCommandFirst: true);

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"]);

        Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
        Assert.Equal(AdapterInvocationMode.Protocol, context.Mode);
        Assert.True(context.IsProtocolInvocation);
        Assert.False(context.IsHumanCommandInvocation);
        Assert.Equal("GitCredentialHelper", context.Entrypoint.Name);
        Assert.Equal("azureauth-credprovider", context.ExecutableName);
        Assert.Equal(["git", "credential-helper", "get"], context.RawArguments);
        Assert.Equal(["git", "credential-helper"], context.MatchedArguments);
        Assert.Equal(["get"], context.PayloadArguments);
    }

    [Fact]
    public void ResolveInvocationFallsBackToHumanCommandEntrypointForSharedExecutable()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"]);

        Assert.Equal(AdapterProtocol.Unspecified, context.Protocol);
        Assert.Equal([AdapterProtocol.GitCredentialHelper], context.Descriptor.SupportedProtocols);
        Assert.Equal(AdapterInvocationMode.HumanCommand, context.Mode);
        Assert.True(context.IsHumanCommandInvocation);
        Assert.False(context.IsProtocolInvocation);
        Assert.Equal("HumanCommand", context.Entrypoint.Name);
        Assert.Empty(context.MatchedArguments);
        Assert.Equal(["doctor", "--json"], context.PayloadArguments);
    }

    [Fact]
    public void ConstructorRejectsExecutableOnlyProtocolEntrypointSubsumingHumanExactBoundary()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Shared Host",
                AdapterProtocol.GitCredentialHelper,
                [
                    new AdapterEntrypointDescriptor(
                        "ProtocolAny",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool"]),
                    new AdapterEntrypointDescriptor(
                        "HumanDoctor",
                        AdapterInvocationMode.HumanCommand,
                        executableNames: ["tool"],
                        argumentTokens: ["doctor"],
                        argumentMatchMode: AdapterArgumentMatchMode.Exact),
                ]));

        Assert.Contains(
            "must not subsume human command",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProtocolAny", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HumanDoctor", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void
        ConstructorAllowsExecutableOnlyProtocolOverlapWhenHumanAliasRemainsReachable()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "ProtocolAny",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"]),
                new AdapterEntrypointDescriptor(
                    "HumanDoctor",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["tool", "alias"],
                    argumentTokens: ["doctor"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);

        AdapterInvocationContext aliasContext = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/alias",
            arguments: ["doctor"]);

        Assert.Equal("HumanDoctor", aliasContext.Entrypoint.Name);
        Assert.Equal(AdapterInvocationMode.HumanCommand, aliasContext.Mode);
        Assert.Equal(AdapterProtocol.Unspecified, aliasContext.Protocol);
        Assert.Equal(["doctor"], aliasContext.MatchedArguments);
        Assert.Empty(aliasContext.PayloadArguments);

        AdapterInvocationContext toolContext = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/tool",
            arguments: ["doctor"]);

        Assert.Equal("ProtocolAny", toolContext.Entrypoint.Name);
        Assert.Equal(AdapterInvocationMode.Protocol, toolContext.Mode);
        Assert.Equal(AdapterProtocol.GitCredentialHelper, toolContext.Protocol);
        Assert.Empty(toolContext.MatchedArguments);
        Assert.Equal(["doctor"], toolContext.PayloadArguments);
    }

    [Fact]
    public void
        ConstructorRejectsExecutableOnlyProtocolUnionSubsumingHumanExactBoundaryAcrossExecutables()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Shared Host",
                AdapterProtocol.GitCredentialHelper,
                [
                    new AdapterEntrypointDescriptor(
                        "ToolProtocolAny",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool"]),
                    new AdapterEntrypointDescriptor(
                        "AliasProtocolAny",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["alias"]),
                    new AdapterEntrypointDescriptor(
                        "HumanDoctor",
                        AdapterInvocationMode.HumanCommand,
                        executableNames: ["tool", "alias"],
                        argumentTokens: ["doctor"],
                        argumentMatchMode: AdapterArgumentMatchMode.Exact),
                ]));

        Assert.Contains(
            "must not subsume human command",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ToolProtocolAny", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AliasProtocolAny", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HumanDoctor", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsProtocolPrefixSubsumingHumanExactWithSameTokens()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Shared Host",
                AdapterProtocol.GitCredentialHelper,
                [
                    new AdapterEntrypointDescriptor(
                        "GitProtocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool"],
                        argumentTokens: ["git", "credential-helper"],
                        argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                    new AdapterEntrypointDescriptor(
                        "HumanGitCommand",
                        AdapterInvocationMode.HumanCommand,
                        executableNames: ["tool"],
                        argumentTokens: ["git", "credential-helper"],
                        argumentMatchMode: AdapterArgumentMatchMode.Exact),
                ]));

        Assert.Contains(
            "must not subsume human command",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GitProtocol", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HumanGitCommand", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsProtocolPrefixSubsumingHumanExactOnExecutableSubset()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Shared Host",
                AdapterProtocol.GitCredentialHelper,
                [
                    new AdapterEntrypointDescriptor(
                        "GitProtocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool", "alias"],
                        argumentTokens: ["git", "credential-helper"],
                        argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                    new AdapterEntrypointDescriptor(
                        "HumanGitCommand",
                        AdapterInvocationMode.HumanCommand,
                        executableNames: ["tool"],
                        argumentTokens: ["git", "credential-helper"],
                        argumentMatchMode: AdapterArgumentMatchMode.Exact),
                ]));

        Assert.Contains(
            "must not subsume human command",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GitProtocol", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HumanGitCommand", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsHumanExactBoundarySubsumedByProtocolUnionAcrossExecutables()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Shared Host",
                AdapterProtocol.GitCredentialHelper,
                [
                    new AdapterEntrypointDescriptor(
                        "ToolGitProtocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool"],
                        argumentTokens: ["git", "credential-helper"],
                        argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                    new AdapterEntrypointDescriptor(
                        "AliasGitProtocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["alias"],
                        argumentTokens: ["git", "credential-helper"],
                        argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                    new AdapterEntrypointDescriptor(
                        "HumanGitCommand",
                        AdapterInvocationMode.HumanCommand,
                        executableNames: ["tool", "alias"],
                        argumentTokens: ["git", "credential-helper"],
                        argumentMatchMode: AdapterArgumentMatchMode.Exact),
                ]));

        Assert.Contains(
            "must not subsume human command",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ToolGitProtocol", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AliasGitProtocol", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HumanGitCommand", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsHumanExactBoundaryCoveredByProtocolExactUnderflow()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Shared Host",
                AdapterProtocol.GitCredentialHelper,
                [
                    new AdapterEntrypointDescriptor(
                        "GitProtocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool"],
                        argumentTokens: ["git", "credential-helper"],
                        argumentMatchMode: AdapterArgumentMatchMode.Exact),
                    new AdapterEntrypointDescriptor(
                        "HumanGitCommand",
                        AdapterInvocationMode.HumanCommand,
                        executableNames: ["tool"],
                        argumentTokens: ["git"],
                        argumentMatchMode: AdapterArgumentMatchMode.Exact),
                ]));

        Assert.Contains(
            "must not subsume human command",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GitProtocol", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HumanGitCommand", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsHumanPrefixBoundaryCoveredByProtocolExactOverflow()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Shared Host",
                AdapterProtocol.GitCredentialHelper,
                [
                    new AdapterEntrypointDescriptor(
                        "GitProtocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool"],
                        argumentTokens: ["git"],
                        argumentMatchMode: AdapterArgumentMatchMode.Exact),
                    new AdapterEntrypointDescriptor(
                        "HumanGitCommand",
                        AdapterInvocationMode.HumanCommand,
                        executableNames: ["tool"],
                        argumentTokens: ["git", "credential-helper"],
                        argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                ]));

        Assert.Contains(
            "must not subsume human command",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GitProtocol", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HumanGitCommand", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsHumanExactBoundaryCoveredByProtocolContainsAllPartialMatch()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "NuGet Plugin",
                AdapterProtocol.NuGetPlugin,
                [
                    new AdapterEntrypointDescriptor(
                        "PluginProtocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool"],
                        argumentTokens: ["-Plugin", "-Uri"],
                        argumentMatchMode: AdapterArgumentMatchMode.ContainsAll),
                    new AdapterEntrypointDescriptor(
                        "HumanPluginCommand",
                        AdapterInvocationMode.HumanCommand,
                        executableNames: ["tool"],
                        argumentTokens: ["-Plugin"],
                        argumentMatchMode: AdapterArgumentMatchMode.Exact),
                ]));

        Assert.Contains(
            "must not subsume human command",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PluginProtocol", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HumanPluginCommand", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsHumanExactBoundaryCoveredByProtocolUnionAcrossExecutables()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Shared Host",
                AdapterProtocol.GitCredentialHelper,
                [
                    new AdapterEntrypointDescriptor(
                        "ToolGitProtocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool"],
                        argumentTokens: ["git", "credential-helper"],
                        argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                    new AdapterEntrypointDescriptor(
                        "AliasGitProtocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["alias"],
                        argumentTokens: ["git", "credential-helper"],
                        argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                    new AdapterEntrypointDescriptor(
                        "HumanGitCommand",
                        AdapterInvocationMode.HumanCommand,
                        executableNames: ["tool", "alias"],
                        argumentTokens: ["git"],
                        argumentMatchMode: AdapterArgumentMatchMode.Exact),
                ]));

        Assert.Contains(
            "must not subsume human command",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ToolGitProtocol", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AliasGitProtocol", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HumanGitCommand", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsExecutableOnlyProtocolEntrypointSubsumingHumanExecutableSubset()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Shared Host",
                AdapterProtocol.GitCredentialHelper,
                [
                    new AdapterEntrypointDescriptor(
                        "ToolOrAliasProtocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool", "alias"]),
                    new AdapterEntrypointDescriptor(
                        "ToolHumanCommand",
                        AdapterInvocationMode.HumanCommand,
                        executableNames: ["tool"]),
                ]));

        Assert.Contains(
            "must not subsume human command",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ToolOrAliasProtocol", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ToolHumanCommand", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveInvocationStillAllowsHumanCommandWhenArgumentsAreNotProtocolLike()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "GitProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"],
                    argumentTokens: ["git", "credential-helper"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "HumanDoctor",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["tool"],
                    argumentTokens: ["doctor"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/tool",
            arguments: ["doctor"]);

        Assert.Equal("HumanDoctor", context.Entrypoint.Name);
        Assert.Equal(AdapterInvocationMode.HumanCommand, context.Mode);
        Assert.Equal(AdapterProtocol.Unspecified, context.Protocol);
        Assert.Equal(["doctor"], context.MatchedArguments);
        Assert.Empty(context.PayloadArguments);
    }

    [Fact]
    public void TryResolveInvocationDoesNotFallBackToHumanCommandForPartialProtocolPrefix()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void ConstructorRejectsHumanExactBoundaryCoveredByProtocolPrefixUnderflow()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Shared Host",
                AdapterProtocol.GitCredentialHelper,
                [
                    new AdapterEntrypointDescriptor(
                        "GitProtocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["azureauth-credprovider"],
                        argumentTokens: ["git", "credential-helper"],
                        argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                    new AdapterEntrypointDescriptor(
                        "HumanGitCommand",
                        AdapterInvocationMode.HumanCommand,
                        executableNames: ["azureauth-credprovider"],
                        argumentTokens: ["git"],
                        argumentMatchMode: AdapterArgumentMatchMode.Exact),
                ]));

        Assert.Contains(
            "must not subsume human command",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GitProtocol", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HumanGitCommand", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorAllowsHumanPrefixBoundaryWhenProtocolOnlyCoversUnderflowSlice()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "GitProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["git", "credential-helper"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "HumanGitCommand",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["git"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
            ]);

        Assert.True(descriptor.SupportsHumanCommandMode);
        Assert.True(descriptor.SupportsProtocolMode);
    }

    [Fact]
    public void TryResolveRejectsArgOnlyHumanBoundaryForProtocolPrefixUnderflowWithNullPath()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "GitProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["git", "credential-helper"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "HumanGitCommand",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["git"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: null,
            arguments: ["git"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void TryResolveRejectsArgOnlyHumanBoundaryForProtocolFullMatchWithNullPath()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "GitProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["git", "credential-helper"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "HumanGitCredentialHelperCommand",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["git", "credential-helper"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: null,
            arguments: ["git", "credential-helper"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void TryResolveRejectsArgOnlyHumanBoundaryForExecutableOnlyProtocolWithNullPath()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "ProtocolAny",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"]),
                new AdapterEntrypointDescriptor(
                    "HumanDoctor",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["doctor"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: null,
            arguments: ["doctor"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void
        ResolveInvocationThrowsForExecutableOnlyProtocolConflictAtArgOnlyHumanBoundaryWithNullPath()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "ProtocolAny",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"]),
                new AdapterEntrypointDescriptor(
                    "HumanDoctor",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["doctor"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AdapterHostBootstrap.ResolveInvocation(
                descriptor,
                executablePath: null,
                arguments: ["doctor"]));

        Assert.Contains("Shared Host", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "does not match the current invocation boundary",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolveAcceptsArgOnlyHumanBoundaryWithNullPathWhenArgsAreProtocolDisjoint()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "GitProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["git", "credential-helper"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "HumanDoctorCommand",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["doctor"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: null,
            arguments: ["doctor"],
            out AdapterInvocationContext? context);

        Assert.True(matched);
        Assert.NotNull(context);
        Assert.Equal("HumanDoctorCommand", context.Entrypoint.Name);
        Assert.Equal(AdapterInvocationMode.HumanCommand, context.Mode);
        Assert.Equal(AdapterProtocol.Unspecified, context.Protocol);
        Assert.Null(context.ExecutablePath);
        Assert.Null(context.ExecutableName);
        Assert.Equal(["doctor"], context.MatchedArguments);
        Assert.Empty(context.PayloadArguments);
    }

    [Fact]
    public void TryResolveRejectsArgOnlyHumanBoundaryForExplicitBoundaryOnlyPath()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "GitProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["git", "credential-helper"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "HumanDoctorCommand",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["doctor"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/",
            arguments: ["doctor"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void TryResolveRejectsArgOnlyHumanBoundaryForExplicitWindowsUncShareRoot()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "GitProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["git", "credential-helper"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "HumanDoctorCommand",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["doctor"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);

        if (!OperatingSystem.IsWindows())
        {
            AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
                descriptor,
                executablePath: @"\\server\share",
                arguments: ["doctor"]);

            Assert.Equal("HumanDoctorCommand", context.Entrypoint.Name);
            Assert.Equal(AdapterInvocationMode.HumanCommand, context.Mode);
            Assert.Equal(AdapterProtocol.Unspecified, context.Protocol);
            return;
        }

        AssertNoInvocation(
            descriptor,
            executablePath: @"\\server\share",
            "doctor");
    }

    [Fact]
    public void TryResolveInvocationDoesNotAcceptHumanFallbackForExactProtocolBoundaryOverflow()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.NuGetPlugin,
            [
                new AdapterEntrypointDescriptor(
                    "ExactProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["proto"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
                new AdapterEntrypointDescriptor(
                    "HumanCommand",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["azureauth-credprovider"]),
            ]);

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["proto", "extra"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void ResolveInvocationMatchesWindowsSharedExecutableCaseAndExtensionVariants()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(humanCommandFirst: true);

        if (!OperatingSystem.IsWindows())
        {
            AssertNoInvocation(
                descriptor,
                executablePath: @"C:\Program Files\Azure Auth\AZUREAUTH-CREDPROVIDER.EXE",
                "git",
                "credential-helper",
                "get");
            return;
        }

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: @"C:\Program Files\Azure Auth\AZUREAUTH-CREDPROVIDER.EXE",
            arguments: ["git", "credential-helper", "get"]);

        Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
        Assert.Equal(AdapterInvocationMode.Protocol, context.Mode);
        Assert.Equal("AZUREAUTH-CREDPROVIDER.EXE", context.ExecutableName);
        Assert.Equal(["git", "credential-helper"], context.MatchedArguments);
        Assert.Equal(["get"], context.PayloadArguments);
    }

    [Fact]
    public void TryResolveInvocationDoesNotStripMixedCaseExeSuffixOnUnixSemantics()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(
            sharedExecutableName: "azureauth-credprovider.exe");

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider.ExE",
            arguments: ["git", "credential-helper", "get"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void TryResolveInvocationDoesNotUseWindowsCaseSensitivityForExeNormalizationOnUnixPaths()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(
            sharedExecutableName: "AZUREAUTH-CREDPROVIDER.EXE");

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void TryResolveInvocationDoesNotFallBackToWindowsSemanticsForExplicitUnixPaths()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(
            sharedExecutableName: "azureauth-credprovider.exe");

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/AZUREAUTH-CREDPROVIDER",
            arguments: ["git", "credential-helper", "get"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void ResolveInvocationMatchesBareExecutableAgainstExeShapeOnlyOnWindows()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(
            sharedExecutableName: "azureauth-credprovider.exe");

        if (!OperatingSystem.IsWindows())
        {
            AssertNoInvocation(
                descriptor,
                executablePath: "azureauth-credprovider",
                "git",
                "credential-helper",
                "get");
            return;
        }

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: "azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"]);

        Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
        Assert.Equal(AdapterInvocationMode.Protocol, context.Mode);
        Assert.Equal("azureauth-credprovider", context.ExecutableName);
        Assert.Equal("GitCredentialHelper", context.Entrypoint.Name);
    }

    [Fact]
    public void TryResolveInvocationDoesNotMatchUnixBareExecutableAgainstExeShape()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(
            sharedExecutableName: "azureauth-credprovider.exe");

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void ResolveInvocationMatchesBareExecutableAgainstExeEntrypointShapeOnWindows()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(
            sharedExecutableName: "AZUREAUTH-CREDPROVIDER.EXE");

        if (!OperatingSystem.IsWindows())
        {
            AssertNoInvocation(
                descriptor,
                executablePath: @"C:\Program Files\Azure Auth\azureauth-credprovider",
                "git",
                "credential-helper",
                "get");
            return;
        }

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: @"C:\Program Files\Azure Auth\azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"]);

        Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
        Assert.Equal(AdapterInvocationMode.Protocol, context.Mode);
        Assert.Equal("GitCredentialHelper", context.Entrypoint.Name);
    }

    [Fact]
    public void ResolveInvocationExtractsBasenameFromWindowsDriveRelativeExecutablePath()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(
            sharedExecutableName: "azureauth-credprovider.exe");

        if (!OperatingSystem.IsWindows())
        {
            AssertNoInvocation(
                descriptor,
                executablePath: @"C:azureauth-credprovider.exe",
                "git",
                "credential-helper",
                "get");
            return;
        }

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: @"C:azureauth-credprovider.exe",
            arguments: ["git", "credential-helper", "get"]);

        Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
        Assert.Equal(AdapterInvocationMode.Protocol, context.Mode);
        Assert.Equal("azureauth-credprovider.exe", context.ExecutableName);
        Assert.Equal("GitCredentialHelper", context.Entrypoint.Name);
    }

    [Fact]
    public void TryResolveInvocationDoesNotTreatWindowsDriveRelativeBareNameAsAliasOnNonWindows()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(sharedExecutableName: "foo");

        if (OperatingSystem.IsWindows())
        {
            AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
                descriptor,
                executablePath: @"C:foo",
                arguments: ["git", "credential-helper", "get"]);

            Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
            Assert.Equal(AdapterInvocationMode.Protocol, context.Mode);
            Assert.Equal("foo", context.ExecutableName);
            Assert.Equal("GitCredentialHelper", context.Entrypoint.Name);
            return;
        }

        AssertNoInvocation(
            descriptor,
            executablePath: @"C:foo",
            "git",
            "credential-helper",
            "get");
    }

    [Fact]
    public void TryResolveInvocationDoesNotTreatUncLikeBareNameAsExecutableAliasOnNonWindows()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(sharedExecutableName: "foo");

        if (OperatingSystem.IsWindows())
        {
            AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
                descriptor,
                executablePath: @"\\server\share\foo",
                arguments: ["git", "credential-helper", "get"]);

            Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
            Assert.Equal(AdapterInvocationMode.Protocol, context.Mode);
            Assert.Equal("foo", context.ExecutableName);
            Assert.Equal("GitCredentialHelper", context.Entrypoint.Name);
            return;
        }

        AssertNoInvocation(
            descriptor,
            executablePath: @"\\server\share\foo",
            "git",
            "credential-helper",
            "get");
    }

    [Fact]
    public void ResolveInvocationTreatsForwardSlashUncPathAsWindowsPathShape()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();

        if (!OperatingSystem.IsWindows())
        {
            AssertNoInvocation(
                descriptor,
                executablePath: "//server/share/AZUREAUTH-CREDPROVIDER.EXE",
                "git",
                "credential-helper",
                "get");
            return;
        }

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: "//server/share/AZUREAUTH-CREDPROVIDER.EXE",
            arguments: ["git", "credential-helper", "get"]);

        Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
        Assert.Equal(AdapterInvocationMode.Protocol, context.Mode);
        Assert.Equal("AZUREAUTH-CREDPROVIDER.EXE", context.ExecutableName);
        Assert.Equal("GitCredentialHelper", context.Entrypoint.Name);
    }

    [Fact]
    public void TryResolveInvocationDoesNotTreatForwardSlashUncShareRootAsExecutableNameOnWindows()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(sharedExecutableName: "share");

        if (OperatingSystem.IsWindows())
        {
            AssertNoInvocation(
                descriptor,
                executablePath: "//server/share",
                "git",
                "credential-helper",
                "get");
            AssertNoInvocation(
                descriptor,
                executablePath: "//server/share/",
                "git",
                "credential-helper",
                "get");
            return;
        }

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: "//server/share",
            arguments: ["git", "credential-helper", "get"]);

        Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
        Assert.Equal(AdapterInvocationMode.Protocol, context.Mode);
        Assert.Equal("share", context.ExecutableName);
        Assert.Equal("GitCredentialHelper", context.Entrypoint.Name);

        AssertNoInvocation(
            descriptor,
            executablePath: "//server/share/",
            "git",
            "credential-helper",
            "get");
    }

    [Fact]
    public void TryResolveInvocationDoesNotTreatBackslashUncShareRootAsExecutableNameOnWindows()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(sharedExecutableName: "share");

        AssertNoInvocation(
            descriptor,
            executablePath: @"\\server\share",
            "git",
            "credential-helper",
            "get");
        AssertNoInvocation(
            descriptor,
            executablePath: "\\\\server\\share\\",
            "git",
            "credential-helper",
            "get");
    }

    [Fact]
    public void
        TryResolveInvocationDoesNotTreatWindowsDeviceBoundaryOnlyPathsAsExecutableNameOnWindows()
    {
        const string volumeName = "Volume{11111111-1111-1111-1111-111111111111}";

        foreach ((string executablePath, string sharedExecutableName) in new[]
        {
            (@"\\?\C:", "C:"),
            (@"\\.\C:", "C:"),
            ($@"\\?\{volumeName}", volumeName),
            ($@"\\.\{volumeName}", volumeName),
            (@"\\?\UNC", "UNC"),
            (@"\\.\UNC", "UNC"),
        })
        {
            AdapterDescriptor descriptor = CreateSharedGitDescriptor(
                sharedExecutableName: sharedExecutableName);

            AssertNoInvocation(
                descriptor,
                executablePath: executablePath,
                "git",
                "credential-helper",
                "get");
        }
    }

    [Fact]
    public void
        TryResolveInvocationRejectsNonFileBackedWindowsDeviceNamespaceRootsAsExecutableNames()
    {
        foreach ((string executablePath, string sharedExecutableName) in new[]
        {
            (@"\\.\PhysicalDrive0", "PhysicalDrive0"),
            (@"\\.\pipe", "pipe"),
            (@"\\?\GLOBALROOT", "GLOBALROOT"),
            (@"\\?\foo", "foo"),
        })
        {
            AdapterDescriptor descriptor = CreateSharedGitDescriptor(
                sharedExecutableName: sharedExecutableName);

            AssertNoInvocation(
                descriptor,
                executablePath: executablePath,
                "git",
                "credential-helper",
                "get");
        }
    }

    [Fact]
    public void
        TryResolveInvocationRejectsNonFileBackedWindowsDeviceNamespaceChildPathsAsExecutableNames()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();

        foreach (string executablePath in new[]
        {
            @"\\.\PIPE\azureauth-credprovider",
            @"\\?\GLOBALROOT\Device\NamedPipe\azureauth-credprovider",
            @"\\?\foo\azureauth-credprovider",
        })
        {
            AssertNoInvocation(
                descriptor,
                executablePath: executablePath,
                "git",
                "credential-helper",
                "get");
        }
    }

    [Fact]
    public void
        TryResolveInvocationRejectsDoubleLeadingRawWindowsNtAliasLookalikesAsExecutableNames()
    {
        const string volumeName = "Volume{11111111-1111-1111-1111-111111111111}";
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();

        foreach (string executablePath in new[]
        {
            @"\\??\UNC\server\share\azureauth-credprovider.exe",
            "//??/UNC/server/share/azureauth-credprovider.exe",
            $@"\\??\{volumeName}\azureauth-credprovider.exe",
            $"//??/{volumeName}/azureauth-credprovider.exe",
            @"\\DosDevices\C:\dir\azureauth-credprovider.exe",
            "//DosDevices/C:/dir/azureauth-credprovider.exe",
            $@"\\DosDevices\{volumeName}\azureauth-credprovider.exe",
            $"//DosDevices/{volumeName}/azureauth-credprovider.exe",
            @"\\Global??\UNC\server\share\azureauth-credprovider.exe",
            "//Global??/UNC/server/share/azureauth-credprovider.exe",
            $@"\\Global??\{volumeName}\azureauth-credprovider.exe",
            $"//Global??/{volumeName}/azureauth-credprovider.exe",
        })
        {
            AssertNoInvocation(
                descriptor,
                executablePath,
                "git",
                "credential-helper",
                "get");
        }
    }

    [Fact]
    public void
        TryResolveInvocationDoesNotTreatRawWindowsNtNamespacePathsAsExecutableNameOnWindows()
    {
        foreach ((string executablePath, string sharedExecutableName) in new[]
        {
            (@"\??\C:", "C:"),
            (@"\??\UNC\server\share", "share"),
            (@"\DosDevices\C:", "C:"),
            (@"\Global??\UNC\server", "server"),
            (@"\Global??\UNC\server\share", "share"),
            (@"\Device\HarddiskVolume1", "HarddiskVolume1"),
            (@"\Device\NamedPipe\azureauth-credprovider", "azureauth-credprovider"),
        })
        {
            AdapterDescriptor descriptor = CreateSharedGitDescriptor(
                sharedExecutableName: sharedExecutableName);

            AssertNoInvocation(
                descriptor,
                executablePath: executablePath,
                "git",
                "credential-helper",
                "get");
        }
    }

    [Fact]
    public void
        TryResolveInvocationRejectsKnownNonFileBackedRawWindowsObjectManagerRootsAsExecutableNames()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();

        foreach (string executablePath in new[]
        {
            @"\KnownDlls\azureauth-credprovider.exe",
            @"\BaseNamedObjects\azureauth-credprovider.exe",
            @"\Registry\Machine\AzureAuth\azureauth-credprovider.exe",
            @"\RPC Control\azureauth-credprovider.exe",
        })
        {
            AssertNoInvocation(
                descriptor,
                executablePath,
                "git",
                "credential-helper",
                "get");
        }
    }

    [Fact]
    public void
        TryResolveInvocationRejectsWindowsPathsWithEmptyChildSegmentsAsExecutableNames()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();

        foreach (string executablePath in new[]
        {
            @"\\server\share\\AZUREAUTH-CREDPROVIDER.EXE",
            @"C:\dir\\AZUREAUTH-CREDPROVIDER.EXE",
            @"\\?\C:\dir\\AZUREAUTH-CREDPROVIDER.EXE",
            @"\DosDevices\C:\dir\\AZUREAUTH-CREDPROVIDER.EXE",
        })
        {
            AssertNoInvocation(
                descriptor,
                executablePath,
                "git",
                "credential-helper",
                "get");
        }
    }

    [Fact]
    public void TryResolveInvocationDoesNotTreatWindowsUncIpcSharesAsExecutableNameOnWindows()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();

        foreach (string executablePath in new[]
        {
            @"\\server\pipe\AZUREAUTH-CREDPROVIDER.EXE",
            @"\\server\IPC$\AZUREAUTH-CREDPROVIDER.EXE",
            "//server/mailslot/AZUREAUTH-CREDPROVIDER.EXE",
            "//server/IPC$/AZUREAUTH-CREDPROVIDER.EXE",
            @"\\?\UNC\server\pipe\AZUREAUTH-CREDPROVIDER.EXE",
            @"\\?\UNC\server\IPC$\AZUREAUTH-CREDPROVIDER.EXE",
            "//?/UNC/server/mailslot/AZUREAUTH-CREDPROVIDER.EXE",
            "//?/UNC/server/IPC$/AZUREAUTH-CREDPROVIDER.EXE",
            @"\Global??\UNC\server\pipe\AZUREAUTH-CREDPROVIDER.EXE",
            @"\Global??\UNC\server\IPC$\AZUREAUTH-CREDPROVIDER.EXE",
            @"\??\UNC\server\mailslot\AZUREAUTH-CREDPROVIDER.EXE",
            @"\??\UNC\server\IPC$\AZUREAUTH-CREDPROVIDER.EXE",
        })
        {
            AssertNoInvocation(
                descriptor,
                executablePath,
                "git",
                "credential-helper",
                "get");
        }
    }

    [Fact]
    public void
        TryResolveInvocationRejectsUncIpcShareTrailingDotOrSpaceOnWindows()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();

        foreach (string shareName in new[]
        {
            "pipe.",
            "pipe ",
            "mailslot.",
            "mailslot ",
            "IPC$.",
            "IPC$ ",
        })
        {
            foreach (string executablePath in CreateWindowsUncExecutablePathVariants(
                        "server",
                        shareName,
                        "AZUREAUTH-CREDPROVIDER.EXE"))
            {
                AssertNoInvocation(
                    descriptor,
                    executablePath,
                    "git",
                    "credential-helper",
                    "get");
            }
        }
    }

    [Fact]
    public void
        TryResolveInvocationRejectsAliasReservedShareTrailingDotOrSpaceExecutableNames()
    {
        const string volumeName = "Volume{11111111-1111-1111-1111-111111111111}";
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();

        foreach ((string shareComponent, string[] childPathSegments) in new (string, string[])[]
        {
            ("UNC.", ["server", "share", "AZUREAUTH-CREDPROVIDER.EXE"]),
            ("UNC ", ["server", "share", "AZUREAUTH-CREDPROVIDER.EXE"]),
            ($"{volumeName}.", ["AZUREAUTH-CREDPROVIDER.EXE"]),
            ($"{volumeName} ", ["AZUREAUTH-CREDPROVIDER.EXE"]),
        })
        {
            foreach (string executablePath in
                        CreateDosDevicesReservedShareLookalikeExecutablePathVariants(
                            shareComponent,
                            childPathSegments))
            {
                AssertNoInvocation(
                    descriptor,
                    executablePath,
                    "git",
                    "credential-helper",
                    "get");
            }
        }
    }

    [Fact]
    public void GetExecutableNameTreatsWindowsUncServerRootsAsBoundaryOnly()
    {
        foreach (string executablePath in new[] { @"\\server", "//server" })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsWindowsDeviceUncShareRootsAsBoundaryOnly()
    {
        foreach (string executablePath in new[]
        {
            @"\\?\UNC\server\share",
            @"\\.\UNC\server\share",
            "//?/UNC/server/share",
            "//./UNC/server/share",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsWindowsDeviceBoundaryOnlyPathsAsBoundaryOnly()
    {
        const string volumeName = "Volume{11111111-1111-1111-1111-111111111111}";

        foreach (string executablePath in new[]
        {
            @"\\?\C:",
            @"\\.\C:",
            "//?/C:",
            "//./C:",
            $@"\\?\{volumeName}",
            $@"\\.\{volumeName}",
            $"//?/{volumeName}",
            $"//./{volumeName}",
            @"\\?\UNC",
            @"\\.\UNC",
            "//?/UNC",
            "//./UNC",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsNonFileBackedWindowsDeviceNamespacesAsBoundaryOnly()
    {
        foreach (string executablePath in new[]
        {
            @"\\.\PhysicalDrive0",
            "//./PhysicalDrive0",
            @"\\.\pipe",
            "//./pipe",
            @"\\?\GLOBALROOT",
            "//?/GLOBALROOT",
            @"\\?\foo",
            "//?/foo",
            @"\\.\PIPE\azureauth-credprovider",
            "//./PIPE/azureauth-credprovider",
            @"\\?\GLOBALROOT\Device\NamedPipe\azureauth-credprovider",
            "//?/GLOBALROOT/Device/NamedPipe/azureauth-credprovider",
            @"\\?\foo\azureauth-credprovider",
            "//?/foo/azureauth-credprovider",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsDoubleLeadingRawWindowsNtAliasLookalikesAsInvalidOnWindows()
    {
        const string volumeName = "Volume{11111111-1111-1111-1111-111111111111}";

        foreach (string executablePath in new[]
        {
            @"\\??\UNC\server\share\tool.exe",
            "//??/UNC/server/share/tool.exe",
            $@"\\??\{volumeName}\tool.exe",
            $"//??/{volumeName}/tool.exe",
            @"\\DosDevices\C:\dir\tool.exe",
            "//DosDevices/C:/dir/tool.exe",
            $@"\\DosDevices\{volumeName}\tool.exe",
            $"//DosDevices/{volumeName}/tool.exe",
            @"\\Global??\UNC\server\share\tool.exe",
            "//Global??/UNC/server/share/tool.exe",
            $@"\\Global??\{volumeName}\tool.exe",
            $"//Global??/{volumeName}/tool.exe",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void
        GetExecutableNameRejectsAliasReservedShareTrailingDotOrSpaceOnWindows()
    {
        const string volumeName = "Volume{11111111-1111-1111-1111-111111111111}";

        foreach ((string shareComponent, string[] childPathSegments) in new (string, string[])[]
        {
            ("UNC.", ["server", "share", "tool.exe"]),
            ("UNC ", ["server", "share", "tool.exe"]),
            ($"{volumeName}.", ["tool.exe"]),
            ($"{volumeName} ", ["tool.exe"]),
        })
        {
            foreach (string executablePath in
                        CreateDosDevicesReservedShareLookalikeExecutablePathVariants(
                            shareComponent,
                            childPathSegments))
            {
                Assert.Null(
                    AdapterHostBootstrap.GetExecutableName(
                        executablePath,
                        useWindowsExecutableSemantics: true));
            }
        }
    }

    [Fact]
    public void GetExecutableNamePreservesBasenameForWindowsUncHostsNamedDosDevices()
    {
        foreach (string executablePath in new[]
        {
            @"\\DosDevices\share\tool.exe",
            "//DosDevices/share/tool.exe",
        })
        {
            Assert.Equal(
                "tool.exe",
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsRawWindowsNtNamespacePathsAsInvalidOnWindows()
    {
        foreach (string executablePath in new[]
        {
            @"\??\C:",
            @"\??\UNC\server\share",
            @"\DosDevices\C:",
            @"\Global??\UNC\server",
            @"\Global??\UNC\server\share",
            @"\Device\HarddiskVolume1",
            @"\Device\NamedPipe\foo",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void
        GetExecutableNameTreatsKnownNonFileBackedRawWindowsObjectManagerRootsAsInvalidOnWindows()
    {
        foreach (string executablePath in new[]
        {
            @"\KnownDlls\tool.exe",
            @"\BaseNamedObjects\tool.exe",
            @"\Registry\Machine\AzureAuth\tool.exe",
            @"\RPC Control\tool.exe",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsWindowsPathsWithEmptyChildSegmentsAsInvalidOnWindows()
    {
        foreach (string executablePath in new[]
        {
            @"\\server\share\\tool.exe",
            @"C:\dir\\tool.exe",
            @"\\?\C:\dir\\tool.exe",
            @"\DosDevices\C:\dir\\tool.exe",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsMalformedWindowsUncPathsAsInvalidOnWindows()
    {
        foreach (string executablePath in new[]
        {
            "//server//share",
            "//server//tool.exe",
            @"\\server\\share",
            @"\\server\\tool.exe",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsPathsWithRepeatedLeadingSeparatorsAsInvalidOnWindows()
    {
        foreach (string executablePath in new[]
        {
            @"\\\server\share\tool.exe",
            "////?/C:/NUL/tool.exe",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsUnsafeWindowsPathSegmentsAsInvalidOnWindows()
    {
        foreach (string executablePath in new[]
        {
            "NUL",
            @"C:\NUL\tool.exe",
            @"C:\CON.txt",
            @"C:\dir\COM1.exe",
            @"\\server\share\NUL\tool.exe",
            @"\\server\share\CON.txt",
            @"\\server\share\dir\COM1.exe",
            @"\\?\C:\NUL\tool.exe",
            @"\DosDevices\C:\dir\COM1.exe",
            @"C:\dir\.\tool.exe",
            @"C:\dir\..\tool.exe",
            @"C:\dir.\tool.exe",
            @"C:\dir \tool.exe",
            @"C:\dir\NUL .exe",
            @"\\server\share\dir\COM1 .txt",
            @"\\?\C:\dir\NUL .exe",
            @"\DosDevices\C:\dir\COM1 .txt",
            @"C:\bad<dir>\tool.exe",
            @"C:\dir\bad?.exe",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsWindowsUncIpcSharesAsInvalidOnWindows()
    {
        foreach (string executablePath in new[]
        {
            @"\\server\pipe\tool.exe",
            @"\\server\IPC$\tool.exe",
            "//server/mailslot/tool.exe",
            "//server/IPC$/tool.exe",
            @"\\?\UNC\server\pipe\tool.exe",
            @"\\?\UNC\server\IPC$\tool.exe",
            "//?/UNC/server/mailslot/tool.exe",
            "//?/UNC/server/IPC$/tool.exe",
            @"\Global??\UNC\server\pipe\tool.exe",
            @"\Global??\UNC\server\IPC$\tool.exe",
            @"\??\UNC\server\mailslot\tool.exe",
            @"\??\UNC\server\IPC$\tool.exe",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void
        GetExecutableNameRejectsUncIpcShareTrailingDotOrSpaceOnWindows()
    {
        foreach (string shareName in new[]
        {
            "pipe.",
            "pipe ",
            "mailslot.",
            "mailslot ",
            "IPC$.",
            "IPC$ ",
        })
        {
            foreach (string executablePath in CreateWindowsUncExecutablePathVariants(
                        "server",
                        shareName,
                        "tool.exe"))
            {
                Assert.Null(
                    AdapterHostBootstrap.GetExecutableName(
                        executablePath,
                        useWindowsExecutableSemantics: true));
            }
        }
    }

    [Fact]
    public void GetExecutableNameTreatsUnsafeWindowsUncShareNamesAsInvalidOnWindows()
    {
        string overlongShareName = new('s', 81);

        foreach (string shareName in new[]
        {
            "bad+share",
            "bad=share",
            "bad;share",
            "bad,share",
            "bad[share",
            "bad]share",
            "bad:share",
            "bad|share",
            "bad<share",
            "bad>share",
            "bad\"share",
            "bad*share",
            "bad?share",
            "bad\u001Fshare",
            "bad\0share",
            overlongShareName,
        })
        {
            foreach (string executablePath in new[]
            {
                $@"\\server\{shareName}\tool.exe",
                $@"\\?\UNC\server\{shareName}\tool.exe",
                $@"\\.\UNC\server\{shareName}\tool.exe",
                $@"\Global??\UNC\server\{shareName}\tool.exe",
                $@"\??\UNC\server\{shareName}\tool.exe",
            })
            {
                Assert.Null(
                    AdapterHostBootstrap.GetExecutableName(
                        executablePath,
                        useWindowsExecutableSemantics: true));
            }
        }
    }

    [Fact]
    public void GetExecutableNameTreatsUnsafeWindowsUncAuthoritiesAsInvalidOnWindows()
    {
        foreach (string authorityName in new[]
        {
            "bad host",
            "bad[host",
            "bad]host",
        })
        {
            foreach (string executablePath in CreateWindowsUncExecutablePathVariants(
                        authorityName,
                        "share",
                        "tool.exe"))
            {
                Assert.Null(
                    AdapterHostBootstrap.GetExecutableName(
                        executablePath,
                        useWindowsExecutableSemantics: true));
            }
        }
    }

    [Fact]
    public void
        GetExecutableNameTreatsWindowsPathsContainingControlOrNulCharactersAsInvalidOnWindows()
    {
        foreach (string executablePath in new[]
        {
            "C:\\dir\\bad\u001Fname\\tool.exe",
            $"C:\\dir\\bad{'\0'}name\\tool.exe",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsTerminalDotSegmentsAsInvalidBasenames()
    {
        foreach ((string executablePath, bool useWindowsExecutableSemantics) in new[]
        {
            (".", false),
            ("..", false),
            ("/usr/local/bin/.", false),
            ("/usr/local/bin/..", false),
            (@"C:.", true),
            (@"C:..", true),
            (@"C:\tools\.", true),
            (@"C:\tools\..", true),
            (@"\\server\share\.", true),
            (@"\\server\share\..", true),
            (@"\\?\C:\tools\.", true),
            (@"\\?\UNC\server\share\..", true),
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsMalformedWindowsDriveAndPseudoDevicePathsAsInvalidOnWindows()
    {
        foreach (string executablePath in new[]
        {
            "C::",
            @"C::\tool.exe",
            @"\?\C:\tool.exe",
            @"\.\C:\tool.exe",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsNonAsciiWindowsDriveFormsAsInvalidOnWindows()
    {
        foreach (string executablePath in new[]
        {
            @"Ω:\tool.exe",
            @"\\?\Ж:\tool.exe",
            @"\??\Ж:\tool.exe",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNameTreatsNonCanonicalWindowsDeviceVolumePathsAsInvalidOnWindows()
    {
        foreach (string executablePath in new[]
        {
            @"\\?\Volume{11111111111111111111111111111111}\tool.exe",
            @"\\?\Volume{11111111-1111-1111-1111-11111111111Z}\tool.exe",
            @"\\?\Volume{not-a-guid}\tool.exe",
            @"\??\Volume{not-a-guid}\tool.exe",
        })
        {
            Assert.Null(
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNamePreservesBasenameForWindowsUncChildPaths()
    {
        foreach ((string executablePath, string expectedExecutableName) in
            new[]
            {
                ("//server/share/tool.exe", "tool.exe"),
                (@"\\server\share\tool.exe", "tool.exe"),
                (@"\\?\UNC\server\share\tool.exe", "tool.exe"),
                (@"\\.\UNC\server\share\tool.exe", "tool.exe"),
                ("//?/UNC/server/share/tool.exe", "tool.exe"),
                ("//./UNC/server/share/tool.exe", "tool.exe"),
            })
        {
            Assert.Equal(
                expectedExecutableName,
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNamePreservesBasenameForWindowsUncSharesNamedLikeReservedDevices()
    {
        foreach (string executablePath in new[]
        {
            @"\\server\COM1\tool.exe",
            @"\\?\UNC\server\COM1\tool.exe",
            @"\\.\UNC\server\COM1\tool.exe",
            @"\Global??\UNC\server\COM1\tool.exe",
            @"\??\UNC\server\COM1\tool.exe",
        })
        {
            Assert.Equal(
                "tool.exe",
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNamePreservesBasenameForWindowsUncAuthoritiesNearReservedDeviceNames()
    {
        foreach (string executablePath in new[]
        {
            @"\\con.example.com\share\tool.exe",
            "//prn.example.net/share/tool.exe",
        })
        {
            Assert.Equal(
                "tool.exe",
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNamePreservesBasenameForCanonicalizableRawWindowsNtChildPaths()
    {
        const string volumeName = "Volume{11111111-1111-1111-1111-111111111111}";

        foreach ((string executablePath, string expectedExecutableName) in
            new[]
            {
                (@"\??\C:\dir\tool.exe", "tool.exe"),
                ($@"\??\{volumeName}\dir\tool.exe", "tool.exe"),
                (@"\??\UNC\server\share\tool.exe", "tool.exe"),
                (@"\DosDevices\C:\dir\tool.exe", "tool.exe"),
                (@"\Global??\UNC\server\share\tool.exe", "tool.exe"),
                (@"\GLOBAL??/UNC/server/share/tool.exe", "tool.exe"),
            })
        {
            Assert.Equal(
                expectedExecutableName,
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void ResolveInvocationPreservesAllowlistedWindowsDeviceChildPaths()
    {
        const string volumeName = "Volume{11111111-1111-1111-1111-111111111111}";
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();

        foreach (string executablePath in new[]
        {
            @"\\?\C:\dir\AZUREAUTH-CREDPROVIDER.EXE",
            $@"\\?\{volumeName}\dir\AZUREAUTH-CREDPROVIDER.EXE",
            @"\\?\UNC\server\share\AZUREAUTH-CREDPROVIDER.EXE",
        })
        {
            if (!OperatingSystem.IsWindows())
            {
                AssertNoInvocation(
                    descriptor,
                    executablePath,
                    "git",
                    "credential-helper",
                    "get");
                continue;
            }

            AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
                descriptor,
                executablePath,
                arguments: ["git", "credential-helper", "get"]);

            Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
            Assert.Equal(AdapterInvocationMode.Protocol, context.Mode);
            Assert.Equal("AZUREAUTH-CREDPROVIDER.EXE", context.ExecutableName);
            Assert.Equal("GitCredentialHelper", context.Entrypoint.Name);
        }
    }

    [Fact]
    public void ResolveInvocationPreservesAllowlistedRawWindowsNtAliasChildPaths()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();

        foreach (string executablePath in new[]
        {
            @"\DosDevices\C:\dir\AZUREAUTH-CREDPROVIDER.EXE",
            @"\Global??\UNC\server\share\AZUREAUTH-CREDPROVIDER.EXE",
            @"\GLOBAL??/UNC/server/share/AZUREAUTH-CREDPROVIDER.EXE",
        })
        {
            if (!OperatingSystem.IsWindows())
            {
                AssertNoInvocation(
                    descriptor,
                    executablePath,
                    "git",
                    "credential-helper",
                    "get");
                continue;
            }

            AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
                descriptor,
                executablePath,
                arguments: ["git", "credential-helper", "get"]);

            Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
            Assert.Equal(AdapterInvocationMode.Protocol, context.Mode);
            Assert.Equal("AZUREAUTH-CREDPROVIDER.EXE", context.ExecutableName);
            Assert.Equal("GitCredentialHelper", context.Entrypoint.Name);
        }
    }

    [Fact]
    public void GetExecutableNamePreservesBasenameForWindowsDeviceChildPaths()
    {
        const string volumeName = "Volume{11111111-1111-1111-1111-111111111111}";

        foreach ((string executablePath, string expectedExecutableName) in
            new[]
            {
                (@"\\?\C:\dir\tool.exe", "tool.exe"),
                (@"\\.\C:\dir\tool.exe", "tool.exe"),
                ("//?/C:/dir/tool.exe", "tool.exe"),
                ("//./C:/dir/tool.exe", "tool.exe"),
                ($@"\\?\{volumeName}\dir\tool.exe", "tool.exe"),
                ($@"\\.\{volumeName}\dir\tool.exe", "tool.exe"),
                ($"//?/{volumeName}/dir/tool.exe", "tool.exe"),
                ($"//./{volumeName}/dir/tool.exe", "tool.exe"),
            })
        {
            Assert.Equal(
                expectedExecutableName,
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void GetExecutableNamePreservesBasenameForOrdinaryWindowsPathsNearReservedDeviceNames()
    {
        foreach ((string executablePath, string expectedExecutableName) in new[]
        {
            (@"C:\dir\COM10.exe", "COM10.exe"),
            (@"\\server\share\LPT10.exe", "LPT10.exe"),
            (@"\\?\C:\dir\CONSOLE.exe", "CONSOLE.exe"),
        })
        {
            Assert.Equal(
                expectedExecutableName,
                AdapterHostBootstrap.GetExecutableName(
                    executablePath,
                    useWindowsExecutableSemantics: true));
        }
    }

    [Fact]
    public void TryResolveInvocationTreatsSingleLeadingSlashWindowsishPathAsExplicitUnixShape()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/server/share/AZUREAUTH-CREDPROVIDER.EXE",
            arguments: ["git", "credential-helper", "get"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void
        TryResolveInvocationDoesNotTreatRepeatedLeadingSeparatorsAsExplicitUnixShapeOnWindows()
    {
        AdapterDescriptor descriptor = OperatingSystem.IsWindows()
            ? CreateSharedGitDescriptor(
                sharedExecutableName: "AZUREAUTH-CREDPROVIDER.EXE")
            : CreateSharedGitDescriptor();

        foreach (string executablePath in new[]
        {
            @"\\\server\share\AZUREAUTH-CREDPROVIDER.EXE",
            "////?/C:/NUL/AZUREAUTH-CREDPROVIDER.EXE",
        })
        {
            AssertNoInvocation(
                descriptor,
                executablePath,
                "git",
                "credential-helper",
                "get");
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectUnconstrainedHumanEntrypointForExplicitlyInvalidPaths()
    {
        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();
        string[] invalidPaths = OperatingSystem.IsWindows()
            ? [
                ".",
                "..",
                "NUL",
                @"C::",
                @"C::\tool.exe",
                @"\?\C:\tool.exe",
                @"\.\C:\tool.exe",
                @"Ω:\tool.exe",
                @"\\?\Ж:\tool.exe",
                @"\\?\Volume{not-a-guid}\tool.exe",
                @"\\??\UNC\server\share\tool.exe",
                @"\\DosDevices\C:\dir\tool.exe",
                @"\KnownDlls\tool.exe",
                @"\Registry\Machine\AzureAuth\azureauth-credprovider.exe",
                @"C:\NUL\tool.exe",
                @"C:\CON.txt",
                @"C:\dir\COM1.exe",
                @"\\server\share\NUL\tool.exe",
                @"C:\dir\..\tool.exe",
                @"C:\dir \tool.exe",
            ]
            : [".", ".."];

        foreach (string executablePath in invalidPaths)
        {
            AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectUnconstrainedHumanEntrypointForWindowsDeviceBoundaryPaths()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();

        foreach (string executablePath in new[]
        {
            @"\\?\C:",
            @"\\.\C:",
            @"\\?\UNC",
            @"\\.\UNC",
        })
        {
            AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectUnconstrainedHumanEntrypointForReservedAliasesWithWhitespace()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();

        foreach (string executablePath in new[]
        {
            @"C:\dir\NUL .exe",
            @"\\server\share\dir\COM1 .txt",
            @"\\?\C:\dir\NUL .exe",
            @"\DosDevices\C:\dir\COM1 .txt",
        })
        {
            AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectUnconstrainedHumanEntrypointForEmptyOrWhitespacePaths()
    {
        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();

        foreach (string executablePath in new[] { string.Empty, " ", "\t" })
        {
            AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectUnconstrainedHumanEntrypointForBoundaryOrMalformedWindowsPaths()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();

        foreach (string executablePath in new[]
        {
            @"\\server",
            @"\\server\share",
            @"\\?\UNC\server\share",
            "//?/UNC/server/share",
            @"\\server\\share",
            "//server//share",
            @"\\server\share\\AZUREAUTH-CREDPROVIDER.EXE",
            @"C:\dir\\AZUREAUTH-CREDPROVIDER.EXE",
            @"\\?\C:\dir\\AZUREAUTH-CREDPROVIDER.EXE",
            @"\DosDevices\C:\dir\\AZUREAUTH-CREDPROVIDER.EXE",
            @"\\\server\share\tool.exe",
        })
        {
            AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectUnconstrainedHumanEntrypointForRawWindowsNtPaths()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();

        foreach (string executablePath in new[]
        {
            @"\??\C:",
            @"\??\UNC\server\share",
            @"\Device\NamedPipe\foo",
        })
        {
            AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectUnconstrainedHumanEntrypointForNonFileBackedDeviceNamespacePaths()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();

        foreach (string executablePath in new[]
        {
            @"\\.\PhysicalDrive0",
            @"\\.\pipe",
            @"\\?\GLOBALROOT",
            @"\\?\foo",
            @"\\.\PIPE\azureauth-credprovider",
            @"\\?\GLOBALROOT\Device\NamedPipe\azureauth-credprovider",
            @"\\?\foo\azureauth-credprovider",
        })
        {
            AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectUnconstrainedHumanEntrypointForWindowsUncIpcSharePaths()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();

        foreach (string shareName in new[] { "pipe", "mailslot", "IPC$" })
        {
            foreach (string executablePath in CreateWindowsUncExecutablePathVariants(
                        "server",
                        shareName,
                        "tool.exe"))
            {
                AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
            }
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectHumanEntrypointForUncIpcShareTrailingDotOrSpace()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();

        foreach (string shareName in new[]
        {
            "pipe.",
            "pipe ",
            "mailslot.",
            "mailslot ",
            "IPC$.",
            "IPC$ ",
        })
        {
            foreach (string executablePath in CreateWindowsUncExecutablePathVariants(
                        "server",
                        shareName,
                        "tool.exe"))
            {
                AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
            }
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectHumanEntrypointForAliasReservedShareTrailingDotOrSpace()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string volumeName = "Volume{11111111-1111-1111-1111-111111111111}";
        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();

        foreach ((string shareComponent, string[] childPathSegments) in new (string, string[])[]
        {
            ("UNC.", ["server", "share", "tool.exe"]),
            ("UNC ", ["server", "share", "tool.exe"]),
            ($"{volumeName}.", ["tool.exe"]),
            ($"{volumeName} ", ["tool.exe"]),
        })
        {
            foreach (string executablePath in
                        CreateDosDevicesReservedShareLookalikeExecutablePathVariants(
                            shareComponent,
                            childPathSegments))
            {
                AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
            }
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectUnconstrainedHumanEntrypointForUnsafeWindowsUncAuthorities()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();

        foreach (string authorityName in new[] { "bad host", "bad[host", "bad]host" })
        {
            foreach (string executablePath in CreateWindowsUncExecutablePathVariants(
                        authorityName,
                        "share",
                        "tool.exe"))
            {
                AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
            }
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectUnconstrainedHumanEntrypointForUnsafeWindowsUncShareNames()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();
        string overlongShareName = new('s', 81);

        foreach (string shareName in new[] { "bad?share", overlongShareName })
        {
            foreach (string executablePath in CreateWindowsUncExecutablePathVariants(
                        "server",
                        shareName,
                        "tool.exe"))
            {
                AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
            }
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectUnconstrainedHumanEntrypointForWindowsPathsWithControlOrNul()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();

        foreach (string executablePath in new[]
        {
            $@"C:\dir\bad{'\u001F'}name\tool.exe",
            $@"\\server\share\bad{'\0'}name\tool.exe",
            $@"\\?\UNC\server\share\bad{'\u001F'}name\tool.exe",
            $@"\Global??\UNC\server\share\bad{'\0'}name\tool.exe",
        })
        {
            AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
        }
    }

    [Fact]
    public void
        ResolveAndTryResolveRejectUnconstrainedHumanEntrypointForUnixPathsWithControlOrNul()
    {
        AdapterDescriptor descriptor = CreateExecutableUnconstrainedHumanDoctorDescriptor();

        foreach (string executablePath in new[]
        {
            "/tmp/bad\u001Fname/tool",
            "/tmp/bad\0name/tool",
        })
        {
            AssertNoInvocationAndResolveThrows(descriptor, executablePath, "doctor");
        }
    }

    [Fact]
    public void ResolveInvocationTreatsBackslashInUnixPathAsFileNameCharacter()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(
            sharedExecutableName: @"nottool\tool");

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: "/tmp/nottool\\tool",
            arguments: ["git", "credential-helper", "get"]);

        Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
        Assert.Equal("GitCredentialHelper", context.Entrypoint.Name);
        Assert.Equal(@"nottool\tool", context.ExecutableName);
    }

    [Fact]
    public void TryResolveInvocationDoesNotTreatUnixPathContainingBackslashAsWindowsPath()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(
            sharedExecutableName: "tool");

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/tmp/nottool\\TOOL",
            arguments: ["git", "credential-helper", "get"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void ResolveInvocationPreservesTrailingWhitespaceInUnixExecutableBasename()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(
            sharedExecutableName: "tool ");

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: "/tmp/tool ",
            arguments: ["git", "credential-helper", "get"]);

        Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
        Assert.Equal("GitCredentialHelper", context.Entrypoint.Name);
        Assert.Equal("tool ", context.ExecutableName);
    }

    [Fact]
    public void TryResolveInvocationDoesNotTrimTrailingWhitespaceFromUnixExecutableBasename()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor(
            sharedExecutableName: "tool");

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/tmp/tool ",
            arguments: ["git", "credential-helper", "get"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void ResolveUsesExecutableShapeWithoutParsingPayloadForDisjointHumanEntrypoint()
    {
        var descriptor = new AdapterDescriptor(
            "Git Credential Helper",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "DedicatedHelper",
                    AdapterInvocationMode.Protocol,
                    executableNames:
                    [
                        "git-credential-azureauth-credprovider",
                        "git-credential-azureauth-credprovider.exe",
                    ],
                    description: "Dedicated Git helper entry point."),
                new AdapterEntrypointDescriptor(
                    "HumanCommand",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["azureauth-credprovider"]),
            ]);

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: "/opt/azureauth/git-credential-azureauth-credprovider",
            arguments: ["unexpected-operation"]);

        Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
        Assert.Equal(AdapterInvocationMode.Protocol, context.Mode);
        Assert.Equal("DedicatedHelper", context.Entrypoint.Name);
        Assert.Empty(context.MatchedArguments);
        Assert.Equal(["unexpected-operation"], context.PayloadArguments);
    }

    [Fact]
    public void
        ConstructorAllowsExecutableOnlyProtocolEntrypointForUnconstrainedHumanExecutableBoundary()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "ProtocolAny",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"]),
                new AdapterEntrypointDescriptor(
                    "HumanDoctor",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["doctor"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);

        Assert.True(descriptor.SupportsProtocolMode);
        Assert.True(descriptor.SupportsHumanCommandMode);
    }

    [Fact]
    public void ConstructorRejectsProtocolEntrypointWhenExecutableNormalizationIsAmbiguous()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Shared Host",
                AdapterProtocol.GitCredentialHelper,
                [
                    new AdapterEntrypointDescriptor(
                        "ProtocolAny",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool.ExE"]),
                    new AdapterEntrypointDescriptor(
                        "HumanDoctor",
                        AdapterInvocationMode.HumanCommand,
                        executableNames: ["tool"],
                        argumentTokens: ["doctor"],
                        argumentMatchMode: AdapterArgumentMatchMode.Exact),
                ]));

        Assert.Contains(
            "must not subsume human command",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProtocolAny", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HumanDoctor", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolveInvocationSupportsContainsAllArgumentMatches()
    {
        var descriptor = new AdapterDescriptor(
            "NuGet Plugin",
            AdapterProtocol.NuGetPlugin,
            [
                new AdapterEntrypointDescriptor(
                    "Plugin",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["-Plugin"],
                    argumentMatchMode: AdapterArgumentMatchMode.ContainsAll,
                    stripMatchedArguments: false),
                new AdapterEntrypointDescriptor(
                    "HumanCommand",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["azureauth-credprovider"]),
            ]);

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments:
            [
                "-Uri",
                "https://pkgs.dev.azure.com/example/_packaging/feed/nuget/v3/index.json",
                "-Plugin",
            ],
            out AdapterInvocationContext? context);

        Assert.True(matched);
        Assert.NotNull(context);
        Assert.Equal(AdapterProtocol.NuGetPlugin, context.Protocol);
        Assert.Equal(AdapterInvocationMode.Protocol, context.Mode);
        Assert.Equal(["-Plugin"], context.MatchedArguments);
        Assert.Equal(
            [
                "-Uri",
                "https://pkgs.dev.azure.com/example/_packaging/feed/nuget/v3/index.json",
                "-Plugin",
            ],
            context.PayloadArguments);
    }

    [Fact]
    public void TryResolveInvocationRejectsHumanFallbackForPartialContainsAllProtocolBoundary()
    {
        var descriptor = new AdapterDescriptor(
            "NuGet Plugin",
            AdapterProtocol.NuGetPlugin,
            [
                new AdapterEntrypointDescriptor(
                    "Plugin",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"],
                    argumentTokens: ["-Plugin", "-Uri"],
                    argumentMatchMode: AdapterArgumentMatchMode.ContainsAll),
                new AdapterEntrypointDescriptor(
                    "HumanCommand",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["tool"]),
            ]);

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/tool",
            arguments: ["-Plugin"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void TryResolveInvocationRejectsHumanFallbackForPartialDuplicateContainsAllBoundary()
    {
        var descriptor = new AdapterDescriptor(
            "Duplicate Flag Protocol",
            AdapterProtocol.NuGetPlugin,
            [
                new AdapterEntrypointDescriptor(
                    "Plugin",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"],
                    argumentTokens: ["-Plugin", "-Plugin"],
                    argumentMatchMode: AdapterArgumentMatchMode.ContainsAll),
                new AdapterEntrypointDescriptor(
                    "HumanCommand",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["tool"]),
            ]);

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/tool",
            arguments: ["-Plugin"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void TryResolveInvocationFailClosesProtocolNearMissBeforeAmbiguousHumanDisambiguation()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "GitProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"],
                    argumentTokens: ["git", "credential-helper"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "HumanGitOne",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["git"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
                new AdapterEntrypointDescriptor(
                    "HumanGitTwo",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["git"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: null,
            arguments: ["git"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void ResolveInvocationThrowsWhenExecutableNameSetsDifferByMixedCaseExeSuffixOnUnix()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "LowerCaseExeOrAlias",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool.exe", "alias"],
                    argumentTokens: ["git"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "MixedCaseExeOrAliasAndOther",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool.ExE", "alias", "other"],
                    argumentTokens: ["git"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
            ]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AdapterHostBootstrap.ResolveInvocation(
                descriptor,
                executablePath: "/usr/local/bin/alias",
                arguments: ["git", "get"]));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LowerCaseExeOrAlias", exception.Message, StringComparison.Ordinal);
        Assert.Contains("MixedCaseExeOrAliasAndOther", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolveReturnsFalseWhenExecutableNameSetsDifferByMixedCaseExeSuffixOnUnix()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "LowerCaseExeOrAlias",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool.exe", "alias"],
                    argumentTokens: ["git"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "MixedCaseExeOrAliasAndOther",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool.ExE", "alias", "other"],
                    argumentTokens: ["git"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
            ]);

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/alias",
            arguments: ["git", "get"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void ResolveInvocationPrefersEntrypointWithNarrowerExecutableNameSet()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "ToolOrAlias",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool", "alias"],
                    argumentTokens: ["git"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "ToolOnly",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"],
                    argumentTokens: ["git"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
            ]);

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/tool",
            arguments: ["git", "get"]);

        Assert.Equal("ToolOnly", context.Entrypoint.Name);
        Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
    }

    [Fact]
    public void ResolveInvocationPrefersExactArgumentBoundaryOverUnusedExecutableAliasSuperset()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "ToolAny",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"]),
                new AdapterEntrypointDescriptor(
                    "ToolOrAliasExactCmd",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool", "alias"],
                    argumentTokens: ["cmd"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);

        AdapterInvocationContext context = AdapterHostBootstrap.ResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/tool",
            arguments: ["cmd"]);

        Assert.Equal("ToolOrAliasExactCmd", context.Entrypoint.Name);
        Assert.Equal(AdapterProtocol.GitCredentialHelper, context.Protocol);
        Assert.Equal(["cmd"], context.MatchedArguments);
        Assert.Empty(context.PayloadArguments);
    }

    [Fact]
    public void ResolveInvocationThrowsWhenEquallySpecificEntrypointsMatch()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.Unspecified,
            [
                new AdapterEntrypointDescriptor(
                    "GitProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["shared"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix,
                    protocol: AdapterProtocol.GitCredentialHelper),
                new AdapterEntrypointDescriptor(
                    "NuGetProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["shared"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix,
                    protocol: AdapterProtocol.NuGetPlugin),
                new AdapterEntrypointDescriptor(
                    "HumanCommand",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["azureauth-credprovider"]),
            ]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AdapterHostBootstrap.ResolveInvocation(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["shared", "secret-value"]));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Shared Host", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolveInvocationReturnsFalseWhenEquallySpecificEntrypointsMatch()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.Unspecified,
            [
                new AdapterEntrypointDescriptor(
                    "GitProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["shared"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix,
                    protocol: AdapterProtocol.GitCredentialHelper),
                new AdapterEntrypointDescriptor(
                    "NuGetProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["shared"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix,
                    protocol: AdapterProtocol.NuGetPlugin),
                new AdapterEntrypointDescriptor(
                    "HumanCommand",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["azureauth-credprovider"]),
            ]);

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["shared", "secret-value"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void ResolveInvocationThrowsWhenMatchedEntrypointsAreIncomparable()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "PrefixProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"],
                    argumentTokens: ["cmd"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "ContainsAllProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"],
                    argumentTokens: ["cmd", "flag"],
                    argumentMatchMode: AdapterArgumentMatchMode.ContainsAll),
            ]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AdapterHostBootstrap.ResolveInvocation(
                descriptor,
                executablePath: "/usr/local/bin/tool",
                arguments: ["cmd", "flag"]));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ContainsAllProtocol", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PrefixProtocol", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolveInvocationReturnsFalseWhenMatchedEntrypointsAreIncomparable()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "PrefixProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"],
                    argumentTokens: ["cmd"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "ContainsAllProtocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"],
                    argumentTokens: ["cmd", "flag"],
                    argumentMatchMode: AdapterArgumentMatchMode.ContainsAll),
            ]);

        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath: "/usr/local/bin/tool",
            arguments: ["cmd", "flag"],
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    [Fact]
    public void ResolveInvocationThrowsSafeErrorWhenNoEntrypointMatches()
    {
        var descriptor = new AdapterDescriptor(
            "Git Credential Helper",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "DedicatedHelper",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["git-credential-azureauth-credprovider"]),
            ]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AdapterHostBootstrap.ResolveInvocation(
                descriptor,
                executablePath: "/usr/local/bin/another-entrypoint",
                arguments: ["doctor", "--token", "secret-value"]));

        Assert.DoesNotContain("secret-value", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Git Credential Helper", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsDescriptorWithNullEntrypointCollection()
    {
        Assert.Throws<ArgumentNullException>(
            "entrypoints",
            () => new AdapterDescriptor(
                "Null Entrypoints",
                AdapterProtocol.Unspecified,
                entrypoints: null!));
    }

    [Fact]
    public void ConstructorRejectsDescriptorWithoutEntrypoints()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "No Entrypoints",
                AdapterProtocol.Unspecified,
                []));

        Assert.Contains(
            "at least one entry point",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorRejectsDescriptorWithNullEntrypoint()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Null Entrypoint",
                AdapterProtocol.Unspecified,
                new AdapterEntrypointDescriptor[] { null! }));

        Assert.Contains(
            "must not contain null entry points",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorRejectsProtocolEntrypointWithoutConcreteProtocolMetadata()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Protocol Metadata",
                AdapterProtocol.Unspecified,
                [
                    new AdapterEntrypointDescriptor(
                        "Protocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool"]),
                ]));

        Assert.Contains(
            "require a concrete adapter protocol",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorRejectsProtocolEntrypointWhenDescriptorAndEntrypointProtocolsDisagree()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Protocol Metadata",
                AdapterProtocol.GitCredentialHelper,
                [
                    new AdapterEntrypointDescriptor(
                        "Protocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool"],
                        protocol: AdapterProtocol.NuGetPlugin),
                ]));

        Assert.Contains(
            "must agree with the descriptor protocol",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorRejectsConcreteDescriptorProtocolWithoutProtocolEntrypoints()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterDescriptor(
                "Human Only",
                AdapterProtocol.GitCredentialHelper,
                [
                    new AdapterEntrypointDescriptor(
                        "HumanCommand",
                        AdapterInvocationMode.HumanCommand,
                        executableNames: ["azureauth-credprovider"]),
                ]));

        Assert.Contains(
            "require at least one protocol entry point",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorRejectsHumanCommandEntrypointWithConcreteProtocol()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterEntrypointDescriptor(
                "HumanCommand",
                AdapterInvocationMode.HumanCommand,
                executableNames: ["tool"],
                protocol: AdapterProtocol.GitCredentialHelper));

        Assert.Contains(
            "must not declare a concrete adapter protocol",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorRejectsArgumentTokensWhenArgumentMatchModeIsAny()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterEntrypointDescriptor(
                "AnyWithTokens",
                AdapterInvocationMode.HumanCommand,
                executableNames: ["tool"],
                argumentTokens: ["doctor"],
                argumentMatchMode: AdapterArgumentMatchMode.Any));

        Assert.Contains(
            "argument tokens are supported only when the argument match mode requires them",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorRejectsNonAnyArgumentMatchModeWithoutArgumentTokens()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterEntrypointDescriptor(
                "PrefixWithoutTokens",
                AdapterInvocationMode.Protocol,
                executableNames: ["tool"],
                argumentMatchMode: AdapterArgumentMatchMode.Prefix));

        Assert.Contains(
            "require at least one argument token",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorRejectsFullyUnconstrainedEntrypoint()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new AdapterEntrypointDescriptor(
                "Wildcard",
                AdapterInvocationMode.HumanCommand));

        Assert.Contains(
            "fully unconstrained",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorRejectsEntrypointWithInvalidInvocationMode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            "mode",
            () => new AdapterEntrypointDescriptor(
                "InvalidMode",
                (AdapterInvocationMode)999,
                executableNames: ["tool"]));
    }

    [Fact]
    public void ConstructorRejectsEntrypointWithInvalidArgumentMatchMode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            "argumentMatchMode",
            () => new AdapterEntrypointDescriptor(
                "InvalidArgumentMatchMode",
                AdapterInvocationMode.Protocol,
                executableNames: ["tool"],
                argumentTokens: ["git"],
                argumentMatchMode: (AdapterArgumentMatchMode)999));
    }

    [Fact]
    public void ConstructorRejectsEntrypointWithInvalidProtocol()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            "protocol",
            () => new AdapterEntrypointDescriptor(
                "InvalidProtocol",
                AdapterInvocationMode.Protocol,
                executableNames: ["tool"],
                protocol: (AdapterProtocol)999));
    }

    [Fact]
    public void ConstructorRejectsDescriptorWithInvalidProtocol()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            "protocol",
            () => new AdapterDescriptor(
                "InvalidProtocol",
                (AdapterProtocol)999,
                [
                    new AdapterEntrypointDescriptor(
                        "Protocol",
                        AdapterInvocationMode.Protocol,
                        executableNames: ["tool"]),
                ]));
    }

    private static AdapterDescriptor CreateSharedGitDescriptor(
        bool humanCommandFirst = false,
        string sharedExecutableName = "azureauth-credprovider")
    {
        AdapterEntrypointDescriptor protocolEntrypoint = new(
            "GitCredentialHelper",
            AdapterInvocationMode.Protocol,
            executableNames: [sharedExecutableName],
            argumentTokens: ["git", "credential-helper"],
            argumentMatchMode: AdapterArgumentMatchMode.Prefix,
            description: "Shared CLI Git helper protocol path.");
        AdapterEntrypointDescriptor humanEntrypoint = new(
            "HumanCommand",
            AdapterInvocationMode.HumanCommand,
            executableNames: [sharedExecutableName],
            description: "Shared CLI human-facing command path.");

        return new AdapterDescriptor(
            "Git Credential Helper",
            AdapterProtocol.GitCredentialHelper,
            humanCommandFirst
                ? [humanEntrypoint, protocolEntrypoint]
                : [protocolEntrypoint, humanEntrypoint],
            description: "Git helper adapter host metadata.");
    }

    private static AdapterDescriptor CreateExecutableUnconstrainedHumanDoctorDescriptor()
    {
        return new AdapterDescriptor(
            "Human Only",
            AdapterProtocol.Unspecified,
            [
                new AdapterEntrypointDescriptor(
                    "HumanDoctor",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["doctor"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);
    }

    private static string[] CreateWindowsUncExecutablePathVariants(
        string authorityComponent,
        string shareComponent,
        string executableName)
    {
        return
        [
            $@"\\{authorityComponent}\{shareComponent}\{executableName}",
            $"//{authorityComponent}/{shareComponent}/{executableName}",
            $@"\\?\UNC\{authorityComponent}\{shareComponent}\{executableName}",
            $"//?/UNC/{authorityComponent}/{shareComponent}/{executableName}",
            $@"\\.\UNC\{authorityComponent}\{shareComponent}\{executableName}",
            $"//./UNC/{authorityComponent}/{shareComponent}/{executableName}",
            $@"\Global??\UNC\{authorityComponent}\{shareComponent}\{executableName}",
            $@"\GLOBAL??/UNC/{authorityComponent}/{shareComponent}/{executableName}",
            $@"\??\UNC\{authorityComponent}\{shareComponent}\{executableName}",
        ];
    }

    private static string[] CreateDosDevicesReservedShareLookalikeExecutablePathVariants(
        string shareComponent,
        params string[] childPathSegments)
    {
        string backslashChildPath = string.Join("\\", childPathSegments);
        string slashChildPath = string.Join("/", childPathSegments);

        return
        [
            $@"\\DosDevices\{shareComponent}\{backslashChildPath}",
            $"//DosDevices/{shareComponent}/{slashChildPath}",
        ];
    }

    private static void AssertNoInvocation(
        AdapterDescriptor descriptor,
        string executablePath,
        params string[] arguments)
    {
        bool matched = AdapterHostBootstrap.TryResolveInvocation(
            descriptor,
            executablePath,
            arguments,
            out AdapterInvocationContext? context);

        Assert.False(matched);
        Assert.Null(context);
    }

    private static void AssertNoInvocationAndResolveThrows(
        AdapterDescriptor descriptor,
        string executablePath,
        params string[] arguments)
    {
        AssertNoInvocation(descriptor, executablePath, arguments);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AdapterHostBootstrap.ResolveInvocation(
                descriptor,
                executablePath,
                arguments));

        Assert.Contains(descriptor.Name, exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "does not match the current invocation boundary",
            exception.Message,
            StringComparison.Ordinal);
    }
}
