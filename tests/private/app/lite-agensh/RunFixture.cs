using System.Text;
using LiteAgensh;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace LiteAgensh.Tests;

internal sealed class RunFixture : IDisposable
{
    public RunFixture()
    {
        RootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiteAgensh.Tests",
            Guid.NewGuid().ToString("N")
        );
        RepositoryPath = Path.Combine(RootPath, "repository");
        StatePath = Path.Combine(RootPath, "repository-state");
        Directory.CreateDirectory(RepositoryPath);
        Configuration = new RunConfiguration
        {
            RepositoryPath = RepositoryPath,
            StatePath = StatePath,
            CampaignLedgerPath = Path.Combine(RootPath, "repository-campaign", "credits.sqlite"),
            Goal = "Coordinate two independent changes without touching the owner's checkout.",
        };
    }

    public string RootPath { get; }

    public string RepositoryPath { get; }

    public string StatePath { get; }

    public RunConfiguration Configuration { get; }

    public RunDatabase CreateDatabase() => new(StatePath, Configuration);

    public string WriteScript(string content)
    {
        string path = Path.Combine(RootPath, $"worker-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public void Dispose()
    {
        // This exact uniquely allocated fixture is the only cleanup target.
        foreach (
            string file in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories)
        )
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(RootPath, recursive: true);
    }
}
