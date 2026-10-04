using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PnpmScriptReaderTests(TestContext context)
{
    [TestMethod]
    [DataRow(".")]
    [DataRow("packages/space project")]
    public async Task ReadPreservesOpaqueScriptsAndDiscoveredIdentity(string directory)
    {
        const string command = "opaque $VALUE; pnpm run other && echo \"雪\"\nsecond line";
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "native scripts"));
        string[]? actualArguments = null;
        var reader = new PnpmScriptReader(root, (arguments, token) =>
        {
            token.ThrowIfCancellationRequested();
            actualArguments = arguments;
            return Task.FromResult(JsonSerializer.Serialize(new Dictionary<string, string>
            { ["build"] = command, ["test"] = "" }));
        });

        PnpmScripts value = await reader.ReadAsync(new(directory, null, []),
            context.CancellationToken);

        Assert.AreEqual(directory, value.Directory);
        Assert.HasCount(2, value.Scripts);
        Assert.AreEqual(command, value.Scripts["build"]);
        Assert.AreEqual("", value.Scripts["test"]);
        CollectionAssert.AreEqual(new[] { "--dir", Path.GetFullPath(directory, root),
            "pkg", "get", "scripts", "--json" }, actualArguments);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" \n")]
    [DataRow("{}")]
    public async Task ReadAcceptsAbsentOrEmptyScripts(string output)
    {
        var reader = new PnpmScriptReader(".", (_, _) => Task.FromResult(output));

        PnpmScripts value = await reader.ReadAsync(new(".", null, []), context.CancellationToken);

        Assert.AreEqual(".", value.Directory);
        Assert.IsEmpty(value.Scripts);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{\"build\":null}")]
    [DataRow("{\"build\":42}")]
    [DataRow("{\"build\":{},\"test\":\"opaque\"}")]
    [DataRow("{\"build\":\"one\",\"build\":\"two\"}")]
    public async Task ReadRejectsMalformedOrAmbiguousScripts(string output)
    {
        var reader = new PnpmScriptReader(".", (_, _) => Task.FromResult(output));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadAsync(
            new("packages/project", null, []), context.CancellationToken));
    }

    [TestMethod]
    public async Task ReadRejectsInvalidJson()
    {
        var reader = new PnpmScriptReader(".", (_, _) => Task.FromResult("{"));

        await Assert.ThrowsAsync<JsonException>(() => reader.ReadAsync(
            new("packages/project", null, []), context.CancellationToken));
    }

    [TestMethod]
    [DataRow("../outside")]
    [DataRow("/outside")]
    [DataRow("packages/../outside")]
    [DataRow("packages\\outside")]
    public async Task ReadRejectsInvalidDiscoveredDirectory(string directory)
    {
        var reader = new PnpmScriptReader(".", (_, _) =>
            throw new AssertFailedException("Invalid scope must not reach the native query."));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadAsync(
            new(directory, null, []), context.CancellationToken));
    }

    [TestMethod]
    public async Task ReadPropagatesNativeFailureAndCancellation()
    {
        var failure = new InvalidDataException("native failure");
        var reader = new PnpmScriptReader(".", (_, _) => Task.FromException<string>(failure));

        InvalidDataException actual = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => reader.ReadAsync(new(".", null, []), context.CancellationToken));

        Assert.AreSame(failure, actual);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => reader.ReadAsync(new(".", null, []), cancellation.Token));
    }

    [TestMethod]
    public async Task ReadRejectsCancellationAfterNativeQuery()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        var reader = new PnpmScriptReader(".", (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult("{}");
        });

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => reader.ReadAsync(new(".", null, []), cancellation.Token));
    }
}
