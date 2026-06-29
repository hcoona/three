using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ProcessTestAppTests
{
    [Fact]
    public void CreateHelperEnvironmentUsesCaseInsensitiveBootstrapVariableNamesOnWindows()
    {
        const string helperNonce = "helper-nonce";
        IReadOnlyDictionary<string, string?> environment = new Dictionary<string, string?>(
            StringComparer.Ordinal
        )
        {
            ["dotnet_root(x64)"] = "explicit root",
            ["DoTnEt_MuLtIlEvEl_LoOkUp"] = "0",
        };
        IReadOnlyDictionary<string, string?> inheritedEnvironment =
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["DOTNET_ROOT(X64)"] = "ambient root should be suppressed",
                ["DOTNET_MULTILEVEL_LOOKUP"] = "1",
                ["DOTNET_ROOT"] = "ambient root should be preserved",
                ["UNRELATED"] = "ignored",
            };

        Dictionary<string, string?> helperEnvironment = ProcessTestApp.CreateHelperEnvironment(
            helperNonce,
            environment,
            ProcessEnvironmentMode.ExplicitOnly,
            inheritedEnvironment,
            useWindowsEnvironmentVariableSemantics: true
        );

        Assert.Equal(
            ProcessTestApp.HelperEnabledValue,
            helperEnvironment[ProcessTestApp.HelperEnabledVariable]
        );
        Assert.Equal(helperNonce, helperEnvironment[ProcessTestApp.HelperNonceVariable]);
        Assert.Equal("explicit root", helperEnvironment["DOTNET_ROOT(X64)"]);
        Assert.Equal("0", helperEnvironment["dotnet_multilevel_lookup"]);
        Assert.Equal("ambient root should be preserved", helperEnvironment["dotnet_root"]);
        Assert.False(helperEnvironment.ContainsKey("UNRELATED"));
        Assert.Equal(5, helperEnvironment.Count);
    }

    [Fact]
    public void CreateHelperEnvironmentUsesCaseSensitiveBootstrapVariableNamesOnNonWindows()
    {
        const string helperNonce = "helper-nonce";
        IReadOnlyDictionary<string, string?> inheritedEnvironment =
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["dotnet_root"] = "lowercase root should not be preserved",
                ["DOTNET_ROOT"] = "uppercase root should be preserved",
                ["dotnet_multilevel_lookup"] = "lowercase lookup should not be preserved",
                ["DOTNET_MULTILEVEL_LOOKUP"] = "uppercase lookup should be preserved",
            };

        Dictionary<string, string?> helperEnvironment = ProcessTestApp.CreateHelperEnvironment(
            helperNonce,
            null,
            ProcessEnvironmentMode.ExplicitOnly,
            inheritedEnvironment,
            useWindowsEnvironmentVariableSemantics: false
        );

        Assert.Equal(
            ProcessTestApp.HelperEnabledValue,
            helperEnvironment[ProcessTestApp.HelperEnabledVariable]
        );
        Assert.Equal(helperNonce, helperEnvironment[ProcessTestApp.HelperNonceVariable]);
        Assert.Equal("uppercase root should be preserved", helperEnvironment["DOTNET_ROOT"]);
        Assert.Equal(
            "uppercase lookup should be preserved",
            helperEnvironment["DOTNET_MULTILEVEL_LOOKUP"]
        );
        Assert.False(helperEnvironment.ContainsKey("dotnet_root"));
        Assert.False(helperEnvironment.ContainsKey("dotnet_multilevel_lookup"));
        Assert.Equal(4, helperEnvironment.Count);
    }

    [Fact]
    public void
        TryGetHelperDispatchArgumentsReturnsFalseWhenAmbientHelperMarkerLacksNonceHandshake()
    {
        Dictionary<string, string?> environment = new(
            StringComparer.Ordinal
        )
        {
            [ProcessTestApp.HelperEnabledVariable] = ProcessTestApp.HelperEnabledValue,
        };

        var activated = ProcessTestApp.TryGetHelperDispatchArguments(
            [ProcessTestApp.HelperSwitch, "inspect"],
            name => environment.TryGetValue(name, out var value) ? value : null,
            out var helperArguments
        );

        Assert.False(activated);
        Assert.Empty(helperArguments);
    }

    [Fact]
    public void TryGetHelperDispatchArgumentsReturnsFalseWhenHelperNonceHandshakeDoesNotMatch()
    {
        const string environmentNonce = "environment-nonce";
        Dictionary<string, string?> environment = new(
            StringComparer.Ordinal
        )
        {
            [ProcessTestApp.HelperEnabledVariable] = ProcessTestApp.HelperEnabledValue,
            [ProcessTestApp.HelperNonceVariable] = environmentNonce,
        };

        var activated = ProcessTestApp.TryGetHelperDispatchArguments(
            ProcessTestApp.CreateHelperArguments("argument-nonce", "inspect"),
            name => environment.TryGetValue(name, out var value) ? value : null,
            out var helperArguments
        );

        Assert.False(activated);
        Assert.Empty(helperArguments);
    }
}
