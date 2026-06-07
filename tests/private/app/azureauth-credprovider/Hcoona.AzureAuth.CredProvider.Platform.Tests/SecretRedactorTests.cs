using Hcoona.AzureAuth.CredProvider.Platform.Redaction;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class SecretRedactorTests
{
    [Fact]
    public void RedactMasksKnownSecret()
    {
        var redactor = new SecretRedactor(["secret"]);

        var redacted = redactor.Redact("value=secret");

        Assert.Equal($"value={SecretRedactor.DefaultMask}", redacted);
    }

    [Fact]
    public void RedactMasksEveryOccurrenceDeterministically()
    {
        var redactor = new SecretRedactor(["secret"]);

        var redacted = redactor.Redact("secret and secret");

        Assert.Equal($"{SecretRedactor.DefaultMask} and {SecretRedactor.DefaultMask}", redacted);
    }

    [Fact]
    public void RedactPrefersLongestSecretToAvoidPartialLeak()
    {
        var redactor = new SecretRedactor(["abc", "abcdef"]);

        var redacted = redactor.Redact("token=abcdef");

        Assert.Equal($"token={SecretRedactor.DefaultMask}", redacted);
    }

    [Fact]
    public void RedactMergesPartiallyOverlappingSecretsToAvoidPartialLeak()
    {
        var redactor = new SecretRedactor(["abc", "bcde"]);

        var redacted = redactor.Redact("token=abcde");

        Assert.Equal($"token={SecretRedactor.DefaultMask}", redacted);
    }

    [Fact]
    public void RedactDoesNotEmitExactMaskSecret()
    {
        var redactor = new SecretRedactor([SecretRedactor.DefaultMask]);

        var redacted = redactor.Redact($"value={SecretRedactor.DefaultMask}");

        Assert.DoesNotContain(SecretRedactor.DefaultMask, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactDoesNotEmitSecretThatIsSubstringOfMask()
    {
        const string Secret = "REDACTED";
        var redactor = new SecretRedactor([Secret]);

        var redacted = redactor.Redact($"value={Secret}");

        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactDoesNotSynthesizeSecretFromMaskAndAdjacentText()
    {
        AssertRedactsWithoutConfiguredSecrets(["password", "ED]tail"], "passwordtail");
    }

    [Fact]
    public void RedactDoesNotSynthesizeMaskSecretByDeletingMiddleText()
    {
        AssertRedactsWithoutConfiguredSecrets(["REDACTED", "x"], "REDxACTED");
    }

    [Fact]
    public void RedactDoesNotSynthesizeMaskSecretByDeletingLateMiddleText()
    {
        AssertRedactsWithoutConfiguredSecrets(["REDACTED", "x"], "REDAxCTED");
    }

    [Fact]
    public void RedactDoesNotSynthesizeMaskSecretByDeletingEarlyMiddleText()
    {
        AssertRedactsWithoutConfiguredSecrets(["REDACTED", "X"], "REXDACTED");
    }

    [Fact]
    public void RedactIgnoresNullAndEmptySecrets()
    {
        var redactor = new SecretRedactor([null, "", "secret"]);

        var redacted = redactor.Redact("empty stays; secret changes");

        Assert.Equal($"empty stays; {SecretRedactor.DefaultMask} changes", redacted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RedactReturnsNullOrEmptyValueUnchanged(string? value)
    {
        var redactor = new SecretRedactor(["secret"]);

        var redacted = redactor.Redact(value);

        Assert.Equal(value, redacted);
    }

    [Fact]
    public void EmptyRedactorReturnsValueUnchanged()
    {
        const string Value = "value=secret";

        var redacted = SecretRedactor.Empty.Redact(Value);

        Assert.Equal(Value, redacted);
    }

    private static void AssertRedactsWithoutConfiguredSecrets(string[] secrets, string input)
    {
        var redactor = new SecretRedactor(secrets);

        var redacted = redactor.Redact(input);

        Assert.NotNull(redacted);
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
        }
    }
}
