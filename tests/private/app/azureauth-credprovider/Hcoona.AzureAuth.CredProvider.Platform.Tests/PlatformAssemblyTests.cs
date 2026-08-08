using System.Reflection;
using Hcoona.AzureAuth.CredProvider.Platform;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class PlatformAssemblyTests
{
    [Fact]
    public void AssemblyMarkerIdentifiesPlatformAssembly()
    {
        Assembly assembly = typeof(PlatformAssembly).Assembly;

        Assert.Equal(PlatformAssembly.AssemblyName, assembly.GetName().Name);
    }
}
