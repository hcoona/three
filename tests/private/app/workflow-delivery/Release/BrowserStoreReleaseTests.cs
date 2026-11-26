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
        using var handler = new ScriptedHttpHandler((_, _) => Response(200, body));
        using var client = new HttpClient(handler);

        ReleaseObservationResult result =
            await Chrome(client).ObserveAsync(context.CancellationToken);

        Assert.AreEqual(Enum.Parse<StoreState>(state), result.StoreState!.Value);
        var request = Assert.ContainsSingle(handler.Requests);
        Assert.AreEqual(HttpMethod.Get, request.Method);
        Assert.AreEqual("https://chromewebstore.googleapis.com/v2/publishers/publisher/items/" +
            "item:fetchStatus", request.Uri.AbsoluteUri);
        Assert.AreEqual("Bearer token", request.Headers.Authorization?.ToString());
        Assert.IsFalse(JsonSerializer.Serialize(result,
            TransferJson.Default.ReleaseObservationResult)
            .Contains("token", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(404, "Conflict")]
    [DataRow(401, "Unknown")]
    public async Task UnavailableChromeStatusEstablishesNoAbsence(int status, string state)
    {
        using var handler = new ScriptedHttpHandler((_, _) => Response(status, "{}"));
        using var client = new HttpClient(handler);

        ReleaseObservationResult result =
            await Chrome(client).ObserveAsync(context.CancellationToken);

        Assert.AreEqual(Enum.Parse<StoreState>(state), result.StoreState!.Value);
    }

    [TestMethod]
    [DataRow("submitted", "true")]
    [DataRow("published", """{"state":"PUBLISHED"}""")]
    [DataRow("published", """{"state":"PUBLISHED","distributionChannels":{}}""")]
    [DataRow("published", """{"state":"PUBLISHED","distributionChannels":[]}""")]
    [DataRow("published", """{"state":"PUBLISHED","distributionChannels":[false]}""")]
    [DataRow("published", """{"state":"PUBLISHED","distributionChannels":[{}]}""")]
    [DataRow("published", """{"distributionChannels":[{"crxVersion":7}]}""")]
    [DataRow("published", """{"distributionChannels":[{"crxVersion":"invalid"}]}""")]
    [DataRow("published", """{"distributionChannels":[{"crxVersion":"1.0.0.0.0"}]}""")]
    [DataRow("submitted", """{"distributionChannels":[{"crxVersion":"1.0.0"}]}""")]
    [DataRow("submitted",
        """{"state":"IN_EXPEDITED_REVIEW","distributionChannels":[{"crxVersion":"1.0.0"}]}""")]
    public async Task UnreadableChromeRevisionBlocksPublication(string field, string revision)
    {
        string body = $$"""{"{{field}}ItemRevisionStatus":{{revision}}}""";
        using var handler = new ScriptedHttpHandler((_, _) => Response(200, body));
        using var client = new HttpClient(handler);
        using var files = new Originals(["chrome-zip"]);
        ReleasePreparationResult preparation = files.Preparation("chrome-web-store",
            live: true);
        var store = new ChromeWebStore(client, preparation.Prepared.Subject, preparation.Target!,
            Version, "token");

        ReleasePublicationResult result = await PublishAsync(
            preparation, files, Observation(preparation, StoreState.Absent), store,
            context.CancellationToken);

        Assert.AreEqual(StoreState.Unknown, result.Before.StoreState!.Value);
        Assert.IsNotEmpty(result.Before.Observation.Reasons ?? []);
        Assert.IsEmpty(result.Steps);
        Assert.AreEqual(HttpMethod.Get, Assert.ContainsSingle(handler.Requests).Method);
    }

    [TestMethod]
    public async Task ChromeUploadPollsAsyncStateThenPublishesTheItem()
    {
        using var handler = new ScriptedHttpHandler((_, number) => number switch
        {
            0 => Response(200, """{"uploadState":"IN_PROGRESS"}"""),
            1 => Response(200, """{"lastAsyncUploadState":"IN_PROGRESS"}"""),
            2 => Response(200, """{"lastAsyncUploadState":"SUCCEEDED"}"""),
            _ => Response(200, """{"state":"PENDING_REVIEW"}"""),
        });
        using var client = new HttpClient(handler);
        using var files = new Originals(["chrome-zip"]);
        var operations = new List<ReleaseWriteStep>();

        ReleaseObservationResult? final = await Chrome(client).PublishAsync(files.Files, operations,
            context.CancellationToken);

        Assert.IsNull(final);
        Assert.IsTrue(operations.All(operation => operation.Succeeded));
        Assert.AreEqual("upload,publish", string.Join(",", operations.Select(operation =>
            operation.Target)));
        Assert.AreEqual("https://chromewebstore.googleapis.com/upload/v2/publishers/" +
            "publisher/items/item:upload", handler.Requests[0].Uri.AbsoluteUri);
        Assert.AreEqual("application/zip", handler.Requests[0].ContentType);
        Assert.AreEqual("https://chromewebstore.googleapis.com/v2/publishers/publisher/" +
            "items/item:publish", handler.Requests[3].Uri.AbsoluteUri);
        Assert.HasCount(4, handler.Requests);
    }

    [TestMethod]
    public async Task FailedChromeUploadIsNeverPublished()
    {
        using var handler = new ScriptedHttpHandler((_, _) =>
            Response(200, """{"uploadState":"FAILED"}"""));
        using var client = new HttpClient(handler);
        using var files = new Originals(["chrome-zip"]);
        var operations = new List<ReleaseWriteStep>();

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
        using var handler = new ScriptedHttpHandler((_, number) =>
            number == 0 ? Response(status, body) : Response(addon, "{}"));
        using var client = new HttpClient(handler);

        ReleaseObservationResult result = await Amo(client).ObserveAsync(context.CancellationToken);

        Assert.AreEqual(Enum.Parse<StoreState>(state), result.StoreState!.Value);
        Assert.AreEqual("https://addons.mozilla.org/api/v5/addons/addon/item/versions/v1.2.0.7/",
            handler.Requests[0].Uri.AbsoluteUri);
    }

    [TestMethod]
    public async Task AmoUploadsVersionWithSourceAndObservesTheReceivedVersion()
    {
        using var handler = new ScriptedHttpHandler((_, number) => number switch
        {
            0 => Response(201, """{"uuid":"abc"}"""),
            1 => Response(200, """{"processed":false}"""),
            2 => Response(200, """{"processed":true,"valid":true,"version":"1.2.0.7"}"""),
            3 => Response(201, """{"id":1}"""),
            _ => Response(200, AmoListed + "\"unreviewed\"}}"),
        }, captureBody: true);
        using var client = new HttpClient(handler);
        using var files = new Originals(["firefox-zip", "firefox-sources"]);
        var operations = new List<ReleaseWriteStep>();

        ReleaseObservationResult? final = await Amo(client).PublishAsync(files.Files, operations,
            context.CancellationToken);

        Assert.AreEqual(StoreState.InReview, final!.StoreState!.Value);
        Assert.IsTrue(operations.All(operation => operation.Succeeded));
        Assert.AreEqual("https://addons.mozilla.org/api/v5/addons/upload/",
            handler.Requests[0].Uri.AbsoluteUri);
        Assert.Contains("name=channel", handler.Requests[0].Body);
        Assert.Contains("listed", handler.Requests[0].Body);
        Assert.AreEqual("https://addons.mozilla.org/api/v5/addons/addon/item/versions/",
            handler.Requests[3].Uri.AbsoluteUri);
        Assert.Contains("name=source", handler.Requests[3].Body);
        Assert.Contains("name=upload", handler.Requests[3].Body);
        foreach (var request in handler.Requests)
        {
            Assert.AreEqual("JWT", request.Headers.Authorization?.Scheme);
            string[] jwt = request.Headers.Authorization!.Parameter!.Split('.');
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
            request.Headers.Authorization?.Parameter).Distinct().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AmoReadsBackAfterAmbiguousCreateWithoutResubmitting(bool timeout)
    {
        using var handler = new ScriptedHttpHandler((_, number) => number switch
        {
            0 => Response(404, "{}"),
            1 => Response(200, "{}"),
            2 => Response(201, """{"uuid":"abc"}"""),
            3 => Response(200, """{"processed":true,"valid":true,"version":"1.2.0.7"}"""),
            4 => throw (timeout ? new TaskCanceledException("Request timed out.")
                : new HttpRequestException("Connection lost after create.")),
            _ => Response(200, AmoListed + "\"unreviewed\"}}"),
        });
        using var client = new HttpClient(handler);
        using var files = new Originals(["firefox-zip", "firefox-sources"]);
        ReleasePreparationResult preparation = files.Preparation("firefox-amo", live: true);
        ReleaseObservationResult initial = BrowserStoreDestinationAdapter.Unobserved(preparation);
        var store = new FirefoxAmo(client, preparation.Prepared.Subject, preparation.Target!,
            Version, "issuer", "secret");

        ReleasePublicationResult publication = await PublishAsync(
            preparation, files, initial, store, context.CancellationToken);
        ReleaseCompletionResult completion = Complete(preparation, initial, null, publication);

        var create = Assert.ContainsSingle(handler.Requests.Where(request =>
            request.Method == HttpMethod.Post && request.Uri.AbsolutePath ==
            "/api/v5/addons/addon/item/versions/"));
        Assert.AreEqual(create, handler.Requests[4]);
        Assert.HasCount(6, handler.Requests);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[^1].Method);
        Assert.AreEqual("/api/v5/addons/addon/item/versions/v" + Version + "/",
            handler.Requests[^1].Uri.AbsolutePath);
        Assert.IsFalse(Assert.ContainsSingle(publication.Steps.Where(operation =>
            operation.Target == "create-version")).Succeeded);
        Assert.StartsWith("create-version:", Assert.ContainsSingle(publication.Errors));
        Assert.AreEqual(StoreState.InReview, publication.After!.StoreState!.Value);
        Assert.AreEqual(StoreState.InReview, completion.StoreState!.Value);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, completion.Outcome.State);
        Assert.IsTrue(completion.MutationAttempted);
    }

    [TestMethod]
    public async Task InvalidAmoUploadCreatesNoVersion()
    {
        using var handler = new ScriptedHttpHandler((_, number) => number switch
        {
            0 => Response(201, """{"uuid":"abc"}"""),
            1 => Response(200, """{"processed":true,"valid":false,"version":"1.2.0.7"}"""),
            _ => Response(404, "{}"),
        });
        using var client = new HttpClient(handler);
        using var files = new Originals(["firefox-zip", "firefox-sources"]);
        var operations = new List<ReleaseWriteStep>();

        ReleaseObservationResult? final = await Amo(client).PublishAsync(files.Files, operations,
            context.CancellationToken);

        Assert.IsFalse(Assert.ContainsSingle(operations).Succeeded);
        Assert.IsFalse(handler.Requests.Any(request => request.Uri.AbsolutePath.EndsWith(
            "/versions/", StringComparison.Ordinal) && request.Method == HttpMethod.Post));
        Assert.AreNotEqual(StoreState.Received, final!.StoreState!.Value);
    }

    [TestMethod]
    [DataRow("Succeeded", "Received")]
    [DataRow("Failed", "Conflict")]
    [DataRow("InProgress", "Unknown")]
    public async Task EdgeSubmissionOperationIsTheOnlyStoreReport(string submitted,
        string state)
    {
        using var handler = new ScriptedHttpHandler((_, number) => number switch
        {
            0 => Response(202, "", "upload-op"),
            1 => Response(200, """{"status":"Succeeded"}"""),
            2 => Response(202, "", "submit-op"),
            _ => Response(200, $$"""{"status":"{{submitted}}"}"""),
        });
        using var client = new HttpClient(handler);
        using var files = new Originals(["edge-zip"]);
        ReleasePreparationResult preparation = files.Preparation("edge-addons", live: true);
        var store = new EdgeAddons(client, preparation.Prepared.Subject, preparation.Target!,
            Version, "key", "client", (_, _) => Task.CompletedTask, attempts: 2);

        ReleaseObservationResult initial = await store.ObserveAsync(context.CancellationToken);
        Assert.AreEqual(StoreState.Unobserved, initial.StoreState!.Value);
        Assert.IsEmpty(handler.Requests);
        ReleasePublicationResult publication = await PublishAsync(
            preparation, files, initial, store, context.CancellationToken);
        ReleaseCompletionResult completion = Complete(preparation, initial, null, publication);

        Assert.AreEqual(Enum.Parse<StoreState>(state), completion.StoreState!.Value);
        Assert.AreEqual(state switch
        {
            "Received" => ReleaseBusinessState.Satisfied,
            "Conflict" => ReleaseBusinessState.Unsatisfied,
            _ => ReleaseBusinessState.Unknown
        }, completion.Outcome.State);
        Assert.AreEqual("https://api.addons.microsoftedge.microsoft.com/v1/products/item/" +
            "submissions/draft/package", handler.Requests[0].Uri.AbsoluteUri);
        Assert.AreEqual("https://api.addons.microsoftedge.microsoft.com/v1/products/item/" +
            "submissions/draft/package/operations/upload-op", handler.Requests[1].Uri.AbsoluteUri);
        Assert.AreEqual("https://api.addons.microsoftedge.microsoft.com/v1/products/item/" +
            "submissions", handler.Requests[2].Uri.AbsoluteUri);
        Assert.AreEqual("https://api.addons.microsoftedge.microsoft.com/v1/products/item/" +
            "submissions/operations/submit-op", handler.Requests[3].Uri.AbsoluteUri);
        Assert.IsTrue(handler.Requests.All(request =>
            request.Headers.Authorization?.ToString() == "ApiKey key" &&
            request.Headers.GetValues("X-ClientID").Single() == "client"));
        // An unfinished or failed submission returns to the owner; it is never resubmitted.
        Assert.ContainsSingle(handler.Requests.Where(request => request.Method == HttpMethod.Post &&
            request.Uri.AbsolutePath.EndsWith("/submissions", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void DryRunDoesNotResolveStoreCredentialsAndDescribesFirefoxOutputs()
    {
        const string Store = "firefox-amo";
        using var files = new Originals(BrowserStorePreparation.StoreOutputs(Store));
        ReleasePreparationResult preparation = files.Preparation(Store);
        string root = Directory.CreateTempSubdirectory("browser-store-dry-").FullName;
        try
        {
            string prepared = Path.Combine(root, "preparation.json");
            File.WriteAllText(prepared, JsonSerializer.Serialize(preparation,
                TransferJson.Default.ReleasePreparationResult));
            int factory = 0;
            string Run(params string[] args)
            {
                using var output = new StringWriter();
                Assert.AreEqual(0, ReleaseCli.Run(["release", .. args], output,
                    new(BrowserStoreFactory: (_, _, _) =>
                    {
                        factory++;
                        throw new InvalidOperationException();
                    }, Environment: name => throw new AssertFailedException(
                        "A dry run must not resolve store credentials: " + name))));
                return output.ToString();
            }
            string observation = Path.Combine(root, "observation.json");
            File.WriteAllText(observation, Run("observe-browser-store", prepared, files.Root));
            ReleaseDryRun result = JsonSerializer.Deserialize(Run("describe-browser-store",
                prepared, observation), TransferJson.Default.ReleaseDryRun)!;

            Assert.AreEqual(0, factory);
            Assert.AreEqual(StoreState.Unobserved, ReleaseCli.Read(observation,
                TransferJson.Default.ReleaseObservationResult).StoreState!.Value);
            Assert.IsTrue(result.Ready);
            Assert.HasCount(2, result.HypotheticalWrites);
            Assert.AreEqual("The store was not observed: a dry run acquires no store " +
                "credential.", result.Reasons[0]);
            Assert.ThrowsExactly<InvalidDataException>(() => ReleaseEngine.Describe(
                ReleaseAdapters.BrowserStore, preparation,
                Observation(preparation, StoreState.Absent)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task SatisfiedStoreIsNeverWrittenByThePublisher()
    {
        using var files = new Originals(["chrome-zip"]);
        ReleasePreparationResult preparation = files.Preparation("chrome-web-store",
            live: true);
        var store = new FakeStore(Observation(preparation, StoreState.InReview));

        ReleasePublicationResult result = await PublishAsync(
            preparation, files, Observation(preparation, StoreState.Absent), store,
            context.CancellationToken);

        Assert.AreEqual(0, store.Publications);
        Assert.IsEmpty(result.Errors);
        Assert.AreEqual(StoreState.InReview, result.After!.StoreState!.Value);
        ReleaseCompletionResult missingFinal = Complete(preparation,
            Observation(preparation, StoreState.Absent), final: null, result);
        Assert.AreEqual(ReleaseBusinessState.Unknown, missingFinal.Outcome.State);
        Assert.AreEqual(false, missingFinal.MutationAttempted);
        Assert.DoesNotContain("effects remain unknown",
            string.Join(";", missingFinal.Outcome.Reasons));
    }

    [TestMethod]
    public async Task PublisherSubmitsAnAbsentVersionAndCompletesFromTheStoreReport()
    {
        using var files = new Originals(["firefox-zip", "firefox-sources"]);
        ReleasePreparationResult preparation = files.Preparation("firefox-amo",
            live: true);
        ReleaseObservationResult initial = BrowserStoreDestinationAdapter.Unobserved(preparation);
        var store = new FakeStore(Observation(preparation, StoreState.Absent),
            Observation(preparation, StoreState.InReview));

        ReleasePublicationResult publication = await PublishAsync(
            preparation, files, initial, store, context.CancellationToken);
        ReleaseCompletionResult completion = Complete(preparation, initial, null, publication);

        Assert.AreEqual(1, store.Publications);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, completion.Outcome.State);
        Assert.IsTrue(completion.MutationAttempted);
        Assert.AreEqual(ReleaseBusinessState.Unknown,
            Complete(preparation, initial, null, null).Outcome.State);
    }

    [TestMethod]
    public void MissingPublisherResultLeavesALaterPublishedStateUnknown()
    {
        using var files = new Originals(["chrome-zip"]);
        ReleasePreparationResult preparation = files.Preparation("chrome-web-store",
            live: true);

        ReleaseObservationResult published = Observation(preparation, StoreState.Published);
        ReleaseCompletionResult completion = Complete(preparation,
            Observation(preparation, StoreState.Absent), published, null);

        Assert.AreEqual(ReleaseBusinessState.Unknown, completion.Outcome.State);
        Assert.AreEqual(StoreState.Published, completion.StoreState!.Value);
        Assert.IsNull(completion.MutationAttempted);
        Assert.IsNull(completion.Publication);
        Assert.AreEqual(ReleaseBusinessState.Satisfied,
            Complete(preparation, published, published, null).Outcome.State);
        Assert.AreEqual(ReleaseBusinessState.Unknown,
            Complete(preparation, published, null, null).Outcome.State);
    }

    [TestMethod]
    [DataRow("firefox-amo")]
    [DataRow("edge-addons")]
    public void CompletePublisherOnlyStoreRejectsExternalFinalObservation(string store)
    {
        using var files = new Originals(BrowserStorePreparation.StoreOutputs(store));
        ReleasePreparationResult preparation = files.Preparation(store, live: true);
        ReleaseObservationResult initial = BrowserStoreDestinationAdapter.Unobserved(preparation);
        ReleaseObservationResult final = Observation(preparation, StoreState.Received);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            Complete(preparation, initial, final, publication: null));
    }

    [TestMethod]
    public async Task DryRunAndBuddyLiveCannotPublish()
    {
        using var files = new Originals(["chrome-zip"]);
        ReleasePreparationResult dry = files.Preparation("chrome-web-store");
        await Assert.ThrowsAsync<InvalidDataException>(() => PublishAsync(
            dry, files, Observation(dry, StoreState.Absent),
            new FakeStore(Observation(dry, StoreState.Absent)), context.CancellationToken));
        ReleasePreparationResult buddy = dry with
        {
            Prepared = dry.Prepared with
            {
                Subject = dry.Prepared.Subject with
                {
                    Channel = "buddy",
                    DryRun = false,
                    SelectedRef = "refs/heads/main"
                }
            }
        };
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseAdapters.BrowserStore.ValidatePreparation(buddy));
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
                ["RUNNER_TEMP"] = root,
                ["DESTINATION"] = store,
                ["CHANNEL"] = "official",
                ["DRY_RUN"] = "false",
                ["GITHUB_SHA"] = new string('a', 40),
                ["GITHUB_REF"] = "refs/heads/main",
                ["GITHUB_RUN_ATTEMPT"] = "1",
                ["GITHUB_REPOSITORY"] = "hcoona/three",
                ["GITHUB_WORKSPACE"] = root,
                ["STORE_ITEM"] = "item",
                ["STORE_PUBLISHER"] = "publisher",
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
            ReleasePreparationResult preparation = originals.Preparation(store,
                live: true);
            File.WriteAllText(Path.Combine(root, "prepared/preparation.json"),
                JsonSerializer.Serialize(preparation,
                    TransferJson.Default.ReleasePreparationResult));
            Assert.AreEqual(0, Run("receive", "observation"));
            File.WriteAllText(Path.Combine(root, "observation/observation.json"),
                JsonSerializer.Serialize(Observation(preparation, Enum.Parse<StoreState>(
                    state)), TransferJson.Default.ReleaseObservationResult));
            Assert.AreEqual(0, Run("state"));
            Assert.AreEqual("write-needed=" + needed + "\n", File.ReadAllText(env[
                "GITHUB_OUTPUT"]));
            if (store == "chrome-web-store" && state == "Absent")
            {
                env["STORE_ITEM"] = "another";
                Assert.ThrowsExactly<InvalidDataException>(() => Run("receive", "verification"));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ReleaseSubject Subject(string store, bool live = false) =>
        new(new string('a', 40), "refs/heads/main", BrowserStorePreparation.Unit,
            BrowserStorePreparation.Declaration, "official", store, !live);

    private static BrowserStoreTarget Target(string store) => new(store, "item",
        store == "chrome-web-store" ? "publisher" : null);

    private static ReleaseObservationResult Observation(ReleasePreparationResult preparation,
        StoreState state) => StoreHttp.Observation(preparation.Prepared.Subject,
            preparation.Target!, Version, state, [], []);

    private static Task<ReleasePublicationResult> PublishAsync(ReleasePreparationResult preparation,
        Originals originals, ReleaseObservationResult initial, IBrowserStore store,
        CancellationToken token) =>
        ReleaseAdapters.BrowserStore.PublishAsync(preparation, originals.Root, originals.Files,
            initial, store, token);

    private static ReleaseCompletionResult Complete(ReleasePreparationResult preparation,
        ReleaseObservationResult initial, ReleaseObservationResult? final,
        ReleasePublicationResult? publication) =>
        ReleaseEngine.Complete(ReleaseAdapters.BrowserStore, preparation, initial, final,
            publication);

    private static ChromeWebStore Chrome(HttpClient client) => new(client,
        Subject("chrome-web-store"), Target("chrome-web-store"), Version, "token",
        (_, _) => Task.CompletedTask);

    private static FirefoxAmo Amo(HttpClient client) => new(client, Subject("firefox-amo"),
        Target("firefox-amo"), Version, "issuer", "secret", (_, _) => Task.CompletedTask);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Pad(string value) => value + new string('=', (4 - value.Length % 4)
        % 4);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StoreCliSuppliesNoRedirectClientSoCustomHeadersNeverFollowRedirects(
        bool publish)
    {
        await using var target = new RegistryHttpClientTests.LoopbackServer(tls: null,
            _ => RegistryHttpClientTests.Ok("{}"));
        await using var origin = new RegistryHttpClientTests.LoopbackServer(tls: null,
            _ => RegistryHttpClientTests.Redirect($"http://127.0.0.1:{target.Port}/other"));
        string storeName = publish ? BrowserStorePreparation.EdgeAddons
            : BrowserStorePreparation.ChromeWebStore;
        using var originals = new Originals(BrowserStorePreparation.StoreOutputs(storeName));
        ReleasePreparationResult preparation = originals.Preparation(storeName, live: true);
        string dir = Directory.CreateTempSubdirectory("browser-store-cli-").FullName;
        try
        {
            string prepPath = Path.Combine(dir, "preparation.json");
            string initPath = Path.Combine(dir, "initial.json");
            File.WriteAllText(prepPath, JsonSerializer.Serialize(preparation,
                TransferJson.Default.ReleasePreparationResult));
            File.WriteAllText(initPath, JsonSerializer.Serialize(
                BrowserStoreDestinationAdapter.Unobserved(preparation),
                TransferJson.Default.ReleaseObservationResult));
            HttpStatusCode? observedStatus = null;
            using var output = new StringWriter();

            _ = ReleaseCli.Run(publish
                    ? ["release", "publish-browser-store", prepPath, originals.Root, initPath]
                    : ["release", "observe-browser-store", prepPath, originals.Root],
                output, new(BrowserStoreFactory: (prep, _, client) => new RedirectProbeStore(
                    prep, client, new Uri($"http://127.0.0.1:{origin.Port}/status"),
                    status => observedStatus = status)));

            Assert.AreEqual(HttpStatusCode.Found, observedStatus);
            Assert.HasCount(1, origin.Requests);
            Assert.IsEmpty(target.Requests);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class RedirectProbeStore(ReleasePreparationResult preparation,
        HttpClient client, Uri uri, Action<HttpStatusCode> record) : IBrowserStore
    {
        public async Task<ReleaseObservationResult> ObserveAsync(CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("X-ClientID", "secret-client-id");
            using HttpResponseMessage response = await client.SendAsync(request, token);
            record(response.StatusCode);
            return StoreHttp.Observation(preparation.Prepared.Subject, preparation.Target!, Version,
                StoreState.Unknown, ["Redirect blocked."], []);
        }

        public async Task<ReleaseObservationResult?> PublishAsync(StoreFile[] files,
            List<ReleaseWriteStep> operations, CancellationToken token) =>
            await ObserveAsync(token);
    }

    private static HttpResponseMessage Response(int status, string body,
        string? location = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (location is not null)
            response.Headers.Location = new(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class FakeStore(params ReleaseObservationResult[] observations) : IBrowserStore
    {
        private int observed;
        internal int Publications { get; private set; }

        public Task<ReleaseObservationResult> ObserveAsync(CancellationToken token) =>
            Task.FromResult(observations[Math.Min(observed++, observations.Length - 1)]);

        public Task<ReleaseObservationResult?> PublishAsync(StoreFile[] files,
            List<ReleaseWriteStep> operations, CancellationToken token)
        {
            Publications++;
            operations.Add(ReleaseWriteStep.FromStore("submit", true, "Submitted."));
            return Task.FromResult<ReleaseObservationResult?>(observations[^1]);
        }
    }

    // Qualified originals and their preparation for one store request.
    private sealed class Originals : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory(
            "browser-store-originals-").FullName;
        private readonly string[] outputs;
        internal string Root => root;
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

        internal ReleasePreparationResult Preparation(string store, bool live = false)
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
                new(StringComparer.Ordinal), [new("build", new("pnpm", root, ["run", "zip"], 600),
                    new NativeCommandResult(NativeTermination.Exited, 0, "", "", 0.25, null))],
                [], Target: Target(store));
        }

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

}
