using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using LiteAgensh;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class NativeMetadataTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeErrorPreservesItsReasonUnlessTheCallerActuallyCanceled(bool canceled)
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        await using NativeSdkSession native = new(
            database,
            new ProcessRunner(database),
            fixture.RepositoryPath,
            isolated: false
        );
        SessionEvent error = SessionEvent.FromJson(
            """
            {
              "id":"00000000-0000-0000-0000-000000000001",
              "timestamp":"2026-01-01T00:00:00Z",
              "type":"session.error",
              "data":{
                "errorType":"query",
                "message":"Prompt exceeds model input budget.",
                "originatingMessageId":"00000000-0000-0000-0000-000000000002"
              }
            }
            """
        )!;
        native.RecordEvent(error);
        native.RecordEvent(SessionEvent.FromJson(
            """
            {
              "id":"00000000-0000-0000-0000-000000000003",
              "timestamp":"2026-01-01T00:00:01Z",
              "type":"session.error",
              "data":{"errorType":"query","message":"Later abort error."}
            }
            """
        )!);
        using CancellationTokenSource stop = new();
        if (canceled)
        {
            await stop.CancelAsync();
            var interrupted = Assert.ThrowsExactly<OperationCanceledException>(
                () => native.CheckEvents(stop.Token)
            );
            Assert.AreEqual(stop.Token, interrupted.CancellationToken);
        }
        else
        {
            OperationException failed = Assert.ThrowsExactly<OperationException>(
                () => native.CheckEvents(stop.Token)
            );
            Assert.AreEqual("native_stopped", failed.Code);
            Assert.Contains("query", failed.Message);
            Assert.Contains("Prompt exceeds model input budget.", failed.Message);
            Assert.Contains("00000000-0000-0000-0000-000000000002", failed.Message);
            Assert.DoesNotContain("Later abort error", failed.Message);
        }
    }

    // Pinned SDK DTOs used by the real application's metadata guard.
#pragma warning disable GHCP001
    [TestMethod]
    [DataRow("metadata-session")]
    [DataRow("active-session")]
    [DataRow("cwd")]
    [DataRow("owned")]
    [DataRow("remote")]
    [DataRow("missing-limits")]
    [DataRow("unknown-limit")]
    [DataRow("lower-limit")]
    [DataRow("higher-limit")]
    [DataRow("adjacent-limit")]
    public void RealMetadataGuardRejectsMismatchAndAcceptsMatchingIdentityAndLimit(string scenario)
    {
        using RunFixture fixture = new();
        SessionMetadataSnapshot metadata = new()
        {
            SessionId = "controlled-session",
            WorkingDirectory = fixture.RepositoryPath,
            SessionLimits = new SessionLimitsConfig { MaxAiCredits = 60 },
        };
        string active = "controlled-session";
        switch (scenario)
        {
            case "metadata-session":
                metadata.SessionId = "other-session";
                break;
            case "active-session":
                active = "other-session";
                break;
            case "cwd":
                metadata.WorkingDirectory = fixture.StatePath;
                break;
            case "owned":
                metadata.AlreadyInUse = true;
                break;
            case "remote":
                metadata.IsRemote = true;
                break;
            case "missing-limits":
                metadata.SessionLimits = null;
                break;
            case "unknown-limit":
                metadata.SessionLimits!.MaxAiCredits = null;
                break;
            case "lower-limit":
                metadata.SessionLimits!.MaxAiCredits = 59;
                break;
            case "higher-limit":
                metadata.SessionLimits!.MaxAiCredits = 61;
                break;
            case "adjacent-limit":
                metadata.SessionLimits!.MaxAiCredits = Math.BitIncrement(60);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            NativeSdkSession.ValidateMetadata(
                metadata,
                active,
                "controlled-session",
                fixture.RepositoryPath,
                60
            )
        );

        Assert.AreEqual("credit_unknown", error.Code);
        NativeSdkSession.ValidateMetadata(
            new SessionMetadataSnapshot
            {
                SessionId = "controlled-session",
                WorkingDirectory =
                    fixture.RepositoryPath.ToUpperInvariant() + Path.DirectorySeparatorChar,
                SessionLimits = new SessionLimitsConfig { MaxAiCredits = 60 },
            },
            "controlled-session",
            "controlled-session",
            fixture.RepositoryPath,
            60
        );
    }

    [TestMethod]
    public void FractionalAppliedCapUsesTheRealConservativeConversion()
    {
        using RunFixture fixture = new();
        SessionMetadataSnapshot metadata = new()
        {
            SessionId = "controlled-session",
            WorkingDirectory = fixture.RepositoryPath,
            SessionLimits = new SessionLimitsConfig { MaxAiCredits = 30.12345678901229d },
        };
        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            NativeSdkSession.ValidateMetadata(
                metadata,
                "controlled-session",
                "controlled-session",
                fixture.RepositoryPath,
                30.12345678901229m
            )
        );
        Assert.AreEqual("credit_unknown", error.Code);
        metadata.SessionLimits.MaxAiCredits = 30.123456789012287d;
        NativeSdkSession.ValidateMetadata(
            metadata,
            "controlled-session",
            "controlled-session",
            fixture.RepositoryPath,
            30.12345678901229m
        );
    }

    [TestMethod]
    public void OptionalRecoveryCapDoesNotWaiveIdentityOrOwnershipValidation()
    {
        using RunFixture fixture = new();
        SessionMetadataSnapshot metadata = new()
        {
            SessionId = "controlled-session",
            WorkingDirectory = fixture.RepositoryPath,
            SessionLimits = null,
        };
        NativeSdkSession.ValidateMetadata(
            metadata,
            "controlled-session",
            "controlled-session",
            fixture.RepositoryPath,
            null
        );
        metadata.AlreadyInUse = true;

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            NativeSdkSession.ValidateMetadata(
                metadata,
                "controlled-session",
                "controlled-session",
                fixture.RepositoryPath,
                null
            )
        );

        Assert.AreEqual("credit_unknown", error.Code);
    }
#pragma warning restore GHCP001
}
