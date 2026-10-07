using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonNativeActivitiesTests
{
    private static readonly string[] OperationIds = ["root-quality", "control-no-dev"];
    private static readonly string[] QualityGroups = ["dev", "quality"];
    private static readonly string[] FeatureExtras = ["feature-one"];

    private static PythonSupplementRequest Request() => new("/endpoint", "/tool/python",
        "/owned/cache", [],
        [new("root-quality", "/endpoint", ["consumer"], false),
            new("control-no-dev", "/endpoint", ["control"], true)],
        [new("ordinary-marker", "python_version >= '3.14'", []),
            new("extra-marker", "extra == 'feature-one'", ["feature-one"])],
        [new("workspace-python", ">=3.14")]);

    // Controlled native response shape; this is not another native observation.
    private static JsonObject Response() => JsonNode.Parse("""
        {
          "workspace_root": "/endpoint",
          "interpreter": "/tool/python",
          "group_operations": [
            {"id": "control-no-dev", "groups": []},
            {"id": "root-quality", "groups": ["dev", "quality"]}
          ],
          "markers": [
            {"id": "extra-marker", "active": false},
            {"id": "ordinary-marker", "active": true}
          ],
          "python_constraints": [{"id": "workspace-python", "active": false}]
        }
        """)!.AsObject();

    private static PythonNativeActivities Read(JsonObject response,
        PythonSupplementRequest? request = null)
    {
        using JsonDocument document = JsonDocument.Parse(response.ToJsonString());
        return PythonNativeActivities.Read(request ?? Request(),
            new(document.RootElement, []));
    }

    [TestMethod]
    public void NativeAnswersRetainOperationIdsAndFalseActivity()
    {
        PythonSupplementRequest request = Request();
        PythonNativeActivities result = Read(Response(), request);
        CollectionAssert.AreEquivalent(OperationIds,
            result.Groups.Keys.ToArray());
        CollectionAssert.AreEqual(QualityGroups, result.Groups["root-quality"]);
        Assert.IsTrue(result.Markers[request.Markers[0].Id]);
        Assert.IsFalse(result.Markers[request.Markers[1].Id]);
        Assert.IsFalse(result.PythonConstraints[request.PythonConstraints[0].Id]);
        CollectionAssert.AreEqual(FeatureExtras, request.Markers[1].Extras);
        Assert.AreEqual("extra == 'feature-one'", request.Markers[1].Expression);
    }

    [TestMethod]
    public void EmptyAndNoDevRequestsRemainDistinct()
    {
        PythonNativeActivities result = Read(Response());
        Assert.IsEmpty(result.Groups["control-no-dev"]);
        Assert.HasCount(2, result.Groups["root-quality"]);
        JsonObject empty = Response();
        foreach (string field in new[] { "group_operations", "markers", "python_constraints" })
            empty[field] = new JsonArray();
        PythonNativeActivities noOperations = Read(empty, Request() with
        {
            GroupOperations = [],
            Markers = [],
            PythonConstraints = []
        });
        Assert.IsEmpty(noOperations.Groups);
        Assert.IsEmpty(noOperations.Markers);
        Assert.IsEmpty(noOperations.PythonConstraints);
    }

    [TestMethod]
    [DataRow("group_operations", "missing")]
    [DataRow("markers", "missing")]
    [DataRow("python_constraints", "missing")]
    [DataRow("group_operations", "extra")]
    [DataRow("markers", "extra")]
    [DataRow("python_constraints", "extra")]
    [DataRow("group_operations", "duplicate")]
    [DataRow("markers", "duplicate")]
    [DataRow("python_constraints", "duplicate")]
    [DataRow("group_operations", "null")]
    [DataRow("markers", "null")]
    [DataRow("python_constraints", "null")]
    [DataRow("group_operations", "wrong-type")]
    [DataRow("markers", "wrong-type")]
    [DataRow("python_constraints", "wrong-type")]
    [DataRow("group_operations", "missing-field")]
    [DataRow("markers", "missing-field")]
    [DataRow("python_constraints", "missing-field")]
    [DataRow("group_operations", "duplicate-group")]
    public void MissingOrAmbiguousNativeAnswersFail(string field, string fault)
    {
        JsonObject response = Response();
        JsonArray answers = response[field]!.AsArray();
        switch (fault)
        {
            case "missing": answers.RemoveAt(0); break;
            case "extra":
                JsonObject extra = answers[0]!.DeepClone().AsObject();
                extra["id"] = "unexpected-native-answer";
                answers.Add(extra);
                break;
            case "duplicate": answers.Add(answers[0]!.DeepClone()); break;
            case "null": answers[0]!["id"] = null; break;
            case "wrong-type":
                answers[0]![field == "group_operations" ? "groups" : "active"] = "false";
                break;
            case "missing-field": response.Remove(field); break;
            case "duplicate-group":
                answers[0]!["groups"] = new JsonArray("dev", "dev");
                break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Read(response));
    }

    [TestMethod]
    [DataRow("workspace_root")]
    [DataRow("interpreter")]
    public void ChangedEndpointOrInterpreterCannotSupplyActivity(string field)
    {
        JsonObject response = Response();
        response[field] = "/unrelated/private-subject";
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(
            () => Read(response));
        Assert.AreEqual("Python activity subject differs from its request.", error.Message);
        Assert.DoesNotContain("private-subject", error.Message);
    }

    [TestMethod]
    [DataRow("group_operations", false)]
    [DataRow("markers", false)]
    [DataRow("python_constraints", false)]
    [DataRow("group_operations", true)]
    [DataRow("markers", true)]
    [DataRow("python_constraints", true)]
    public void AmbiguousRequestIdsCannotBeJoined(string field, bool empty)
    {
        PythonSupplementRequest request = Request();
        string id = empty ? " " : "workspace-python";
        request = field switch
        {
            "group_operations" => request with
            {
                GroupOperations =
                [request.GroupOperations[0], request.GroupOperations[0] with
                    { Id = empty ? " " : request.GroupOperations[0].Id }]
            },
            "markers" => request with
            {
                Markers = [request.Markers[0], request.Markers[1] with
                    { Id = empty ? " " : request.Markers[0].Id }]
            },
            _ => request with
            {
                PythonConstraints = empty ? [new(id, ">=3.14")] :
                    [request.PythonConstraints[0], request.PythonConstraints[0]]
            }
        };
        Assert.ThrowsExactly<InvalidDataException>(() => Read(Response(), request));
    }
}
