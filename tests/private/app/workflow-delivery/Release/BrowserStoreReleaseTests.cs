using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class BrowserStoreReleaseTests(TestContext context)
{
    private const string Version = "1.2.0.7";
    private const string AmoListed =
        """{"version":"1.2.0.7","channel":"listed","file":{"status":""";

    private static string ChromeStatus(string? submitted, string? published)
    {
        if (submitted == "takenDown") return """{"takenDown":true}""";
        static string Revision(string name, string? value) => value is null ? "" :
            $$"""
            "{{name}}":{"state":"{{value.Split('@')[0]}}",
            "distributionChannels":[{"crxVersion":"{{value.Split('@')[1]}}"}]}
            """.ReplaceLineEndings("");
        string[] parts = [Revision("submittedItemRevisionStatus", submitted),
            Revision("publishedItemRevisionStatus", published)];
        return "{" + string.Join(',', parts.Where(part => part.Length > 0)) + "}";
    }

    [TestMethod]
    [DataRow(null, "PUBLISHED@1.2.0.7", "Published")]
    [DataRow("PENDING_REVIEW@1.2.0.7", "PUBLISHED@1.2.0.6", "InReview")]
    [DataRow("STAGED@1.2.0.7", null, "Received")]
    [DataRow("REJECTED@1.2.0.7", null, "Conflict")]
    [DataRow(null, "PUBLISHED@1.10.0.0", "Conflict")]
    [DataRow("PENDING_REVIEW@1.2.0.6", null, "Conflict")]
    [DataRow(null, "PUBLISHED@1.2.0.6", "Absent")]
    [DataRow("takenDown", null, "Conflict")]
    public async Task ChromeStatusDefinesTheExactVersionState(string? submitted,
        string? published, string state)
    {
        string body = ChromeStatus(submitted, published);
        using var handler = new Handler((_, _) => Response(200, body));
        using var client = new HttpClient(handler);

        StoreObservation result = await Chrome(client).ObserveAsync(context.CancellationToken);

        Assert.AreEqual(Enum.Parse<StoreState>(state), result.State);
        Request request = Assert.ContainsSingle(handler.Requests);
        Assert.AreEqual("GET", request.Method);
        Assert.AreEqual("https://chromewebstore.googleapis.com/v2/publishers/publisher/items/" +
            "item:fetchStatus", request.Uri);
        Assert.AreEqual("Bearer token", request.Authorization);
        Assert.IsFalse(JsonSerializer.Serialize(result, TransferJson.Default.StoreObservation)
            .Contains("token", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(404, "Conflict")]
    [DataRow(401, "Unknown")]
    [DataRow(503, "Unknown")]
    public async Task UnavailableChromeStatusEstablishesNoAbsence(int status, string state)
    {
        using var handler = new Handler((_, _) => Response(status, "{}"));
        using var client = new HttpClient(handler);

        StoreObservation result = await Chrome(client).ObserveAsync(context.CancellationToken);

        Assert.AreEqual(Enum.Parse<StoreState>(state), result.State);
    }

    [TestMethod]
    public async Task ChromeUploadPollsAsyncStateThenPublishesTheItem()
    {
        using var handler = new Handler((request, number) => number switch
        {
            0 => Response(200, """{"uploadState":"IN_PROGRESS"}"""),
            1 => Response(200, """{"lastAsyncUploadState":"IN_PROGRESS"}"""),
            2 => Response(200, """{"lastAsyncUploadState":"SUCCEEDED"}"""),
            _ => Response(200, """{"state":"PENDING_REVIEW"}"""),
        });
        using var client = new HttpClient(handler);
        using var files = new Originals(["chrome-zip"]);
        var operations = new List<StoreOperation>();

        StoreObservation? final = await Chrome(client).PublishAsync(files.Files, operations,
            context.CancellationToken);

        Assert.IsNull(final);
        Assert.IsTrue(operations.All(operation => operation.Succeeded));
        Assert.AreEqual("upload,publish", string.Join(",", operations.Select(operation =>
            operation.Operation)));
        Assert.AreEqual("https://chromewebstore.googleapis.com/upload/v2/publishers/" +
            "publisher/items/item:upload", handler.Requests[0].Uri);
        Assert.AreEqual("application/zip", handler.Requests[0].ContentType);
        Assert.AreEqual("https://chromewebstore.googleapis.com/v2/publishers/publisher/" +
            "items/item:publish", handler.Requests[3].Uri);
        Assert.HasCount(4, handler.Requests);
    }

    [TestMethod]
    public async Task FailedChromeUploadIsNeverPublished()
    {
        using var handler = new Handler((_, _) => Response(200, """{"uploadState":"FAILED"}"""));
        using var client = new HttpClient(handler);
        using var files = new Originals(["chrome-zip"]);
        var operations = new List<StoreOperation>();

        _ = await Chrome(client).PublishAsync(files.Files, operations,
            context.CancellationToken);

        Assert.IsFalse(Assert.ContainsSingle(operations).Succeeded);
        Assert.ContainsSingle(handler.Requests);
    }

    [TestMethod]
    [DataRow(200, AmoListed + "\"public\"}}", 0, "Published")]
    [DataRow(200, AmoListed + "\"unreviewed\"}}", 0, "InReview")]
    [DataRow(200, """{"version":"1.2.0.7","channel":"listed","is_disabled":true,""" +
        "\"file\":{\"status\":\"public\"}}", 0, "Conflict")]
    [DataRow(200, """{"version":"1.2.0.7","channel":"unlisted","file":{"status":"public"}}""",
        0, "Conflict")]
    [DataRow(404, "{}", 200, "Absent")]
    [DataRow(404, "{}", 404, "Conflict")]
    [DataRow(404, "{}", 503, "Unknown")]
    [DataRow(401, "{}", 0, "Unknown")]
    public async Task AmoVersionDetailDefinesTheExactVersionState(int status, string body,
        int addon, string state)
    {
        using var handler = new Handler((_, number) => number == 0 ? Response(status, body)
            : Response(addon, "{}"));
        using var client = new HttpClient(handler);

        StoreObservation result = await Amo(client).ObserveAsync(context.CancellationToken);

        Assert.AreEqual(Enum.Parse<StoreState>(state), result.State);
        Assert.AreEqual("https://addons.mozilla.org/api/v5/addons/addon/item/versions/v1.2.0.7/",
            handler.Requests[0].Uri);
    }

    [TestMethod]
    public async Task AmoUploadsVersionWithSourceAndObservesTheReceivedVersion()
    {
        using var handler = new Handler((request, number) => number switch
        {
            0 => Response(201, """{"uuid":"abc"}"""),
            1 => Response(200, """{"processed":false}"""),
            2 => Response(200, """{"processed":true,"valid":true,"version":"1.2.0.7"}"""),
            3 => Response(201, """{"id":1}"""),
            _ => Response(200, AmoListed + "\"unreviewed\"}}"),
        });
        using var client = new HttpClient(handler);
        using var files = new Originals(["firefox-zip", "firefox-sources"]);
        var operations = new List<StoreOperation>();

        StoreObservation? final = await Amo(client).PublishAsync(files.Files, operations,
            context.CancellationToken);

        Assert.AreEqual(StoreState.InReview, final!.State);
        Assert.IsTrue(operations.All(operation => operation.Succeeded));
        Assert.AreEqual("https://addons.mozilla.org/api/v5/addons/upload/",
            handler.Requests[0].Uri);
        Assert.Contains("name=channel", handler.Requests[0].Body);
        Assert.Contains("listed", handler.Requests[0].Body);
        Assert.AreEqual("https://addons.mozilla.org/api/v5/addons/addon/item/versions/",
            handler.Requests[3].Uri);
        Assert.Contains("name=source", handler.Requests[3].Body);
        Assert.Contains("name=upload", handler.Requests[3].Body);
        foreach (Request request in handler.Requests)
        {
            string[] jwt = request.Authorization!["JWT ".Length..].Split('.');
            Assert.HasCount(3, jwt);
            byte[] signature = HMACSHA256.HashData("secret"u8.ToArray(),
                Encoding.ASCII.GetBytes(jwt[0] + "." + jwt[1]));
            Assert.AreEqual(Base64Url(signature), jwt[2]);
            using JsonDocument claims = JsonDocument.Parse(Convert.FromBase64String(
                Pad(jwt[1].Replace('-', '+').Replace('_', '/'))));
            Assert.AreEqual("issuer", claims.RootElement.GetProperty("iss").GetString());
            Assert.IsTrue(claims.RootElement.GetProperty("exp").GetInt64() -
                claims.RootElement.GetProperty("iat").GetInt64() <= 300);
        }
        // Every request carries a fresh token.
        Assert.AreEqual(handler.Requests.Count, handler.Requests.Select(request =>
            request.Authorization).Distinct().Count());
    }

    [TestMethod]
    public async Task InvalidAmoUploadCreatesNoVersion()
    {
        using var handler = new Handler((_, number) => number switch
        {
            0 => Response(201, """{"uuid":"abc"}"""),
            1 => Response(200, """{"processed":true,"valid":false,"version":"1.2.0.7"}"""),
            _ => Response(404, "{}"),
        });
        using var client = new HttpClient(handler);
        using var files = new Originals(["firefox-zip", "firefox-sources"]);
        var operations = new List<StoreOperation>();

        StoreObservation? final = await Amo(client).PublishAsync(files.Files, operations,
            context.CancellationToken);

        Assert.IsFalse(Assert.ContainsSingle(operations).Succeeded);
        Assert.IsFalse(handler.Requests.Any(request => request.Uri.EndsWith("/versions/",
            StringComparison.Ordinal) && request.Method == "POST"));
        Assert.AreNotEqual(StoreState.Received, final!.State);
    }

    [TestMethod]
    [DataRow("Succeeded", "Received")]
    [DataRow("Failed", "Conflict")]
    [DataRow("InProgress", "Unknown")]
    public async Task EdgeSubmissionOperationIsTheOnlyStoreReport(string submitted,
        string state)
    {
        using var handler = new Handler((request, number) => number switch
        {
            0 => Response(202, "", "upload-op"),
            1 => Response(200, """{"status":"Succeeded"}"""),
            2 => Response(202, "", "submit-op"),
            _ => Response(200, $$"""{"status":"{{submitted}}"}"""),
        });
        using var client = new HttpClient(handler);
        using var files = new Originals(["edge-zip"]);
        var operations = new List<StoreOperation>();
        var store = new EdgeAddons(client, Subject("edge-addons"), Target("edge-addons"),
            Version, "key", "client", (_, _) => Task.CompletedTask, attempts: 2);

        Assert.AreEqual(StoreState.Unobserved, (await store.ObserveAsync(
            context.CancellationToken)).State);
        StoreObservation? final = await store.PublishAsync(files.Files, operations,
            context.CancellationToken);

        Assert.AreEqual(Enum.Parse<StoreState>(state), final!.State);
        Assert.AreEqual("https://api.addons.microsoftedge.microsoft.com/v1/products/item/" +
            "submissions/draft/package", handler.Requests[0].Uri);
        Assert.AreEqual("https://api.addons.microsoftedge.microsoft.com/v1/products/item/" +
            "submissions/draft/package/operations/upload-op", handler.Requests[1].Uri);
        Assert.AreEqual("https://api.addons.microsoftedge.microsoft.com/v1/products/item/" +
            "submissions", handler.Requests[2].Uri);
        Assert.AreEqual("https://api.addons.microsoftedge.microsoft.com/v1/products/item/" +
            "submissions/operations/submit-op", handler.Requests[3].Uri);
        Assert.IsTrue(handler.Requests.All(request => request.Authorization == "ApiKey key" &&
            request.ClientId == "client"));
        // An unfinished or failed submission returns to the owner; it is never resubmitted.
        Assert.AreEqual(1, handler.Requests.Count(request => request.Method == "POST" &&
            request.Uri.EndsWith("/submissions", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("chrome-web-store")]
    [DataRow("firefox-amo")]
    [DataRow("edge-addons")]
    public void DryRunObservesNoStoreAndReportsItUnobserved(string store)
    {
        using var files = new Originals(BrowserStorePreparation.StoreOutputs(store));
        BrowserStorePreparationResult preparation = files.Preparation(store);
        string root = Directory.CreateTempSubdirectory("browser-store-dry-").FullName;
        try
        {
            string prepared = Path.Combine(root, "preparation.json");
            File.WriteAllText(prepared, JsonSerializer.Serialize(preparation,
                TransferJson.Default.BrowserStorePreparationResult));
            string originals = Path.GetDirectoryName(Path.GetDirectoryName(
                files.Files[0].Path))!;
            int factory = 0;
            string Run(params string[] args)
            {
                using var output = new StringWriter();
                Assert.AreEqual(0, BrowserStoreReleaseCli.Run(["release", .. args], output,
                    store: (_, _, _) => { factory++; throw new InvalidOperationException(); },
                    environment: _ => null));
                return output.ToString();
            }
            string observation = Path.Combine(root, "observation.json");
            File.WriteAllText(observation, Run("observe-browser-store", prepared, originals));
            ReleaseDryRun result = JsonSerializer.Deserialize(Run("describe-browser-store",
                prepared, observation), TransferJson.Default.ReleaseDryRun)!;

            Assert.AreEqual(0, factory);
            Assert.AreEqual(StoreState.Unobserved, JsonSerializer.Deserialize(
                File.ReadAllText(observation), TransferJson.Default.StoreObservation)!.State);
            Assert.IsTrue(result.Ready);
            Assert.HasCount(2, result.HypotheticalWrites);
            Assert.AreEqual("The store was not observed: a dry run acquires no store " +
                "credential.", result.Reasons[0]);
            Assert.ThrowsExactly<InvalidDataException>(() => BrowserStoreRelease.Describe(
                preparation, Observation(preparation, StoreState.Absent)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task SatisfiedStoreIsNeverWrittenByThePublisher()
    {
        using var files = new Originals(["chrome-zip"]);
        BrowserStorePreparationResult preparation = files.Preparation("chrome-web-store",
            live: true);
        var store = new FakeStore(Observation(preparation, StoreState.InReview));

        BrowserStorePublicationResult result = await BrowserStoreRelease.PublishAsync(
            preparation, files.Files, Observation(preparation, StoreState.Absent), store,
            context.CancellationToken);

        Assert.AreEqual(0, store.Publications);
        Assert.IsEmpty(result.Errors);
        Assert.AreEqual(StoreState.InReview, result.Final!.State);
    }

    [TestMethod]
    public async Task PublisherSubmitsAnAbsentVersionAndCompletesFromTheStoreReport()
    {
        using var files = new Originals(["firefox-zip", "firefox-sources"]);
        BrowserStorePreparationResult preparation = files.Preparation("firefox-amo",
            live: true);
        StoreObservation initial = BrowserStoreRelease.Unobserved(preparation);
        var store = new FakeStore(Observation(preparation, StoreState.Absent),
            Observation(preparation, StoreState.InReview));

        BrowserStorePublicationResult publication = await BrowserStoreRelease.PublishAsync(
            preparation, files.Files, initial, store, context.CancellationToken);
        BrowserStoreCompletionResult completion = BrowserStoreRelease.Complete(preparation,
            initial, null, publication);

        Assert.AreEqual(1, store.Publications);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, completion.Outcome.State);
        Assert.IsTrue(completion.MutationAttempted);
        Assert.AreEqual(ReleaseBusinessState.Unknown, BrowserStoreRelease.Complete(
            preparation, initial, null, null).Outcome.State);
    }

    [TestMethod]
    public void MissingPublisherResultLeavesALaterPublishedStateUnknown()
    {
        using var files = new Originals(["chrome-zip"]);
        BrowserStorePreparationResult preparation = files.Preparation("chrome-web-store",
            live: true);

        BrowserStoreCompletionResult completion = BrowserStoreRelease.Complete(preparation,
            Observation(preparation, StoreState.Absent),
            Observation(preparation, StoreState.Published), null);

        Assert.AreEqual(ReleaseBusinessState.Unknown, completion.Outcome.State);
        Assert.AreEqual(StoreState.Published, completion.State);
        Assert.IsNull(completion.MutationAttempted);
        Assert.IsNull(completion.Publication);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, BrowserStoreRelease.Complete(
            preparation, Observation(preparation, StoreState.Published),
            Observation(preparation, StoreState.Published), null).Outcome.State);
    }

    [TestMethod]
    public async Task DryRunAndBuddyLiveCannotPublish()
    {
        using var files = new Originals(["chrome-zip"]);
        BrowserStorePreparationResult dry = files.Preparation("chrome-web-store");
        await Assert.ThrowsAsync<InvalidDataException>(() => BrowserStoreRelease.PublishAsync(
            dry, files.Files, Observation(dry, StoreState.Absent),
            new FakeStore(Observation(dry, StoreState.Absent)), context.CancellationToken));
        BrowserStorePreparationResult buddy = dry with { Prepared = dry.Prepared with
            { Subject = dry.Prepared.Subject with { Channel = "buddy", DryRun = false,
                SelectedRef = "refs/heads/main" } } };
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserStoreRelease.Validate(buddy));
    }

    [TestMethod]
    [DataRow("chrome-web-store", "Absent", "true")]
    [DataRow("chrome-web-store", "Published", "false")]
    [DataRow("firefox-amo", "Unobserved", "true")]
    [DataRow("edge-addons", "Unobserved", "true")]
    public void JobInputsBindTheStoreTargetAndRouteWritesThroughTheGate(string store,
        string state, string needed)
    {
        using var originals = new Originals(BrowserStorePreparation.StoreOutputs(store));
        string root = Directory.CreateTempSubdirectory("browser-store-caller-").FullName;
        try
        {
            var env = new Dictionary<string, string>
            {
                ["RUNNER_TEMP"] = root, ["DESTINATION"] = store, ["CHANNEL"] = "official",
                ["DRY_RUN"] = "false", ["GITHUB_SHA"] = new string('a', 40),
                ["GITHUB_REF"] = "refs/heads/main", ["GITHUB_RUN_ATTEMPT"] = "1",
                ["GITHUB_REPOSITORY"] = "hcoona/three", ["GITHUB_WORKSPACE"] = root,
                ["STORE_ITEM"] = "item", ["STORE_PUBLISHER"] = "publisher",
                ["GITHUB_OUTPUT"] = Path.Combine(root, "output"),
            };
            int Run(params string[] args) => ReleaseJobInputs.Run(
                ["release", "job-inputs", "browser-store", .. args],
                name => env.GetValueOrDefault(name))!.Value;
            Assert.AreEqual(0, Run("request"));
            using JsonDocument request = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
                "prepared/request.json")));
            Assert.AreEqual("item", request.RootElement.GetProperty("target")
                .GetProperty("item").GetString());
            BrowserStorePreparationResult preparation = originals.Preparation(store,
                live: true);
            File.WriteAllText(Path.Combine(root, "prepared/preparation.json"),
                JsonSerializer.Serialize(preparation,
                    TransferJson.Default.BrowserStorePreparationResult));
            Assert.AreEqual(0, Run("receive", "observation"));
            File.WriteAllText(Path.Combine(root, "observation/observation.json"),
                JsonSerializer.Serialize(Observation(preparation, Enum.Parse<StoreState>(
                    state)), TransferJson.Default.StoreObservation));
            Assert.AreEqual(0, Run("state"));
            Assert.AreEqual("write-needed=" + needed + "\n", File.ReadAllText(env[
                "GITHUB_OUTPUT"]));
            env["STORE_ITEM"] = "another";
            Assert.ThrowsExactly<InvalidDataException>(() => Run("receive", "verification"));
            env["STORE_ITEM"] = "item";
            env["CHANNEL"] = "buddy";
            Assert.ThrowsExactly<InvalidDataException>(() => Run("receive", "verification"));
            env["CHANNEL"] = "official";
            env["GITHUB_REF"] = "refs/heads/feature";
            Assert.ThrowsExactly<InvalidDataException>(() => Run("receive", "verification"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ReleaseSubject Subject(string store, bool live = false) =>
        new(new string('a', 40), "refs/heads/main", BrowserStorePreparation.Unit,
            BrowserStorePreparation.Declaration, "official", store, !live);

    private static BrowserStoreTarget Target(string store) => new(store, "item",
        store == "chrome-web-store" ? "publisher" : null);

    private static StoreObservation Observation(BrowserStorePreparationResult preparation,
        StoreState state) => new(preparation.Prepared.Subject, preparation.Target, Version,
        state, [], []);

    private static ChromeWebStore Chrome(HttpClient client) => new(client,
        Subject("chrome-web-store"), Target("chrome-web-store"), Version, "token",
        (_, _) => Task.CompletedTask);

    private static FirefoxAmo Amo(HttpClient client) => new(client, Subject("firefox-amo"),
        Target("firefox-amo"), Version, "issuer", "secret", (_, _) => Task.CompletedTask);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Pad(string value) => value + new string('=', (4 - value.Length % 4)
        % 4);

    private static HttpResponseMessage Response(int status, string body,
        string? location = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (location is not null) response.Headers.Location = new(location, UriKind.Relative);
        return response;
    }

    private sealed class FakeStore(params StoreObservation[] observations) : IBrowserStore
    {
        private int observed;
        internal int Publications { get; private set; }

        public Task<StoreObservation> ObserveAsync(CancellationToken token) =>
            Task.FromResult(observations[Math.Min(observed++, observations.Length - 1)]);

        public Task<StoreObservation?> PublishAsync(StoreFile[] files,
            List<StoreOperation> operations, CancellationToken token)
        {
            Publications++;
            operations.Add(new("submit", true, "Submitted."));
            return Task.FromResult<StoreObservation?>(observations[^1]);
        }
    }

    // Qualified originals and their preparation for one store request.
    private sealed class Originals : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory(
            "browser-store-originals-").FullName;
        private readonly string[] outputs;
        internal StoreFile[] Files { get; }

        internal Originals(string[] outputs)
        {
            this.outputs = outputs;
            Files = outputs.Select(output =>
            {
                string path = Path.Combine(root, "original", output + ".zip");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "original " + output);
                return new StoreFile(output, path);
            }).ToArray();
        }

        internal BrowserStorePreparationResult Preparation(string store, bool live = false)
        {
            ReleaseFile[] files = outputs.Select(output =>
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(root, "original", output +
                    ".zip"));
                return new ReleaseFile("browser-extensions", output, output + "-kind",
                    BrowserStorePreparation.Unit, Version, "original/" + output + ".zip",
                    bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            }).ToArray();
            string[] required = ["native-input", "build", "contents", "consumer",
                "original-bytes"];
            return new(new(Subject(store, live), files, required, required.Select(id =>
                    new ReleaseCheck(id, ReleaseCheckStatus.Passed)).ToArray()),
                Target(store), [new("build", new("pnpm", root, ["run", "zip"], 600),
                    new NativeCommandResult(NativeTermination.Exited, 0, "", "", 0.25, null))],
                []);
        }

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed record Request(string Uri, string Method, string? Authorization,
        string? ClientId, string? ContentType, string Body);

    private sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        internal List<Request> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int number = Requests.Count;
            string body = request.Content is null ? "" :
                await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(request.RequestUri!.AbsoluteUri, request.Method.Method,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-ClientID", out var values)
                    ? values.Single() : null,
                request.Content?.Headers.ContentType?.MediaType, body));
            return respond(request, number);
        }
    }
}
