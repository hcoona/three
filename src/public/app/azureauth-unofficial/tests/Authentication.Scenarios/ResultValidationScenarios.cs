using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WindowsResultValidation;

namespace Authentication.Scenarios;

[TestClass]
public sealed class ResultValidationScenarios
{
    private const string Email = "selected@example.invalid";
    private const string Scope = "https://resource.invalid/read";
    private static readonly DateTimeOffset Received = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ResultExpectation Expected(Outcome outcome = Outcome.Success, string[]? scopes = null,
        bool association = false) => new()
    {
        Email = Email, Scopes = scopes ?? [Scope], ExpectedOutcome = outcome,
        ExpectedRoute = outcome == Outcome.Success ? Route.Silent : Route.None,
        DefaultAssociationIndependentlyAccepted = association,
    };

    private static byte[] Success(string email = Email, string interaction = "silent") => Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new
        {
            protocol = 1, outcome = "success", accessToken = "SYNTHETIC_TOKEN_NOT_A_CREDENTIAL",
            tokenType = "Bearer", expiresOn = "2030-01-01T01:00:00Z", accountEmail = email,
            tenantId = "11111111-2222-4333-8444-555555555555",
            authority = "https://login.microsoftonline.com/11111111-2222-4333-8444-555555555555",
            scopes = new[] { Scope }, mechanism = "wam", interaction, warnings = Array.Empty<string>(),
        }) + "\n");

    [TestMethod]
    public void ValidSuccessReturnsOnlyFixedConclusions()
    {
        var result = ProtocolResult.Validate(Success(), 0, Expected(), Received);
        Assert.IsTrue(result.Passed && result.ProtocolValid && result.MetadataValid);
        Assert.AreEqual(Outcome.Success, result.Outcome);
        Assert.AreEqual(Route.Silent, result.Route);
        string safe = JsonSerializer.Serialize(result);
        Assert.IsFalse(safe.Contains(Email, StringComparison.Ordinal));
        Assert.IsFalse(safe.Contains("SYNTHETIC_TOKEN", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("other@example.invalid", "silent", 0u)]
    [DataRow(Email, "interactive", 0u)]
    [DataRow(Email, "silent", 1u)]
    public void SelectionPermissionAndExitMismatchCannotPass(string email, string interaction, uint exit)
    {
        Assert.ThrowsExactly<InvalidResultException>(() => ProtocolResult.Validate(
            Success(email, interaction), exit, Expected(), Received));
    }

    [TestMethod]
    [DataRow("interaction_required", Outcome.InteractionRequired)]
    [DataRow("invalid_request", Outcome.InvalidRequest)]
    [DataRow("account_ambiguous", Outcome.AccountAmbiguous)]
    [DataRow("identity_validation_failed", Outcome.IdentityValidationFailed)]
    [DataRow("cancelled", Outcome.Cancelled)]
    [DataRow("denied", Outcome.Denied)]
    [DataRow("mechanism_unavailable", Outcome.MechanismUnavailable)]
    [DataRow("temporarily_unavailable", Outcome.TemporarilyUnavailable)]
    [DataRow("timeout", Outcome.Timeout)]
    [DataRow("internal_failure", Outcome.InternalFailure)]
    public void TypedFailurePreservesOutcomeWithoutForwardingReason(string outcome, Outcome expected)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { protocol = 1, outcome, reason = "unknown_private_marker" }) + "\n");
        var result = ProtocolResult.Validate(bytes, 1, Expected(expected), Received);
        Assert.IsTrue(result.Passed && result.ProtocolValid);
        Assert.IsFalse(result.MetadataValid);
        Assert.AreEqual(expected, result.Outcome);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("unknown_private_marker", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("{\"protocol\":1,\"protocol\":1,\"outcome\":\"timeout\",\"reason\":\"safe\"}\n")]
    [DataRow("{\"protocol\":1,\"outcome\":\"timeout\",\"reason\":\"safe\",\"extra\":{\"x\":1,\"x\":2}}\n")]
    [DataRow("{\"protocol\":1,\"outcome\":\"timeout\",\"reason\":\"safe\",\"accessToken\":\"private_marker\"}\n")]
    [DataRow("{\"protocol\":1,\"outcome\":\"timeout\",\"reason\":\"safe\",\"refreshToken\":\"private_marker\"}\n")]
    [DataRow("private_marker")]
    public void InvalidInputProducesOnlyFixedException(string input)
    {
        var error = Assert.ThrowsExactly<InvalidResultException>(() => ProtocolResult.Validate(
            Encoding.UTF8.GetBytes(input), 1, Expected(Outcome.Timeout), Received));
        Assert.AreEqual("Result validation failed.", error.Message);
        Assert.AreEqual(nameof(InvalidResultException), error.ToString());
        Assert.IsNull(error.InnerException);
    }

    [TestMethod]
    public void DefaultScopeRequiresIndependentAssociation()
    {
        string[] scopes = ["https://resource.invalid/.default"];
        Assert.ThrowsExactly<InvalidResultException>(() => ProtocolResult.Validate(
            Success(), 0, Expected(scopes: scopes), Received));
        Assert.IsTrue(ProtocolResult.Validate(Success(), 0, Expected(scopes: scopes, association: true), Received).Passed);
    }

    [TestMethod]
    public void InvalidUtf8AndOversizeInputAreRejectedWithoutSourceExceptions()
    {
        Assert.ThrowsExactly<InvalidResultException>(() => ProtocolResult.Validate(
            new byte[] { 123, 255, 125, 10 }, 0, Expected(), Received));
        Assert.ThrowsExactly<InvalidResultException>(() => ProtocolResult.Validate(
            new byte[1048577], 0, Expected(), Received));
    }
}
