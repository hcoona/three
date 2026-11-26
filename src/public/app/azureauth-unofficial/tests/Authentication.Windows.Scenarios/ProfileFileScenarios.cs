using System.Text;
using System.Text.Json;
using Authentication.Core;
using Authentication.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Authentication.Windows.Scenarios;

// These scenarios need a Windows host. Files stay beneath the run's TEMP directory
// and are intentionally retained; the suite runs under a fresh TEMP root (see ProcessFixture).
// Only the file reader uses Windows; provider and UI are controlled substitutes.
[TestClass]
public sealed class ProfileFileScenarios
{
    private const string Client = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Tenant = "11111111-2222-3333-4444-555555555555";
    private const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation";
    private static readonly TimeSpan FixtureLimit = TimeSpan.FromSeconds(5);
    private const string ProfileJson = """
        {
          "schemaVersion": 1,
          "name": "synthetic-file-profile",
          "clientId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          "cloud": "public",
          "tenantPolicy": { "kind": "multitenant" },
          "integration": "windows-wam",
          "registration": {
            "owner": "Synthetic external owner",
            "externalDependency": true,
            "supportNotice": "Synthetic configuration; no support commitment."
          }
        }
        """;

    [TestMethod]
    [DataRow("personal@example.test")]
    [DataRow("work@example.test")]
    public async Task ExplicitFilePreservesSelectedProfileAndRequest(string email)
    {
        var path = CreatePath();
        var bytes = Encoding.UTF8.GetBytes(ProfileJson);
        File.WriteAllBytes(path, bytes);
        ClientProfile? admitted = null;
        AuthenticationRequest? received = null;

        var result = await InvokeAsync(path, email, profile =>
        {
            admitted = profile;
            return new SyntheticProvider(email, request => received = request);
        });

        AssertSuccess(result, email);
        Assert.IsNotNull(admitted);
        Assert.AreEqual(Guid.Parse(Client), admitted.ClientId);
        Assert.AreEqual("synthetic-file-profile", admitted.Name);
        Assert.IsNotNull(received);
        Assert.AreEqual(email, received.AccountEmail);
        Assert.AreEqual(Guid.Parse(Tenant), received.ExactTenant);
        CollectionAssert.AreEqual(new[] { Scope }, received.Scopes.ToArray());
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    [DataRow(65536)]
    [DataRow(65537)]
    public async Task FileSizeLimitAppliesBeforeAuthentication(int length)
    {
        var path = CreatePath();
        var bytes = Enumerable.Repeat((byte)' ', length).ToArray();
        Encoding.UTF8.GetBytes(ProfileJson).CopyTo(bytes, 0);
        File.WriteAllBytes(path, bytes);
        var providerCreated = false;

        var result = await InvokeAsync(path, "personal@example.test", _ =>
        {
            providerCreated = true;
            return new SyntheticProvider("personal@example.test");
        });

        if (length == 65536)
        {
            AssertSuccess(result, "personal@example.test");
        }
        else
        {
            AssertConfigurationFailure(result);
            Assert.IsFalse(providerCreated);
        }
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    public async Task ReplacingFileAfterAdmissionCannotChangeTheInFlightProfile()
    {
        var path = CreatePath();
        File.WriteAllText(path, ProfileJson, new UTF8Encoding(false, true));
        ClientProfile? admitted = null;
        var replacement = ProfileJson.Replace(Client, "bbbbbbbb-cccc-dddd-eeee-ffffffffffff")
            .Replace("synthetic-file-profile", "replacement-file-profile");

        var result = await InvokeAsync(path, "personal@example.test", profile =>
        {
            admitted = profile;
            File.WriteAllText(path, replacement, new UTF8Encoding(false, true));
            return new SyntheticProvider("personal@example.test");
        });

        AssertSuccess(result, "personal@example.test");
        Assert.IsNotNull(admitted);
        Assert.AreEqual(Guid.Parse(Client), admitted.ClientId);
        Assert.AreEqual("synthetic-file-profile", admitted.Name);
        Assert.AreEqual(replacement, File.ReadAllText(path));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("directory")]
    [DataRow("malformed-json")]
    [DataRow("invalid-utf8")]
    [DataRow("sharing-denied")]
    public async Task UnreadableOrInvalidFileStopsBeforeProviderConstruction(string defect)
    {
        var path = CreatePath();
        FileStream? competingWriter = null;
        switch (defect)
        {
            case "directory":
                Directory.CreateDirectory(path);
                break;
            case "malformed-json":
                File.WriteAllText(path, "SYNTHETIC_PRIVATE_CONFIGURATION_MARKER");
                break;
            case "invalid-utf8":
                File.WriteAllBytes(path, [0xff, 0xfe, 0xfd]);
                break;
            case "sharing-denied":
                File.WriteAllText(path, ProfileJson);
                competingWriter = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                break;
        }

        var providerCreated = false;
        try
        {
            var result = await InvokeAsync(path, "personal@example.test", _ =>
            {
                providerCreated = true;
                return new SyntheticProvider("personal@example.test");
            });
            AssertConfigurationFailure(result);
            Assert.IsFalse(providerCreated);
        }
        finally
        {
            competingWriter?.Dispose();
        }
    }

    private static string CreatePath()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("These scenarios require the admitted Windows host.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "profile-scenario-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "profile.json");
    }

    private static async Task<SerializedResult> InvokeAsync(string path, string email,
        Func<ClientProfile, IAuthenticationProvider> createProvider)
    {
        var host = new ControlledHost();
        var entry = host.Clock.GetTimestamp();
        using var invocation = new RequestInvocation([
            "authenticate", "--protocol", "1", "--profile", path,
            "--account-email", email, "--scope", Scope,
            "--interaction", "non-interactive-only", "--tenant", Tenant,
        ], host, entry);
        await invocation.RunAsync(new WindowsProfileSource(), createProvider).WaitAsync(FixtureLimit);
        Assert.IsTrue(invocation.TryCommitResult(out var result));
        Assert.IsNotNull(result);
        await invocation.OperationCompletion.WaitAsync(FixtureLimit);
        return result;
    }

    private static void AssertSuccess(SerializedResult result, string email)
    {
        using var json = JsonDocument.Parse(result.Utf8Json);
        Assert.AreEqual("success", json.RootElement.GetProperty("outcome").GetString());
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(email, json.RootElement.GetProperty("accountEmail").GetString());
        Assert.AreEqual("SYNTHETIC_FILE_SCENARIO_TOKEN", json.RootElement.GetProperty("accessToken").GetString());
    }

    private static void AssertConfigurationFailure(SerializedResult result)
    {
        using var json = JsonDocument.Parse(result.Utf8Json);
        var value = json.RootElement;
        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual("invalid_request", value.GetProperty("outcome").GetString());
        Assert.AreEqual("invalid_configuration", value.GetProperty("reason").GetString());
        CollectionAssert.AreEquivalent(new[] { "protocol", "outcome", "reason" },
            value.EnumerateObject().Select(property => property.Name).ToArray());
    }

    private sealed class ControlledHost : IRequestHost
    {
        public TimeProvider Clock => TimeProvider.System;
        public Task<nint> OpenInteractionAsync(CancellationToken cancellationToken) => Task.FromResult<nint>(0);
        public Task CloseInteractionAsync() => Task.CompletedTask;
    }

    private sealed class SyntheticProvider(string email, Action<AuthenticationRequest>? observe = null)
        : IAuthenticationProvider
    {
        private readonly object accountHandle = new();

        public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderAccount>>([new(email, accountHandle)]);

        public Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request, ProviderAccount account,
            Guid operationId, CancellationToken cancellationToken)
        {
            observe?.Invoke(request);
            return Task.FromResult(new TokenCandidate("SYNTHETIC_FILE_SCENARIO_TOKEN", email,
                Guid.Parse(Tenant), [Scope], "Bearer", DateTimeOffset.UtcNow.AddMinutes(10), operationId));
        }

        public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request, ProviderAccount? account,
            string? claims, nint parentWindow, Guid operationId, CancellationToken cancellationToken) =>
            Task.FromException<TokenCandidate>(new ProviderFailureException(AuthenticationFailure.MechanismUnavailable));
    }
}
