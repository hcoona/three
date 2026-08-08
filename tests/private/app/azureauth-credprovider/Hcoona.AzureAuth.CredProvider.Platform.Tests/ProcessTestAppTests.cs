using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ProcessTestAppTests
{
    [Fact]
    public void CreateHelperEnvironmentAddsHandshakeAndExplicitOverrides()
    {
        Dictionary<string, string?> helperEnvironment = ProcessTestApp.CreateHelperEnvironment(
            "helper-nonce",
            new Dictionary<string, string?> { ["EXPLICIT"] = "value" }
        );

        Assert.Equal(
            ProcessTestApp.HelperEnabledValue,
            helperEnvironment[ProcessTestApp.HelperEnabledVariable]
        );
        Assert.Equal("helper-nonce", helperEnvironment[ProcessTestApp.HelperNonceVariable]);
        Assert.Equal("value", helperEnvironment["EXPLICIT"]);
    }

    [Fact]
    // editorconfig-checker-disable
    public void TryGetHelperDispatchArgumentsReturnsFalseWhenAmbientHelperMarkerLacksNonceHandshake()
    // editorconfig-checker-enable
    {
        Dictionary<string, string?> environment = new()
        {
            [ProcessTestApp.HelperEnabledVariable] = ProcessTestApp.HelperEnabledValue,
        };

        bool activated = ProcessTestApp.TryGetHelperDispatchArguments(
            [ProcessTestApp.HelperSwitch, "inspect"],
            name => environment.TryGetValue(name, out string? value) ? value : null,
            out string[] helperArguments
        );

        Assert.False(activated);
        Assert.Empty(helperArguments);
    }

    [Fact]
    public void TryGetHelperDispatchArgumentsReturnsFalseWhenHelperNonceHandshakeDoesNotMatch()
    {
        Dictionary<string, string?> environment = new()
        {
            [ProcessTestApp.HelperEnabledVariable] = ProcessTestApp.HelperEnabledValue,
            [ProcessTestApp.HelperNonceVariable] = "environment-nonce",
        };

        bool activated = ProcessTestApp.TryGetHelperDispatchArguments(
            ProcessTestApp.CreateHelperArguments("argument-nonce", "inspect"),
            name => environment.TryGetValue(name, out string? value) ? value : null,
            out string[] helperArguments
        );

        Assert.False(activated);
        Assert.Empty(helperArguments);
    }
}
