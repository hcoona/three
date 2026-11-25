using System.Text.Json.Nodes;

namespace LiteAgensh.Tests.Support;

internal static class SqliteIncidentOutput
{
    internal static void Report(
        IEnumerable<string> directories,
        TextWriter output,
        List<Exception> failures
    )
    {
        foreach (string directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }
            ReportFiles(
                Directory.EnumerateFiles(directory, "incident-*.json"),
                output,
                failures
            );
        }
    }

    internal static void ReportFiles(
        IEnumerable<string> paths,
        TextWriter output,
        List<Exception> failures
    )
    {
        foreach (string path in paths)
        {
            try
            {
                JsonNode? incident = JsonNode.Parse(File.ReadAllText(path));
                output.WriteLine("SQLITE_INCIDENT " + incident!.ToJsonString());
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }
}
