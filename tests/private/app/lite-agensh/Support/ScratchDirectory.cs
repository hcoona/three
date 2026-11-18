using Microsoft.Data.Sqlite;

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

    public void Dispose()
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (string path in Directory.EnumerateFiles(Root, "*.sqlite", options))
        {
            using SqliteConnection connection = new(Sql.ConnectionStringFor(path));
            SqliteConnection.ClearPool(connection);
        }

        Directory.Delete(Root, recursive: true);
    }
}
