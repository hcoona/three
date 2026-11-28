using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Core;
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
            0 => Response(200, "{}"),
            1 => Response(200, """{"uploadState":"IN_PROGRESS"}"""),
            2 => Response(200, """{"lastAsyncUploadState":"IN_PROGRESS"}"""),
            3 => Response(200, """{"lastAsyncUploadState":"SUCCEEDED"}"""),
            _ => Response(200, """{"state":"PENDING_REVIEW"}"""),
        });
        using var client = new HttpClient(handler);
        using var files = new Originals(["chrome-zip"]);
        ReleasePreparationResult preparation = files.Preparation("chrome-web-store", live: true);
        ReleasePublicationResult publication = await PublishAsync(preparation, files,
            Observation(preparation, StoreState.Absent), Chrome(client), context.CancellationToken);

        Assert.IsNull(publication.After);
        Assert.IsTrue(publication.Steps.All(operation => operation.Succeeded));
        Assert.AreEqual("upload,publish", string.Join(",", publication.Steps.Select(operation =>
            operation.Target)));
        Assert.AreEqual("https://chromewebstore.googleapis.com/upload/v2/publishers/" +
            "publisher/items/item:upload", handler.Requests[1].Uri.AbsoluteUri);
        Assert.AreEqual("application/zip", handler.Requests[1].ContentType);
        Assert.AreEqual("https://chromewebstore.googleapis.com/v2/publishers/publisher/" +
            "items/item:publish", handler.Requests[4].Uri.AbsoluteUri);
        Assert.HasCount(5, handler.Requests);
    }

    [TestMethod]
    [DataRow("FAILED", "FAILED")]
    [DataRow("https://example.invalid/opaque-id", "unsupported")]
    public async Task FailedChromeUploadIsNeverPublished(string state, string diagnostic)
    {
        using var handler = new ScriptedHttpHandler((_, _) =>
            Response(200, $$"""{"uploadState":"{{state}}"}"""));
        using var client = new HttpClient(handler);
        using var files = new Originals(["chrome-zip"]);
        ReleasePreparationResult preparation = files.Preparation("chrome-web-store", live: true);
        ReleasePublicationResult publication = await PublishAsync(preparation, files,
            Observation(preparation, StoreState.Absent), Chrome(client), context.CancellationToken);

        ReleaseWriteStep step = Assert.ContainsSingle(publication.Steps);
        Assert.IsFalse(step.Succeeded);
        Assert.AreEqual($"Upload state: {diagnostic}.", step.Detail);
        Assert.ContainsSingle(handler.Requests.Where(r => r.Method == HttpMethod.Post));
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
    [DataRow("disabled", "Conflict", "Unsatisfied")]
    [DataRow("other", "Unknown", "Unknown")]
    public async Task AmoBlockedRefreshIsTheFinalVersionRead(string status, string state,
        string verdict)
    {
        using var handler = new ScriptedHttpHandler((_, _) =>
            Response(200, AmoListed + "\"" + status + "\"}}"));
        using var client = new HttpClient(handler);
        using var files = new Originals(["firefox-zip", "firefox-sources"]);
        var preparation = files.Preparation("firefox-amo", live: true);
        var initial = BrowserStoreDestinationAdapter.Unobserved(preparation);

        var receipt = await PublishAsync(preparation, files, initial, Amo(client),
            context.CancellationToken);
        var completion = Complete(preparation, initial, null, receipt);

        Assert.AreEqual(HttpMethod.Get, Assert.ContainsSingle(handler.Requests).Method);
        Assert.IsEmpty(receipt.Steps);
        Assert.IsFalse(completion.MutationAttempted);
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(verdict), completion.Outcome.State);
        Assert.AreEqual(Enum.Parse<StoreState>(state), completion.StoreState);
        Assert.IsFalse(ReleaseEngine.AnchorAllowed(ReleaseAdapters.BrowserStore,
            preparation, initial, receipt));
        var substituted = receipt with { After = Observation(preparation, StoreState.InReview) };
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseEngine.AnchorAllowed(
            ReleaseAdapters.BrowserStore, preparation, initial, substituted));
        Assert.ThrowsExactly<InvalidDataException>(() => Complete(preparation, initial, null,
            receipt with { After = null }));
    }

    [TestMethod]
    public async Task AmoUploadsVersionWithSourceAndObservesTheReceivedVersion()
    {
        using var handler = new ScriptedHttpHandler((_, number) => number switch
        {
            0 => Response(404, "{}"),
            1 => Response(200, "{}"),
            2 => Response(201, """{"uuid":"abc"}"""),
            3 => Response(200, """{"processed":false}"""),
            4 => Response(200, """{"processed":true,"valid":true,"version":"1.2.0.7"}"""),
            5 => Response(201, """{"id":1}"""),
            _ => Response(200, AmoListed + "\"unreviewed\"}}"),
        }, captureBody: true);
        using var client = new HttpClient(handler);
        using var files = new Originals(["firefox-zip", "firefox-sources"]);
        ReleasePreparationResult preparation = files.Preparation("firefox-amo", live: true);
        ReleasePublicationResult publication = await PublishAsync(preparation, files,
            BrowserStoreDestinationAdapter.Unobserved(preparation),
            Amo(client), context.CancellationToken);

        Assert.AreEqual(StoreState.InReview, publication.After!.StoreState!.Value);
        Assert.IsNull(publication.After.Reads.First(read => read.Stage == "upload-status").Url);
        Assert.IsTrue(publication.Steps.All(operation => operation.Succeeded));
        Assert.AreEqual("https://addons.mozilla.org/api/v5/addons/upload/",
            handler.Requests[2].Uri.AbsoluteUri);
        Assert.Contains("name=channel", handler.Requests[2].Body);
        Assert.Contains("listed", handler.Requests[2].Body);
        Assert.AreEqual("https://addons.mozilla.org/api/v5/addons/addon/item/versions/",
            handler.Requests[5].Uri.AbsoluteUri);
        Assert.Contains("name=source", handler.Requests[5].Body);
        Assert.Contains("name=upload", handler.Requests[5].Body);
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
    [DataRow("timeout", true)]
    [DataRow("lost", true)]
    [DataRow("rejected", false)]
    public async Task AmoReadsBackOnceAndOnlyAnAmbiguousCreateCanUseThatProofForTheAnchor(
        string response, bool permitsAnchor)
    {
        using var handler = new ScriptedHttpHandler((_, number) => number switch
        {
            0 => Response(404, "{}"),
            1 => Response(200, "{}"),
            2 => Response(201, """{"uuid":"abc"}"""),
            3 => Response(200, """{"processed":true,"valid":true,"version":"1.2.0.7"}"""),
            4 => response switch
            {
                "timeout" => throw new TaskCanceledException("Request timed out."),
                "lost" => throw new HttpRequestException("Connection lost after create."),
                _ => Response(400, "{}"),
            },
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
        Assert.AreEqual(permitsAnchor, publication.Steps[^1].Ambiguous);
        Assert.AreEqual(permitsAnchor, ReleaseEngine.AnchorAllowed(ReleaseAdapters.BrowserStore,
            preparation, initial, publication));
    }

    [TestMethod]
    public async Task InvalidAmoUploadCreatesNoVersion()
    {
        using var handler = new ScriptedHttpHandler((_, number) => number switch
        {
            0 => Response(404, "{}"),
            1 => Response(200, "{}"),
            2 => Response(201, """{"uuid":"abc"}"""),
            3 => Response(200, """{"processed":true,"valid":false,"version":"1.2.0.7"}"""),
            _ => Response(404, "{}"),
        });
        using var client = new HttpClient(handler);
        using var files = new Originals(["firefox-zip", "firefox-sources"]);
        ReleasePreparationResult preparation = files.Preparation("firefox-amo", live: true);
        ReleasePublicationResult publication = await PublishAsync(preparation, files,
            BrowserStoreDestinationAdapter.Unobserved(preparation),
            Amo(client), context.CancellationToken);

        Assert.IsFalse(Assert.ContainsSingle(publication.Steps).Succeeded);
        Assert.IsFalse(handler.Requests.Any(request => request.Uri.AbsolutePath.EndsWith(
            "/versions/", StringComparison.Ordinal) && request.Method == HttpMethod.Post));
        Assert.AreNotEqual(StoreState.Received, publication.After!.StoreState!.Value);
    }

    [TestMethod]
    [DataRow("Succeeded", "Received", "First")]
    [DataRow("Succeeded", "Received", "OwnerChecked")]
    [DataRow("Failed", "Conflict", "First")]
    [DataRow("InProgress", "Unknown", "First")]
    public async Task EdgeSubmissionOperationIsTheOnlyStoreReport(string submitted,
        string state, string clearance)
    {
        using var handler = new ScriptedHttpHandler((_, number) => number switch
        {
            0 => Response(202, "", "upload-op"),
            1 => Response(200, """{"status":"Succeeded"}"""),
            2 => Response(202, "", "submit-op"),
            _ => Response(200,
                $$"""
                {"status":"{{submitted}}","errorCode":"https://example.invalid/correlation-id"}
                """),
        });
        using var client = new HttpClient(handler);
        using var files = new Originals(["edge-zip"]);
        ReleasePreparationResult preparation = files.Preparation("edge-addons", live: true,
            clearance: Enum.Parse<SubmissionClearance>(clearance));
        var store = new EdgeAddons(client, preparation.Prepared.Subject, preparation.Target!,
            Version, "key", "client", (_, _) => Task.CompletedTask, attempts: 2);

        ReleaseObservationResult initial = BrowserStoreDestinationAdapter.Unobserved(preparation);
        Assert.AreEqual(StoreState.Unobserved, initial.StoreState!.Value);
        Assert.Contains("no version read", string.Join(" ", initial.Observation.Reasons ?? []));
        ReleasePublicationResult publication = await PublishAsync(
            preparation, files, initial, store, context.CancellationToken);
        ReleaseCompletionResult completion = Complete(preparation, initial, null, publication);

        Assert.AreEqual(StoreState.Unobserved, publication.Before.StoreState!.Value);
        Assert.IsEmpty(publication.Before.Reads);
        string transfer = JsonSerializer.Serialize(publication,
            TransferJson.Default.ReleasePublicationResult);
        Assert.DoesNotContain("operations/upload-op", transfer);
        Assert.DoesNotContain("operations/submit-op", transfer);
        Assert.DoesNotContain("correlation-id", transfer);
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
    [DataRow("chrome-web-store", "upload chrome-zip", "publish", "observes the store")]
    [DataRow("firefox-amo", "upload firefox-zip", "create version with firefox-sources",
        "publisher must observe")]
    [DataRow("edge-addons", "upload edge-zip", "submit", "no version read")]
    public void DryRunDescribesTheStoreWithoutResolvingCredentials(string store, string upload,
        string submit, string liveRead)
    {
        using var files = new Originals(BrowserStorePreparation.StoreOutputs(store));
        ReleasePreparationResult preparation = files.Preparation(store);
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
            CollectionAssert.AreEqual(new[] { upload, submit }, result.HypotheticalWrites);
            Assert.AreEqual("The store was not observed: a dry run acquires no store " +
                "credential.", result.Reasons[0]);
            Assert.Contains(liveRead, string.Join(" ", result.Reasons));
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
    [DataRow("firefox-amo", "Unobserved")]
    [DataRow("edge-addons", "Unknown")]
    public async Task PublisherDoesNotSubmitWithoutAnAuthorizingPrestate(string store, string state)
    {
        using var files = new Originals(BrowserStorePreparation.StoreOutputs(store));
        ReleasePreparationResult preparation = files.Preparation(store, live: true,
            clearance: store == "edge-addons"
                ? SubmissionClearance.First
                : SubmissionClearance.Unresolved);
        ReleaseObservationResult initial = BrowserStoreDestinationAdapter.Unobserved(preparation);
        var port = new FakeStore(Observation(preparation, Enum.Parse<StoreState>(state)),
            Observation(preparation, StoreState.Received));

        ReleasePublicationResult publication = await PublishAsync(
            preparation, files, initial, port, context.CancellationToken);

        Assert.AreEqual(0, port.Publications);
        Assert.IsEmpty(publication.Steps);
        Assert.IsNotEmpty(publication.Errors);
        Assert.AreEqual(ReleaseBusinessState.Unknown,
            Complete(preparation, initial, null, publication).Outcome.State);
    }

    [TestMethod]
    public void AmoWriteReceiptWithoutAFreshVersionReadCannotEstablishCompletionOrOpenTheAnchor()
    {
        using var files = new Originals(["firefox-zip", "firefox-sources"]);
        ReleasePreparationResult preparation = files.Preparation("firefox-amo", live: true);
        ReleaseObservationResult initial = BrowserStoreDestinationAdapter.Unobserved(preparation);
        var receipt = new ReleasePublicationResult(preparation.Prepared.Subject, initial,
            [ReleaseWriteStep.FromStore("upload", true, "Validated."),
                ReleaseWriteStep.FromStore("create-version", true, "Created.")],
            Observation(preparation, StoreState.InReview), []);

        Assert.ThrowsExactly<InvalidDataException>(() => Complete(preparation, initial, null,
            receipt));
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseEngine.AnchorAllowed(
            ReleaseAdapters.BrowserStore, preparation, initial, receipt));
    }

    [TestMethod]
    [DataRow("missing prerequisite")]
    [DataRow("unexplained prefix")]
    [DataRow("duplicate upload")]
    [DataRow("after failed upload")]
    [DataRow("contradictory acknowledgement")]
    public void StoreReceiptCannotInventAWritePathEvenIfTheVersionIsFinallyPublished(string fault)
    {
        using var files = new Originals(["chrome-zip"]);
        ReleasePreparationResult prep = files.Preparation("chrome-web-store", live: true);
        var initial = Observation(prep, StoreState.Absent);
        var final = Observation(prep, StoreState.Published);
        var upload = ReleaseWriteStep.FromStore("upload", true, "Validated.");
        var publish = ReleaseWriteStep.FromStore("publish", true, "Submitted.");
        ReleaseWriteStep[] steps = fault switch
        {
            "missing prerequisite" => [publish],
            "unexplained prefix" => [upload],
            "duplicate upload" => [upload, upload],
            "after failed upload" =>
                [ReleaseWriteStep.FromStore("upload", false, "Lost."), publish],
            _ => [upload with { Succeeded = false }, publish],
        };
        var receipt = new ReleasePublicationResult(prep.Prepared.Subject, initial, steps, null, []);

        Assert.ThrowsExactly<InvalidDataException>(() => Complete(prep, initial, final, receipt));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseEngine.AnchorAllowed(ReleaseAdapters.BrowserStore, prep, initial, receipt));
    }

    [TestMethod]
    [DataRow("edge-addons")]
    [DataRow("firefox-amo")]
    public void APreGateVersionClaimCannotBypassTheRequiredPublisher(string store)
    {
        using var files = new Originals(BrowserStorePreparation.StoreOutputs(store));
        ReleasePreparationResult prep = files.Preparation(store, live: true);
        var impossibleInitial = Observation(prep, StoreState.Published);
        Assert.IsFalse(ReleaseEngine.AnchorAllowed(ReleaseAdapters.BrowserStore, prep,
            impossibleInitial, null));
        Assert.ThrowsExactly<InvalidDataException>(
            () => Complete(prep, impossibleInitial, null, null));
    }

    [TestMethod]
    public async Task StoreSubmissionIsOneOperationAndCancellationNeverResumesItsUpload()
    {
        using var files = new Originals(["edge-zip"]);
        using var cancel =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var prep = files.Preparation(
            "edge-addons", live: true, clearance: SubmissionClearance.First);
        var initial = BrowserStoreDestinationAdapter.Unobserved(prep);
        var adapter = ReleaseAdapters.BrowserStore;
        var binding = adapter.BindPublication(prep);
        OperationId reviewed = Assert.ContainsSingle(
            binding.Profile.Review(binding.Project(initial)).Reviewed);

        var receipt = await ReleaseEngine.PublishAsync(adapter, prep, files.Root, initial,
            DestinationPublishPlan.Store((_, _) => Task.FromResult(initial), "submit", (stage, _) =>
            {
                Assert.AreEqual("upload", stage);
                cancel.Cancel();
                return Task.FromResult(DestinationInvocation.Store(stage, true, "Uploaded."));
            }, readAfterAttempt: false), cancel.Token);

        Assert.AreEqual("upload", Assert.ContainsSingle(receipt.Steps).Target);
        Assert.Contains("interrupted", Assert.ContainsSingle(receipt.Errors));
        OperationAttempt submission = Assert.ContainsSingle(
            binding.Record(receipt, interrupted: true).Attempts);
        Assert.AreEqual(reviewed, submission.Operation);
        Assert.IsFalse(submission.Acknowledged);
        var report = Complete(prep, initial, null, receipt);
        Assert.AreEqual(ReleaseBusinessState.Unknown, report.Outcome.State);
        Assert.IsTrue(report.MutationAttempted);
    }

    [TestMethod]
    public async Task UnresolvedBlindRequestDoesNotAcquireCredentialsAndRejectsAnIssuedReceipt()
    {
        using var files = new Originals(["edge-zip"]);
        ReleasePreparationResult prep = files.Preparation("edge-addons", live: true);
        var initial = BrowserStoreDestinationAdapter.Unobserved(prep);
        var port = new FakeStore(initial);
        Assert.IsFalse(
            ReleaseEngine.MayEnterPublisher(ReleaseAdapters.BrowserStore, prep, initial));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            PublishAsync(prep, files, initial, port, context.CancellationToken));
        Assert.AreEqual(0, port.Reads);
        Assert.AreEqual(0, port.Publications);

        string preparationPath = Path.Combine(files.Root, "prep.json");
        string initialPath = Path.Combine(files.Root, "initial.json");
        File.WriteAllText(
            preparationPath,
            JsonSerializer.Serialize(prep, TransferJson.Default.ReleasePreparationResult));
        File.WriteAllText(
            initialPath,
            JsonSerializer.Serialize(initial, TransferJson.Default.ReleaseObservationResult));
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseCli.Run(
            ["release", "publish-browser-store", preparationPath, files.Root, initialPath],
            TextWriter.Null,
            new(BrowserStoreFactory: (_, _, _) =>
                throw new AssertFailedException("Mounted store credentials."))));

        var report = Complete(prep, initial, null, null);
        Assert.AreEqual(ReleaseBusinessState.Unknown, report.Outcome.State);
        Assert.IsFalse(report.MutationAttempted);
        Assert.IsFalse(
            ReleaseEngine.AnchorAllowed(ReleaseAdapters.BrowserStore, prep, initial, null));
        var forged = new ReleasePublicationResult(prep.Prepared.Subject, initial,
            [
                ReleaseWriteStep.FromStore("upload", true, "Uploaded."),
                ReleaseWriteStep.FromStore("submit", true, "Received.")
            ],
            Observation(prep, StoreState.Received), []);
        Assert.ThrowsExactly<InvalidDataException>(() => Complete(prep, initial, null, forged));
    }

    [TestMethod]
    public void AnUnconfirmedBlindSubmissionCannotSealAReceivedVersion()
    {
        using var files = new Originals(["edge-zip"]);
        ReleasePreparationResult prep = files.Preparation(
            "edge-addons", live: true, clearance: SubmissionClearance.First);
        var initial = BrowserStoreDestinationAdapter.Unobserved(prep);
        var receipt = new ReleasePublicationResult(prep.Prepared.Subject, initial,
            [
                ReleaseWriteStep.FromStore("upload", true, "Uploaded."),
                ReleaseWriteStep.FromStore(
                    "submit", false, "Unavailable: operation did not finish.")
            ],
            Observation(prep, StoreState.Received), []);

        Assert.ThrowsExactly<InvalidDataException>(() => Complete(prep, initial, null, receipt));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseEngine.AnchorAllowed(ReleaseAdapters.BrowserStore, prep, initial, receipt));
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
    [DataRow("edge-addons", "Unobserved", "false")]
    [DataRow("edge-addons", "Unobserved", "true", "first")]
    [DataRow("edge-addons", "Unobserved", "true", "owner-checked")]
    public void JobInputsBindTheStoreTargetAndRouteWritesThroughTheGate(string store,
        string state, string needed, string? clearance = null)
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
            if (clearance is not null) env["EDGE_CLEARANCE"] = clearance;
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
            ReleaseSubject subject = request.RootElement.GetProperty("subject").Deserialize(
                TransferJson.Default.ReleaseSubject)!;
            preparation =
                preparation with { Prepared = preparation.Prepared with { Subject = subject } };
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
            if (clearance == "owner-checked")
            {
                env["EDGE_CLEARANCE"] = "first";
                Assert.ThrowsExactly<InvalidDataException>(() => Run("receive", "verification"));
            }
            if (store == "chrome-web-store" && state == "Absent")
            {
                env["STORE_ITEM"] = "another";
                Assert.ThrowsExactly<InvalidDataException>(() => Run("receive", "verification"));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ReleaseSubject Subject(string store, bool live = false,
        SubmissionClearance clearance = SubmissionClearance.Unresolved) =>
        new(new string('a', 40), "refs/heads/main", BrowserStorePreparation.DefaultUnit.Id,
            BrowserStorePreparation.DefaultUnit.Declaration, "official", store, !live, clearance);

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
        Subject("chrome-web-store", live: true), Target("chrome-web-store"), Version, "token",
        (_, _) => Task.CompletedTask);

    private static FirefoxAmo Amo(HttpClient client) => new(
        client, Subject("firefox-amo", live: true),
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
        ReleasePreparationResult preparation = originals.Preparation(storeName, live: true,
            clearance: publish ? SubmissionClearance.First : SubmissionClearance.Unresolved);
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

        public StoreWriter Bind(StoreFile[] files) =>
            (_, _) => throw new AssertFailedException("A redirect cannot authorize publication.");
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
        internal int Reads => observed;
        internal int Publications { get; private set; }

        public Task<ReleaseObservationResult> ObserveAsync(CancellationToken token) =>
            Task.FromResult(observations[Math.Min(observed++, observations.Length - 1)]);

        public StoreWriter Bind(StoreFile[] files) => (operation, _) =>
        {
            if (operation != "upload") Publications++;
            return Task.FromResult(DestinationInvocation.Store(operation, true, "Acknowledged."));
        };
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

        internal ReleasePreparationResult Preparation(string store, bool live = false,
            SubmissionClearance clearance = SubmissionClearance.Unresolved)
        {
            ReleaseFile[] files = outputs.Select(output =>
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(root, "original", output +
                    ".zip"));
                return new ReleaseFile("browser-extensions", output, output + "-kind",
                    BrowserStorePreparation.DefaultUnit.Id, Version, "original/" + output + ".zip",
                    bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            }).ToArray();
            string[] required = ["native-input", "build", "contents", "consumer",
                "original-bytes"];
            return new(new(Subject(store, live, clearance), files, required, required.Select(id =>
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
