namespace LiteAgensh.Tests.Support;

internal sealed class ScratchDirectory : IDisposable
{
    internal ScratchDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "lite-agensh-specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    internal string Root { get; }

    internal string PathFor(params string[] parts) =>
        Path.Combine([Root, .. parts]);

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
