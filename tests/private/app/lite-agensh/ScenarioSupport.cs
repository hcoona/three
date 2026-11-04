using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

internal sealed class ScenarioClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan interval) => now += interval;
}

internal static class Scenario
{
    public static string DotnetHost =>
        Path.GetFullPath(
            Path.Combine(
                System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                "..",
                "..",
                "..",
                "dotnet.exe"
            )
        );

    public static JsonObject Data(JsonObject result)
    {
        Assert.IsTrue(result["ok"]!.GetValue<bool>(), result.ToJsonString());
        Assert.AreEqual("ok", result["code"]!.GetValue<string>());
        return result["data"]!.AsObject();
    }

    public static void Failure(JsonObject result, string code)
    {
        Assert.IsFalse(result["ok"]!.GetValue<bool>(), result.ToJsonString());
        Assert.AreEqual(code, result["code"]!.GetValue<string>(), result.ToJsonString());
    }

    public static JsonObject Call(
        CollaborationOperations operations,
        string actor,
        string name,
        object arguments
    ) => Data(operations.Invoke(actor, name, Json.SerializePayload(arguments)));

    public static JsonObject[] Items(JsonObject data) =>
        data["items"]!.AsArray().OfType<JsonObject>().ToArray();

    public static long Id(JsonObject data) => data["id"]!.GetValue<long>();
}
