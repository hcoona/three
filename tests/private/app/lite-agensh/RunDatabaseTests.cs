using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class RunDatabaseTests
{
    [TestMethod]
    public void OpenFutureSchemaRejectsWithoutLosingRunOrDomainState()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        string runId = database.RunId;
        string? configuration = database.ReadMetadata("configuration");
        using (SqliteConnection connection = database.Open())
        {
            // A future application owns this table. Older code must not recreate its state.
            Sql.Execute(
                connection,
                """
                CREATE TABLE future_delivery(id INTEGER PRIMARY KEY, commit_id TEXT NOT NULL);
                INSERT INTO future_delivery VALUES(17, 'retained-candidate');
                UPDATE meta SET value='999' WHERE key='schema_version';
                """
            );
        }

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new RunDatabase(fixture.StatePath)
        );

        Assert.Contains("999", error.Message);
        Assert.AreEqual("999", database.ReadMetadata("schema_version"));
        Assert.AreEqual(runId, database.RunId);
        Assert.AreEqual(configuration, database.ReadMetadata("configuration"));
        using SqliteConnection retained = database.Open();
        Assert.AreEqual(
            "retained-candidate",
            Sql.Scalar(retained, "SELECT commit_id FROM future_delivery WHERE id=17;")
        );
        Assert.AreEqual(1L, Sql.Scalar(retained, "SELECT COUNT(*) FROM future_delivery;"));
    }

    [TestMethod]
    public void InitializeExistingRunRejectsReplacementAndPreservesIdentity()
    {
        using RunFixture fixture = new();
        RunDatabase original = fixture.CreateDatabase();
        string runId = original.RunId;
        RunConfiguration replacement = fixture.Configuration with
        {
            Goal = "An unrelated replacement goal must not destroy retained work.",
            WorkerCount = 4,
        };

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new RunDatabase(fixture.StatePath, replacement)
        );

        Assert.Contains("overwritten", error.Message);
        RunDatabase recovered = new(fixture.StatePath);
        Assert.AreEqual(runId, recovered.RunId);
        Assert.AreEqual(fixture.Configuration.Goal, recovered.Configuration.Goal);
        Assert.AreEqual(2, recovered.Configuration.WorkerCount);
        Assert.HasCount(3, recovered.Members);
    }

    [TestMethod]
    public void OpenMissingRunDoesNotCreateState()
    {
        using RunFixture fixture = new();

        FileNotFoundException error = Assert.ThrowsExactly<FileNotFoundException>(() =>
            new RunDatabase(fixture.StatePath)
        );

        Assert.AreEqual(Path.Combine(fixture.StatePath, "run.sqlite"), error.FileName);
        Assert.IsFalse(Directory.Exists(fixture.StatePath));
        Assert.IsTrue(Directory.Exists(fixture.RepositoryPath));
    }
}
