using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace WorkflowDeliveryV3NuGetConsumer;

internal static class NativeRestore
{
    internal static async Task<JsonObject> RunAsync(
        ConsumerRequest request,
        string credential,
        HttpMessageHandler? transport = null,
        CancellationToken cancellationToken = default
    )
    {
        request.ValidateGraph();
        request.ValidateEnvironment();
        ConsumerRequest.Require(
            !Directory.Exists(request.EvidencePath),
            "Consumer restore was already started."
        );
        Directory.CreateDirectory(request.EvidencePath);
        using var clientHandler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            UseDefaultCredentials = false,
            AutomaticDecompression = DecompressionMethods.None,
        };
        using var handler = new BoundedHttpHandler(request, credential, transport ?? clientHandler);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        try
        {
            byte[] effectiveRequest = Encoding.UTF8.GetBytes(request.ToDocument().ToJsonString());
            handler.Save("request.json", effectiveRequest);
            handler.Save(
                "started.json",
                Encoding.UTF8.GetBytes(
                    new JsonObject
                    {
                        ["startedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                        ["graphSha256"] = request.GraphSha256,
                        ["requestSha256"] = ConsumerRequest.Sha256(effectiveRequest),
                        ["maximumRequests"] = request.MaximumRequests,
                        ["maximumResponseBytes"] = request.MaximumResponseBytes,
                        ["timeoutSeconds"] = request.TimeoutSeconds,
                        ["purpose"] = "destination-consumer-restore; no native admission",
                    }.ToJsonString()
                )
            );
            // Install the bounded repository before the cache contains a default one.
            var provider = new CachingSourceProvider(
                new PackageSourceProvider(NullSettings.Instance)
            );
            List<Lazy<INuGetResourceProvider>> resources = Repository
                .Provider.GetCoreV3()
                .Where(item => item.Value is not HttpHandlerResourceV3Provider)
                .ToList();
            resources.Add(
                new Lazy<INuGetResourceProvider>(() =>
                    new ConsumerHttpProvider(clientHandler, handler)
                )
            );
            provider.AddSourceRepository(
                new SourceRepository(
                    new PackageSource(ConsumerRequest.ServiceIndex, "selected"),
                    resources
                )
            );
            ISettings settings = Settings.LoadSpecificSettings(request.Workspace, "nuget.config");
            using var cache = new SourceCacheContext { NoCache = true, DirectDownload = true };
            var arguments = new RestoreArgs
            {
                ConfigFile = request.ConfigPath,
                GlobalPackagesFolder = request.PackagesPath,
                DisableParallel = true,
                AllowNoOp = false,
                CacheContext = cache,
                CachingSourceProvider = provider,
                Log = NullLogger.Instance,
            };
            arguments.PreLoadedRequestProviders.Add(
                new DependencyGraphSpecRequestProvider(
                    new RestoreCommandProvidersCache(),
                    DependencyGraphSpec.Load(request.GraphPath),
                    settings
                )
            );
            IReadOnlyList<RestoreSummary> summaries = await RestoreRunner
                .RunAsync(arguments, deadline.Token)
                .ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            ConsumerRequest.Require(
                summaries.Count == 1 && summaries[0].Success && handler.PackageReturned,
                "Consumer native restore did not complete from the selected HTTP source."
            );

            string directory = Path.Combine(
                request.PackagesPath,
                ConsumerRequest.NormalizedId,
                request.Version
            );
            string filename = ConsumerRequest.NormalizedId + "." + request.Version + ".nupkg";
            byte[] package = await File.ReadAllBytesAsync(
                    Path.Combine(directory, filename),
                    deadline.Token
                )
                .ConfigureAwait(false);
            byte[] witness = await File.ReadAllBytesAsync(
                    Path.Combine(directory, ConsumerRequest.WitnessPath),
                    deadline.Token
                )
                .ConfigureAwait(false);
            string packageSha256 = ConsumerRequest.Sha256(package);
            string packageSha512 = Convert.ToBase64String(SHA512.HashData(package));
            ConsumerRequest.Require(
                packageSha256 == request.PackageSha256
                    && ConsumerRequest.Sha256(witness) == request.WitnessSha256,
                "Consumer installed package or witness differs from the admitted original."
            );
            byte[] assets = await File.ReadAllBytesAsync(
                    Path.Combine(request.Workspace, "obj", "project.assets.json"),
                    deadline.Token
                )
                .ConfigureAwait(false);
            JsonObject libraries = JsonNode.Parse(assets)!["libraries"]!.AsObject();
            ConsumerRequest.Require(libraries.Count == 1, "Unexpected consumer asset libraries.");
            KeyValuePair<string, JsonNode?> library = libraries.Single();
            ConsumerRequest.Require(
                library.Key.Equals(
                    ConsumerRequest.PackageId + "/" + request.Version,
                    StringComparison.OrdinalIgnoreCase
                )
                    && library.Value?["type"]?.GetValue<string>() == "package"
                    && library.Value?["sha512"]?.GetValue<string>() == packageSha512,
                "Consumer assets do not bind the actual installed package."
            );
            handler.Check(package);
            handler.Check(witness);
            handler.Check(assets);
            var result = new JsonObject
            {
                ["schema"] = "workflow-delivery/v3/nuget-consumer-restore-result-v2",
                ["packageRedirectPolicy"] = ConsumerRequest.RedirectPolicy,
                ["httpEvidenceSchema"] = ConsumerRequest.HttpSchema,
                ["packageResponseIndex"] = handler.PackageResponseIndex,
                ["completed"] = true,
                ["packageId"] = ConsumerRequest.NormalizedId,
                ["version"] = request.Version,
                ["packageSha256"] = packageSha256,
                ["packageSha512Base64"] = packageSha512,
                ["witnessSha256"] = ConsumerRequest.Sha256(witness),
                ["graphSha256"] = request.GraphSha256,
                ["requestSha256"] = ConsumerRequest.Sha256(effectiveRequest),
                ["assetsSha256"] = ConsumerRequest.Sha256(assets),
                ["requests"] = handler.Requests,
                ["responseBytes"] = handler.ResponseBytes,
                ["completedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["evidenceLevel"] =
                    "supplied-input native restore; independent provenance "
                    + "and destination audit required",
            };
            handler.Save("result.json", Encoding.UTF8.GetBytes(result.ToJsonString()));
            return result;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            handler.Save(
                "failure.json",
                Encoding.UTF8.GetBytes(
                    new JsonObject
                    {
                        ["completed"] = false,
                        ["errorType"] = error.GetType().Name,
                        ["requests"] = handler.Requests,
                        ["responseBytes"] = handler.ResponseBytes,
                        ["consumerSpent"] = true,
                    }.ToJsonString()
                )
            );
            throw;
        }
    }

    private sealed class ConsumerHttpProvider(HttpClientHandler client, BoundedHttpHandler handler)
        : ResourceProvider(
            typeof(HttpHandlerResource),
            nameof(ConsumerHttpProvider),
            NuGetResourceProviderPositions.Last
        )
    {
        public override Task<Tuple<bool, INuGetResource?>> TryCreate(
            SourceRepository source,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            ConsumerRequest.Require(
                source.PackageSource.Source == ConsumerRequest.ServiceIndex,
                "Unselected consumer source repository."
            );
            return Task.FromResult(
                Tuple.Create<bool, INuGetResource?>(
                    true,
                    new HttpHandlerResourceV3(client, handler)
                )
            );
        }
    }
}
