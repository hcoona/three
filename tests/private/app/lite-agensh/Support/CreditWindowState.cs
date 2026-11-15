using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests.Support;

internal sealed class CreditWindowState : IDisposable
{
    private readonly ScratchDirectory scratch = new();

    internal CreditWindowState(decimal campaignAllowance, decimal runAllowance)
    {
        RunAllowance = runAllowance;
        Ledger = new(LedgerPath, campaignAllowance);
    }

    internal string LedgerPath => scratch.PathFor("campaign.sqlite");

    internal decimal RunAllowance { get; }

    internal CreditLedger Ledger { get; }

    internal CreditReservation Reserve(string worker, bool shared = true, decimal allowance = 0) =>
        Ledger.Reserve(
            "run",
            worker,
            allowance,
            RunAllowance,
            scratch.PathFor(worker + "-" + Guid.NewGuid().ToString("N") + ".usage"),
            shared
        );

    internal CreditReservation Unrelated() =>
        Ledger.Reserve("other", "worker9", 30, 30, scratch.PathFor("other.usage"));

    internal CreditReservation Read(string id) => Ledger.Reservations().Single(row => row.Id == id);

    internal void Start(CreditReservation reservation)
    {
        Ledger.MarkStarted(reservation.Id);
        Ledger.AttachSession(reservation.Id, reservation.SessionId!);
    }

    internal void Dispatch(CreditReservation reservation) =>
        Ledger.BeginDispatch("run", reservation.Id, RunAllowance);

    internal long Count(string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = LedgerPath,
                Pooling = false,
            }.ToString()
        );
        connection.Open();
        return (long)Sql.Scalar(connection, sql, null, parameters)!;
    }

    internal string NewEvidencePath() => scratch.PathFor(Guid.NewGuid().ToString("N") + ".usage");

    internal void AssertAccounting(
        decimal campaignCommitted,
        decimal runCommitted,
        decimal spent,
        decimal held,
        bool uncertain = false
    )
    {
        JsonObject status = Ledger.Status("run", RunAllowance);
        Assert.AreEqual(campaignCommitted, status["campaignCommitted"]!.GetValue<decimal>());
        Assert.AreEqual(runCommitted, status["runCommitted"]!.GetValue<decimal>());
        Assert.AreEqual(spent, status["actualReported"]!.GetValue<decimal>());
        Assert.AreEqual(held, status["outstandingAuthorization"]!.GetValue<decimal>());
        Assert.AreEqual(uncertain, status["uncertain"]!.GetValue<bool>());
    }

    public void Dispose() => scratch.Dispose();
}
