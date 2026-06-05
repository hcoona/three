using Xunit;

namespace Hcoona.DocumentTranslatorCli.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void ProgramTypeIsAvailableFromApplicationAssembly()
    {
        Type programType = typeof(Program);

        Assert.Equal("Hcoona.DocumentTranslatorCli", programType.Namespace);
        Assert.Equal("document-translator", programType.Assembly.GetName().Name);
    }
}
