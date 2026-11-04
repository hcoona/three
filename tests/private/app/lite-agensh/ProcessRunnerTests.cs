using System.Diagnostics;
using System.Globalization;
using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class ProcessRunnerTests(TestContext testContext)
{
    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow(0)]
    [DataRow(7)]
    public async Task RunAsyncDeclaredArgumentsRetainsExactOutcomeAndSettlesLease(int exitCode)
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        ProcessRunner runner = new(database);
        string script = fixture.WriteScript(
            """
            param([string] $Literal, [int] $Result)
            [Console]::Out.WriteLine($Literal)
            [Console]::Out.WriteLine([Environment]::CurrentDirectory)
            [Console]::Error.WriteLine('check diagnostic')
            exit $Result
            """
        );

        ProcessResult result = await runner.RunAsync(
            "pwsh",
            [
                "-NoLogo",
                "-NoProfile",
                "-File",
                script,
                "space ; 'quoted' & literal",
                exitCode.ToString(CultureInfo.InvariantCulture),
            ],
            fixture.RepositoryPath,
            TimeSpan.FromSeconds(30),
            testContext.CancellationToken
        );

        Assert.AreEqual(exitCode, result.ExitCode);
        Assert.AreEqual(
            $"space ; 'quoted' & literal{Environment.NewLine}"
                + $"{fixture.RepositoryPath}{Environment.NewLine}",
            result.StandardOutput
        );
        Assert.AreEqual($"check diagnostic{Environment.NewLine}", result.StandardError);
        Assert.IsFalse(result.TimedOut);
        using SqliteConnection connection = database.Open();
        Assert.AreEqual(
            1L,
            Sql.Scalar(
                connection,
                "SELECT COUNT(*) FROM process_leases WHERE kind='command' AND state='exited';"
            )
        );
        Assert.AreEqual(
            0L,
            Sql.Scalar(connection, "SELECT COUNT(*) FROM process_leases WHERE state<>'exited';")
        );
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task RunAsyncDeadlineExpiresReportsTimeoutAfterSettlingChild()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        ProcessRunner runner = new(database);
        string ready = Path.Combine(fixture.RootPath, "child.pid");
        string script = WriteWaitingChild(fixture);

        ProcessResult result = await runner.RunAsync(
            "pwsh",
            ["-NoLogo", "-NoProfile", "-File", script, ready],
            fixture.RepositoryPath,
            TimeSpan.FromSeconds(5),
            testContext.CancellationToken
        );

        Assert.IsTrue(result.TimedOut);
        Assert.AreNotEqual(0, result.ExitCode);
        using SqliteConnection retained = database.Open();
        using SqliteCommand command = Sql.Command(
            retained,
            "SELECT pid,state FROM process_leases;"
        );
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.IsTrue(reader.Read());
        Assert.AreEqual("exited", reader.GetString(1));
        Assert.IsFalse(IsAlive(reader.GetInt32(0)));
        Assert.IsFalse(reader.Read());
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public async Task RunAsyncUnsupportedDeadlineDoesNotStartChildOrCreateLease(int seconds)
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        ProcessRunner runner = new(database);
        string marker = Path.Combine(fixture.RootPath, "must-not-start.txt");
        string script = fixture.WriteScript(
            """
            param([string] $Marker)
            [IO.File]::WriteAllText($Marker, 'started')
            """
        );

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            runner.RunAsync(
                "pwsh",
                ["-NoProfile", "-File", script, marker],
                fixture.RepositoryPath,
                TimeSpan.FromSeconds(seconds),
                testContext.CancellationToken
            )
        );

        Assert.IsFalse(File.Exists(marker));
        using SqliteConnection connection = database.Open();
        Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM process_leases;"));
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task RunAsyncCancellationAfterReadySettlesChildAndLease()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        ProcessRunner runner = new(database);
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(testContext.CancellationToken);
        string ready = Path.Combine(fixture.RootPath, "child.pid");
        string script = WriteWaitingChild(fixture);
        Task<ProcessResult> execution = runner.RunAsync(
            "pwsh",
            ["-NoLogo", "-NoProfile", "-File", script, ready],
            fixture.RepositoryPath,
            TimeSpan.FromMinutes(5),
            cancellation.Token
        );
        int? childPid = null;
        try
        {
            childPid = await WaitForChildAsync(ready, execution, testContext.CancellationToken);
            using (SqliteConnection connection = database.Open())
            {
                Assert.AreEqual(
                    1L,
                    Sql.Scalar(
                        connection,
                        "SELECT COUNT(*) FROM process_leases WHERE pid=$pid AND state='running';",
                        null,
                        ("$pid", childPid.Value)
                    )
                );
            }

            await cancellation.CancelAsync();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
                await execution
            );

            Assert.IsFalse(
                IsAlive(childPid.Value),
                "Cancellation returned while the owned child was alive."
            );
            using SqliteConnection retained = database.Open();
            Assert.AreEqual(
                "exited",
                Sql.Scalar(
                    retained,
                    "SELECT state FROM process_leases WHERE pid=$pid;",
                    null,
                    ("$pid", childPid.Value)
                )
            );
        }
        finally
        {
            await cancellation.CancelAsync();
            await SettleTestChildAsync(execution, childPid);
        }
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task RunAsyncRegisterFailureSettlesStartedChild()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        ProcessRunner runner = new(database);
        using SqliteConnection blocker = database.Open();
        Sql.Execute(
            blocker,
            """
            CREATE TRIGGER reject_test_registration BEFORE INSERT ON process_leases
            BEGIN SELECT RAISE(ABORT, 'test registration rejected'); END;
            """
        );
        string ready = Path.Combine(fixture.RootPath, "child.pid");
        string script = WriteWaitingChild(fixture);
        Task<ProcessResult>? execution = null;
        int? childPid = null;
        try
        {
            using (SqliteTransaction transaction = blocker.BeginTransaction())
            {
                Sql.Execute(
                    blocker,
                    "UPDATE meta SET value=value WHERE key='run_id';",
                    transaction
                );
                // Hold the writer until the started child identifies itself. Registration
                // then fails without guessing which host process to inspect.
                execution = Task.Run(
                    () =>
                        runner.RunAsync(
                            "pwsh",
                            ["-NoLogo", "-NoProfile", "-File", script, ready],
                            fixture.RepositoryPath,
                            TimeSpan.FromMinutes(5),
                            testContext.CancellationToken
                        ),
                    testContext.CancellationToken
                );
                childPid = await WaitForChildAsync(ready, execution, testContext.CancellationToken);
                transaction.Commit();
            }

            SqliteException error = await Assert.ThrowsExactlyAsync<SqliteException>(async () =>
                await execution
            );

            Assert.Contains("test registration rejected", error.Message);
            Assert.IsFalse(IsAlive(childPid.Value), "Registration failure leaked a started child.");
            using SqliteConnection retained = database.Open();
            Assert.AreEqual(0L, Sql.Scalar(retained, "SELECT COUNT(*) FROM process_leases;"));
        }
        finally
        {
            if (execution is not null)
            {
                await SettleTestChildAsync(execution, childPid);
            }
        }
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task ResolveSurvivorsUnresolvedLeaseBlocksWithoutKillingUnresolvedChild()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        ProcessRunner runner = new(database);
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(testContext.CancellationToken);
        string ready = Path.Combine(fixture.RootPath, "child.pid");
        string script = WriteWaitingChild(fixture);
        Task<ProcessResult> execution = runner.RunAsync(
            "pwsh",
            ["-NoLogo", "-NoProfile", "-File", script, ready],
            fixture.RepositoryPath,
            TimeSpan.FromMinutes(5),
            cancellation.Token
        );
        int? childPid = null;
        try
        {
            childPid = await WaitForChildAsync(ready, execution, testContext.CancellationToken);
            using (SqliteConnection connection = database.Open())
            {
                // A matching PID/start identity does not authorize automatic
                // recovery when the retained lease requires explicit resolution.
                Sql.Execute(
                    connection,
                    "UPDATE process_leases SET state='unresolved' WHERE pid=$pid;",
                    null,
                    ("$pid", childPid.Value)
                );
            }

            InvalidOperationException error =
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                    runner.ResolveSurvivorsAsync(testContext.CancellationToken)
                );

            Assert.Contains("ownership resolution", error.Message);
            Assert.IsTrue(
                IsAlive(childPid.Value),
                "Unresolved ownership must block automatic termination."
            );
            using SqliteConnection retained = database.Open();
            Assert.AreEqual(
                "unresolved",
                Sql.Scalar(
                    retained,
                    "SELECT state FROM process_leases WHERE pid=$pid;",
                    null,
                    ("$pid", childPid.Value)
                )
            );
        }
        finally
        {
            await cancellation.CancelAsync();
            await SettleTestChildAsync(execution, childPid);
        }
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task ResolveSurvivorsRegisteredChildSettlesChildAndLease()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        ProcessRunner runner = new(database);
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(testContext.CancellationToken);
        string ready = Path.Combine(fixture.RootPath, "child.pid");
        string script = WriteWaitingChild(fixture);
        Task<ProcessResult> execution = runner.RunAsync(
            "pwsh",
            ["-NoLogo", "-NoProfile", "-File", script, ready],
            fixture.RepositoryPath,
            TimeSpan.FromMinutes(5),
            cancellation.Token
        );
        int? childPid = null;
        try
        {
            childPid = await WaitForChildAsync(ready, execution, testContext.CancellationToken);

            await new ProcessRunner(new RunDatabase(fixture.StatePath)).ResolveSurvivorsAsync(
                testContext.CancellationToken
            );
            ProcessResult result = await execution;

            Assert.IsFalse(result.TimedOut);
            Assert.AreNotEqual(0, result.ExitCode);
            Assert.IsFalse(IsAlive(childPid.Value));
            using SqliteConnection retained = database.Open();
            Assert.AreEqual(
                "exited",
                Sql.Scalar(
                    retained,
                    "SELECT state FROM process_leases WHERE pid=$pid;",
                    null,
                    ("$pid", childPid.Value)
                )
            );
        }
        finally
        {
            await cancellation.CancelAsync();
            await SettleTestChildAsync(execution, childPid);
        }
    }

    private static string WriteWaitingChild(RunFixture fixture) =>
        fixture.WriteScript(
            """
            param([string] $Ready)
            [IO.File]::WriteAllText($Ready + '.tmp', [string] $PID)
            [IO.File]::Move($Ready + '.tmp', $Ready)
            while ($true) { Start-Sleep -Seconds 300 }
            """
        );

    private static async Task<int> WaitForChildAsync(
        string ready,
        Task<ProcessResult> execution,
        CancellationToken cancellationToken
    )
    {
        while (!File.Exists(ready))
        {
            if (execution.IsCompleted)
            {
                await execution;
                Assert.Fail("The child finished without announcing readiness.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }

        return int.Parse(
            await File.ReadAllTextAsync(ready, cancellationToken),
            CultureInfo.InvariantCulture
        );
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task SettleTestChildAsync(Task<ProcessResult> execution, int? pid)
    {
        // Cleanup is limited to the exact child that wrote our fixture's readiness
        // file. The assertion above must still expose a production settlement bug.
        if (pid is int childPid)
        {
            try
            {
                using Process child = Process.GetProcessById(childPid);
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    await child
                        .WaitForExitAsync(CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
            }
            catch (ArgumentException)
            {
                // Already settled.
            }
        }

        try
        {
            await execution.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Expected for the cancellation scenario.
        }
        catch (SqliteException)
        {
            // Expected for the registration-failure scenario.
        }
    }
}
