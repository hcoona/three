using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Core;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal sealed record ReleaseCliOverrides(
    Func<ReleasePreparationRequest, CancellationToken, Task<ReleasePreparationResult>>? Prepare =
        null,
    Func<PreparedRelease, string, CancellationToken, Task<ReleaseObservationResult>>?
        ObserveNuGet = null,
    TimeSpan? SettleInterval = null,
    Func<PreparedRelease, CancellationToken, Task<ReleaseObservationResult>>? ObserveNpmjs = null,
    Func<PreparedRelease, PythonIndexTools, CancellationToken, Task<ReleaseObservationResult>>?
        ObservePython = null,
    Func<PreparedRelease, string, string, PythonPublisherTools, CancellationToken,
        Task<PythonPublishResult>>? PublishPython = null,
    Func<PreparedRelease, RubySourceTools, RubyReadCredential?, CancellationToken,
        Task<ReleaseObservationResult>>? ObserveRuby = null,
    Func<ReleasePreparationResult, string, RubyPublisherTools,
        IReadOnlyDictionary<string, string?>, CancellationToken, Task<RubyPublishResult>>?
        PublishRuby = null,
    Func<HttpClient>? HttpClientFactory = null,
    Func<ReleasePreparationResult, bool, HttpClient, IBrowserStore>? BrowserStoreFactory = null,
    Func<string, string?>? Environment = null);

internal static class ReleaseCli
{
    internal static int? Run(string[] args, TextWriter output,
        ReleaseCliOverrides? overrides = null)
    {
        if (ReleaseJobInputs.Run(args, overrides?.Environment) is { } jobStatus) return jobStatus;
        if (args is not ["release", var action, .. var rest]) return null;

        if (action == "prepare-python-index-tools" && rest is [var toolRequest])
        {
            PythonIndexToolResult result = PythonIndexToolPreparation.PrepareAsync(
                Read(toolRequest, DestinationToolJson.Default.PythonIndexToolRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            Write(output, result, DestinationToolJson.Default.PythonIndexToolResult);
            return result.Errors.Length == 0 ? 0 : 1;
        }

        if (action.StartsWith("prepare-", StringComparison.Ordinal) && rest is [var requestPath])
        {
            (IDestinationAdapter? adapter, Func<ReleasePreparationRequest, CancellationToken,
                Task<ReleasePreparationResult>>? defaultPrepare) = PrepareTarget(
                    action["prepare-".Length..]);
            if (adapter is null || defaultPrepare is null) return null;
            ReleasePreparationResult result = (overrides?.Prepare ?? defaultPrepare)(
                Read(requestPath, TransferJson.Default.ReleasePreparationRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            Write(output, result, TransferJson.Default.ReleasePreparationResult);
            if (result.Errors.Length != 0 || result.Prepared.Files.Length == 0) return 1;
            adapter.ValidatePreparation(result);
            if (overrides?.Prepare is null &&
                action is "prepare-npm" or "prepare-python" or "prepare-ruby" or
                    "prepare-browser-store" &&
                result.Prepared.Subject.Channel == "official" &&
                result.Prepared.Anchor is null)
                throw new InvalidDataException(
                    "Official preparation omitted its GitHub Release anchor.");
            return 0;
        }

        if (action.StartsWith("describe-", StringComparison.Ordinal) &&
            rest is [var describePreparedPath, var describeObservedPath, .. var describeAnchor] &&
            describeAnchor.Length <= 1)
        {
            (IDestinationAdapter? adapter, string? expectedDestination) =
                ResolveTarget(action["describe-".Length..]);
            if (adapter is null) return null;
            ReleasePreparationResult preparation = Preparation(describePreparedPath);
            RequireDestination(preparation.Prepared.Subject, expectedDestination);
            ReleaseObservationResult observed = Observation(describeObservedPath);
            ReleaseObservationResult? anchorObserved = describeAnchor is [var anchorObsPath]
                ? ReadOptional(anchorObsPath, TransferJson.Default.ReleaseObservationResult)
                : null;
            ReleaseDryRun result = ReleaseEngine.Describe(adapter, preparation, observed,
                anchorObserved);
            Write(output, result, TransferJson.Default.ReleaseDryRun);
            return result.Ready ? 0 : 1;
        }

        if (action.StartsWith("complete-", StringComparison.Ordinal) &&
            rest is [var completePreparedPath, var initialPath, var finalPath, var publishedPath,
                .. var completeAnchor] && completeAnchor.Length is 0 or 3)
        {
            (IDestinationAdapter? adapter, string? expectedDestination) =
                ResolveTarget(action["complete-".Length..]);
            if (adapter is null) return null;
            ReleasePreparationResult preparation = Preparation(completePreparedPath);
            RequireDestination(preparation.Prepared.Subject, expectedDestination);
            (ReleaseObservationResult? anchorInitial, ReleaseObservationResult? anchorFinal,
                ReleasePublicationResult? anchorPublished) = completeAnchor is
                [var aInit, var aFinal, var aPub]
                    ? (ReadOptional(aInit, TransferJson.Default.ReleaseObservationResult),
                        ReadOptional(aFinal, TransferJson.Default.ReleaseObservationResult),
                        ReadOptional(aPub, TransferJson.Default.ReleasePublicationResult))
                    : (null, null, null);
            ReleaseCompletionResult result = ReleaseEngine.Complete(adapter, preparation,
                Observation(initialPath),
                ReadOptional(finalPath, TransferJson.Default.ReleaseObservationResult),
                ReadOptional(publishedPath, TransferJson.Default.ReleasePublicationResult),
                anchorInitial, anchorFinal, anchorPublished);
            Write(output, result, TransferJson.Default.ReleaseCompletionResult);
            return result.Outcome.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }

        return (action, rest) switch
        {
            ("observe-nuget", [var prepPath, var originals, var publicationPath]) =>
                ObserveNuGet(prepPath, originals, publicationPath, output, overrides),
            ("publish-nuget", [var prepPath, var originals, var initialObsPath]) =>
                PublishNuGet(prepPath, originals, initialObsPath, output, overrides),
            ("observe-npmjs", [var prepPath, var originals]) =>
                ObserveNpm(prepPath, originals, "npmjs", output, overrides),
            ("observe-github-npm", [var prepPath, var originals]) =>
                ObserveNpm(prepPath, originals, "github-packages-npm", output, overrides),
            ("publish-npmjs", [var prepPath, var originals, var initialObsPath, var scratch,
                    var toolchain]) => PublishNpm(prepPath, originals, initialObsPath, scratch,
                        toolchain, "npmjs", output, overrides),
            ("publish-github-npm", [var prepPath, var originals, var initialObsPath, var scratch,
                    var toolchain]) => PublishNpm(prepPath, originals, initialObsPath, scratch,
                        toolchain, "github-packages-npm", output, overrides),
            ("observe-python", [var prepPath, var originals, var indexToolsPath]) =>
                ObservePython(prepPath, originals, indexToolsPath, output, overrides),
            ("publish-python", [var prepPath, var originals, var initialObsPath, var indexToolsPath,
                var publisherToolsPath]) =>
                PublishPython(prepPath, originals, initialObsPath, indexToolsPath,
                    publisherToolsPath, output, overrides),
            ("observe-ruby", [var prepPath, var originals, var toolsPath]) =>
                ObserveRuby(prepPath, originals, toolsPath, output, overrides),
            ("publish-ruby", [var prepPath, var originals, var initialObsPath, var readToolsPath,
                var publisherToolsPath]) =>
                PublishRuby(prepPath, originals, initialObsPath, readToolsPath, publisherToolsPath,
                    output, overrides),
            ("observe-github-release", [var prepPath, var originals]) =>
                ObserveGithubRelease(prepPath, originals, output, overrides),
            ("publish-github-release", [var prepPath, var originals, var initialObsPath]) =>
                PublishGithubRelease(prepPath, originals, initialObsPath, output, overrides),
            ("observe-github-anchor", [var prepPath, var originals, var anchorOutputPath]) =>
                ObserveGithubAnchor(prepPath, originals, anchorOutputPath, overrides),
            ("may-publish-github-anchor", [var prepPath, var primaryObsPath, var anchorObsPath,
                var primaryPubPath]) =>
                MayPublishGithubAnchor(
                    prepPath, primaryObsPath, anchorObsPath, primaryPubPath, output),
            ("publish-github-anchor", [var prepPath, var originals, var primaryObsPath,
                var anchorObsPath, var primaryPubPath]) =>
                PublishGithubAnchor(prepPath, originals, primaryObsPath, anchorObsPath,
                    primaryPubPath, output, overrides),
            ("observe-browser-store", [var prepPath, var originals]) =>
                ObserveBrowserStore(prepPath, originals, output, overrides),
            ("publish-browser-store", [var prepPath, var originals, var initialObsPath]) =>
                PublishBrowserStore(prepPath, originals, initialObsPath, output, overrides),
            _ => null,
        };
    }

    private static (IDestinationAdapter? Adapter, Func<ReleasePreparationRequest, CancellationToken,
        Task<ReleasePreparationResult>>? Prepare) PrepareTarget(string ecosystem) =>
        ecosystem switch
        {
            "nuget" => (ReleaseAdapters.NuGet, NuGetPreparation.PrepareAsync),
            "npm" => (ReleaseAdapters.Npm, NpmPreparation.PrepareAsync),
            "python" => (ReleaseAdapters.Python, PythonPreparation.PrepareAsync),
            "ruby" => (ReleaseAdapters.Ruby, RubyPreparation.PrepareAsync),
            "github-release" => (ReleaseAdapters.GithubRelease,
                GithubReleasePreparation.PrepareAsync),
            "browser-store" => (ReleaseAdapters.BrowserStore,
                BrowserStorePreparation.PrepareAsync),
            _ => (null, null),
        };

    private static (IDestinationAdapter? Adapter, string? ExpectedDestination) ResolveTarget(
        string target) => target switch
        {
            "nuget" => (ReleaseAdapters.NuGet, null),
            "npmjs" => (ReleaseAdapters.Npm, "npmjs"),
            "github-npm" => (ReleaseAdapters.Npm, "github-packages-npm"),
            "python" => (ReleaseAdapters.Python, null),
            "ruby" => (ReleaseAdapters.Ruby, null),
            "github-release" => (ReleaseAdapters.GithubRelease, null),
            "browser-store" => (ReleaseAdapters.BrowserStore, null),
            _ => (null, null),
        };

    private static void RequireDestination(ReleaseSubject subject, string? expected)
    {
        if (expected is not null && subject.Destination != expected)
            throw new InvalidDataException("Unsupported destination for release route.");
    }

    private static int ObserveNuGet(string preparationPath, string originals,
        string publicationPath, TextWriter output, ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        ReleaseAdapters.NuGet.VerifyOriginals(preparation, originals);
        using HttpClient client = RegistryHttpClient.Create();
        bool settle = ReadOptional(publicationPath, TransferJson.Default.ReleasePublicationResult)
            ?.Invocation is { ReturnedSuccessfully: true };
        string credential = NuGetObserveCredential(preparation.Prepared, overrides?.Environment);
        Func<PreparedRelease, string, CancellationToken, Task<ReleaseObservationResult>> reader =
            overrides?.ObserveNuGet ?? NuGetObserver(preparation.Prepared, client);
        ReleaseObservationResult result = ReleaseEngine.SettleAsync(preparation.Prepared,
            (prepared, token) => reader(prepared, credential, token),
            settle ? ReleaseEngine.DefaultSettleDeadline : TimeSpan.Zero,
            overrides?.SettleInterval ?? ReleaseEngine.DefaultSettleInterval,
            CancellationToken.None).GetAwaiter().GetResult();
        return EmitObservation(ReleaseAdapters.NuGet, preparation, result, output);
    }

    private static int PublishNuGet(string preparationPath, string originals, string initialPath,
        TextWriter output, ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        ReleaseObservationResult initial = Observation(initialPath);
        using HttpClient client = RegistryHttpClient.Create();
        PreparedRelease selected = preparation.Prepared;
        Func<PreparedRelease, string, CancellationToken, Task<ReleaseObservationResult>> reader =
            overrides?.ObserveNuGet ?? NuGetObserver(selected, client);
        string observeCredential = NuGetObserveCredential(selected, overrides?.Environment);
        string publishCredential = RequiredEnv(
            selected.Subject.Destination == "nugetorg" ? "NUGET_API_KEY" : "GITHUB_TOKEN",
            "Current destination credential is unavailable.", overrides?.Environment);
        ReleasePublicationResult result = ReleaseAdapters.NuGet.PublishAsync(preparation,
            initial, originals, observeCredential, publishCredential, reader,
            (prepared, directory, endpoint, credential, token) =>
                NuGetPublisher.PushAsync(DestinationBindings.NuGet(prepared), directory, endpoint,
                    credential, token),
            CancellationToken.None).GetAwaiter().GetResult();
        return EmitPublication(result, output);
    }

    private static Func<PreparedRelease, string, CancellationToken, Task<ReleaseObservationResult>>
        NuGetObserver(PreparedRelease prepared, HttpClient client) =>
        prepared.Subject.Destination == "nugetorg"
            ? (selected, credential, token) => new NuGetOrgObservation(client).ObserveAsync(
                DestinationBindings.NuGet(selected), credential, token)
            : (selected, credential, token) => new GithubNuGetObservation(client).ObserveAsync(
                DestinationBindings.NuGet(selected), credential, token);

    private static string NuGetObserveCredential(PreparedRelease prepared,
        Func<string, string?>? environment) =>
        prepared.Subject.Destination == "nugetorg"
            ? ""
            : RequiredEnv("GITHUB_TOKEN", "Current destination credential is unavailable.",
                environment);

    private static int ObserveNpm(string preparationPath, string originals, string destination,
        TextWriter output, ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        RequireDestination(preparation.Prepared.Subject, destination);
        ReleaseEngine.VerifyOriginals(preparation.Prepared, originals);
        ReleaseObservationResult result;
        if (destination == "npmjs")
        {
            if (overrides?.ObserveNpmjs is { } controlled)
                result = controlled(preparation.Prepared, CancellationToken.None)
                    .GetAwaiter().GetResult();
            else
            {
                using HttpClient client = RegistryHttpClient.Create();
                result = new NpmjsObservation(client)
                    .ObserveAsync(DestinationBindings.Npm(preparation.Prepared),
                        CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
        }
        else
        {
            using HttpClient client = RegistryHttpClient.Create();
            result = new GithubNpmObservation(client).ObserveAsync(
                DestinationBindings.Npm(preparation.Prepared),
                RequiredEnv("GITHUB_TOKEN", "Current GitHub read/write credential is unavailable.",
                    overrides?.Environment), CancellationToken.None).GetAwaiter().GetResult();
        }
        return EmitObservation(ReleaseAdapters.Npm, preparation, result, output);
    }

    private static int PublishNpm(string preparationPath, string originals, string initialPath,
        string scratch, string toolchain, string destination, TextWriter output,
        ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        RequireDestination(preparation.Prepared.Subject, destination);
        ReleaseEngine.VerifyOriginals(preparation.Prepared, originals);
        ReleaseObservationResult initial = Observation(initialPath);
        using HttpClient client = RegistryHttpClient.Create();
        ReleasePublicationResult result;
        if (destination == "npmjs")
        {
            var reader = new NpmjsObservation(client);
            var platform = Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value,
                    StringComparer.Ordinal);
            result = ReleaseAdapters.Npm.PublishNpmjsAsync(preparation, initial, originals, scratch,
                toolchain, platform, NativeProcess.ExecuteAsync,
                (prepared, token) => reader.ObserveAsync(DestinationBindings.Npm(prepared), token),
                CancellationToken.None).GetAwaiter().GetResult();
        }
        else
        {
            var reader = new GithubNpmObservation(client);
            result = ReleaseAdapters.Npm.PublishGithubAsync(
                preparation, initial, originals, scratch,
                toolchain, RequiredEnv("GITHUB_TOKEN",
                    "Current GitHub read/write credential is unavailable.",
                    overrides?.Environment),
                NativeProcess.ExecuteAsync, (prepared, credential, token) => reader.ObserveAsync(
                    DestinationBindings.Npm(prepared), credential, token), CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        return EmitPublication(result, output);
    }

    private static int ObservePython(string preparationPath, string originals,
        string indexToolsPath, TextWriter output, ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        ReleaseAdapters.Python.VerifyOriginals(preparation, originals);
        ReleaseObservationResult result = PythonObserveAsync(preparation.Prepared,
            Read(indexToolsPath, DestinationToolJson.Default.PythonIndexTools), overrides,
            CancellationToken.None).GetAwaiter().GetResult();
        return EmitObservation(ReleaseAdapters.Python, preparation, result, output);
    }

    private static int PublishPython(string preparationPath, string originals, string initialPath,
        string indexToolsPath, string publisherToolsPath, TextWriter output,
        ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        PythonIndexTools indexTools = Read(
            indexToolsPath, DestinationToolJson.Default.PythonIndexTools);
        PythonPublisherTools publisherTools = Read(publisherToolsPath,
            DestinationToolJson.Default.PythonPublisherTools);
        ReleasePublicationResult result = ReleaseEngine.PublishAsync(ReleaseAdapters.Python,
            preparation, originals, Observation(initialPath),
            DestinationPublishPlan.Files(preparation.Prepared,
                (prepared, token) => PythonObserveAsync(prepared, indexTools with
                {
                    Scratch = FreshScratch(indexTools.Scratch,
                        "Python phase scratch parent must be absolute."),
                }, overrides, token),
                async (file, token) =>
                {
                    PythonPublisherTools stepTools = publisherTools with
                    {
                        Scratch = FreshScratch(publisherTools.Scratch,
                            "Python phase scratch parent must be absolute."),
                    };
                    PythonPublishResult invocation = overrides?.PublishPython is { } controlled
                        ? await controlled(preparation.Prepared, originals, file.Output,
                            stepTools, token)
                        : await DefaultPythonPublishAsync(preparation.Prepared, originals,
                            file.Output, stepTools, overrides?.Environment, token);
                    return ReleaseWriteStep.FromUpload(invocation.Upload,
                        invocation.MaskForwardingFailed);
                }), CancellationToken.None).GetAwaiter().GetResult();
        return EmitPublication(result, output);
    }

    private static async Task<ReleaseObservationResult> PythonObserveAsync(PreparedRelease prepared,
        PythonIndexTools tools, ReleaseCliOverrides? overrides, CancellationToken token)
    {
        if (overrides?.ObservePython is { } controlled)
            return await controlled(prepared, tools, token);
        using HttpClient client = RegistryHttpClient.Create();
        return await new PythonIndexObservation(client, tools).ObserveAsync(
            DestinationBindings.Python(prepared), token);
    }

    private static Task<PythonPublishResult> DefaultPythonPublishAsync(PreparedRelease prepared,
        string originals, string selected, PythonPublisherTools tools,
        Func<string, string?>? env, CancellationToken token)
    {
        Func<string, string?> get = env ?? Environment.GetEnvironmentVariable;
        var platform = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string name in new[] { "GITHUB_ACTIONS", "ACTIONS_ID_TOKEN_REQUEST_URL",
                     "ACTIONS_ID_TOKEN_REQUEST_TOKEN", "SystemRoot" })
            platform[name] = get(name);
        return PythonNativePublisher.PublishAsync(DestinationBindings.Python(prepared), originals,
            selected, tools, platform, Console.Error, token);
    }

    private static int ObserveRuby(string preparationPath, string originals, string toolsPath,
        TextWriter output, ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        ReleaseAdapters.Ruby.VerifyOriginals(preparation, originals);
        ReleaseObservationResult result = RubyObserveAsync(preparation.Prepared,
            Read(toolsPath, DestinationToolJson.Default.RubySourceTools), overrides,
            CancellationToken.None).GetAwaiter().GetResult();
        return EmitObservation(ReleaseAdapters.Ruby, preparation, result, output);
    }

    private static int PublishRuby(string preparationPath, string originals, string initialPath,
        string readToolsPath, string publisherToolsPath, TextWriter output,
        ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        RubySourceTools readTools = Read(
            readToolsPath, DestinationToolJson.Default.RubySourceTools);
        RubyPublisherTools publisherTools = Read(publisherToolsPath,
            DestinationToolJson.Default.RubyPublisherTools);
        Func<string, string?> get = overrides?.Environment ?? Environment.GetEnvironmentVariable;
        var platform = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string name in new[] { "GITHUB_TOKEN", "GEM_HOST_API_KEY" })
            platform[name] = get(name);
        ReleasePublicationResult result = ReleaseEngine.PublishAsync(ReleaseAdapters.Ruby,
            preparation, originals, Observation(initialPath),
            DestinationPublishPlan.Files(preparation.Prepared,
                (prepared, token) => RubyObserveAsync(prepared, readTools with
                {
                    Scratch = FreshScratch(readTools.Scratch,
                        "Ruby phase scratch parent must be absolute."),
                }, overrides, token),
                async (_, token) =>
                {
                    RubyPublisherTools tools = publisherTools with
                    {
                        Scratch = FreshScratch(publisherTools.Scratch,
                            "Ruby phase scratch parent must be absolute."),
                    };
                    RubyPublishResult invocation = overrides?.PublishRuby is { } controlled
                        ? await controlled(preparation, originals, tools, platform, token)
                        : await RubyNativePublisher.PublishAsync(DestinationBindings.Ruby(
                            preparation.Prepared), originals, tools, platform, token);
                    if (invocation.Subject != preparation.Prepared.Subject)
                        throw new InvalidDataException("Ruby publisher returned another request.");
                    return ReleaseWriteStep.FromUpload(invocation.Upload);
                },
                DestinationReadbackMode.AllowCancellation),
            CancellationToken.None).GetAwaiter().GetResult();
        return EmitPublication(result, output);
    }

    private static Task<ReleaseObservationResult> RubyObserveAsync(PreparedRelease prepared,
        RubySourceTools tools, ReleaseCliOverrides? overrides, CancellationToken token)
    {
        Func<string, string?> get = overrides?.Environment ?? Environment.GetEnvironmentVariable;
        RubyReadCredential? credential = prepared.Subject.Destination == "github-packages-ruby"
            ? new(get("GITHUB_ACTOR") ?? "", get("GITHUB_TOKEN") ?? "") : null;
        return overrides?.ObserveRuby is { } controlled
            ? controlled(prepared, tools, credential, token)
            : RubyDestinationObservation.ObserveAsync(DestinationBindings.Ruby(prepared), tools,
                credential, token);
    }

    private static int ObserveGithubRelease(string preparationPath, string originals,
        TextWriter output, ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        ReleaseEngine.VerifyOriginals(preparation.Prepared, originals);
        ReleaseObservationResult result = WithGithubReleaseDestination(overrides, write: false,
            destination => destination.ObserveAsync(DestinationBindings.GithubRelease(
                preparation.Prepared), CancellationToken.None));
        return EmitObservation(ReleaseAdapters.GithubRelease, preparation, result, output);
    }

    private static int PublishGithubRelease(string preparationPath, string originals,
        string initialPath, TextWriter output, ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        ReleaseObservationResult initial = Observation(initialPath);
        ReleasePublicationResult result = WithGithubReleaseDestination(overrides, write: true,
            destination => ReleaseAdapters.GithubRelease.PublishAsync(preparation, originals,
                initial, destination, CancellationToken.None));
        return EmitPublication(result, output);
    }

    private static int ObserveGithubAnchor(string preparationPath, string originals,
        string anchorOutputPath, ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        if (preparation.Prepared.Anchor is not { } anchor) return 0;
        PrimaryAdapterFor(preparation.Prepared.Subject).ValidatePreparation(preparation);
        ReleasePreparationResult anchorPrep = ReleaseAdapters.AnchorPreparation(preparation);
        ReleaseAdapters.GithubAnchor.ValidatePreparation(anchorPrep);
        ReleaseEngine.VerifyOriginals(anchorPrep.Prepared, originals);
        ReleaseObservationResult result = WithGithubReleaseDestination(overrides, write: false,
            destination => destination.ObserveAsync(DestinationBindings.GithubRelease(
                anchorPrep.Prepared, anchor.AllowedSiblingAssets), CancellationToken.None));
        ReleaseAdapters.GithubAnchor.ValidateObservation(anchorPrep, result);
        File.WriteAllText(anchorOutputPath, JsonSerializer.Serialize(result,
            TransferJson.Default.ReleaseObservationResult) + "\n");
        return ReleaseEngine.AnchorObservationProceeds(anchorPrep.Prepared.Subject, result) ? 0 : 1;
    }

    private static int MayPublishGithubAnchor(string preparationPath, string primaryObsPath,
        string anchorObsPath, string primaryPubPath, TextWriter output)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        ReleaseSubject subject = preparation.Prepared.Subject;
        if (subject.DryRun || subject.Channel != "official")
            throw new InvalidDataException("A repository anchor requires a live Official request.");
        ReleaseAdapters.ValidateOptionalAnchor(preparation);
        ReleasePreparationResult anchor = ReleaseAdapters.AnchorPreparation(preparation);
        bool missing = ReleaseEngine.MayEnterPublisher(ReleaseAdapters.GithubAnchor, anchor,
            Observation(anchorObsPath));
        bool permitted = missing && ReleaseEngine.AnchorAllowed(PrimaryAdapterFor(subject),
            preparation, Observation(primaryObsPath),
            ReadOptional(primaryPubPath, TransferJson.Default.ReleasePublicationResult));
        output.WriteLine(permitted ? "true" : "false");
        return 0;
    }

    private static int PublishGithubAnchor(string preparationPath, string originals,
        string primaryObsPath, string anchorObsPath, string primaryPubPath,
        TextWriter output, ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        IDestinationAdapter primaryAdapter = PrimaryAdapterFor(preparation.Prepared.Subject);
        ReleaseObservationResult primaryInitial = Observation(primaryObsPath);
        ReleasePublicationResult? primaryPublication = ReadOptional(primaryPubPath,
            TransferJson.Default.ReleasePublicationResult);
        ReleaseEngine.RequireAnchorAllowed(primaryAdapter, preparation, primaryInitial,
            primaryPublication);
        PreparedGithubAnchor anchor = preparation.Prepared.Anchor!;
        ReleasePreparationResult anchorPrep = ReleaseAdapters.AnchorPreparation(preparation);
        ReleaseObservationResult anchorInitial = Observation(anchorObsPath);
        ReleasePublicationResult result = WithGithubReleaseDestination(overrides, write: true,
            destination => ReleaseAdapters.GithubAnchor.PublishAsync(anchorPrep, originals,
                anchorInitial, anchor.AllowedSiblingAssets, destination, CancellationToken.None));
        return EmitPublication(result, output);
    }

    private static IDestinationAdapter PrimaryAdapterFor(ReleaseSubject subject) =>
        subject.Destination switch
        {
            "nugetorg" or "github-packages-nuget" => ReleaseAdapters.NuGet,
            "npmjs" or "github-packages-npm" => ReleaseAdapters.Npm,
            "pypi" or "testpypi" => ReleaseAdapters.Python,
            "rubygems" or "github-packages-ruby" => ReleaseAdapters.Ruby,
            BrowserStorePreparation.ChromeWebStore or BrowserStorePreparation.FirefoxAmo or
                BrowserStorePreparation.EdgeAddons => ReleaseAdapters.BrowserStore,
            _ => throw new InvalidDataException(
                "Unsupported destination for GitHub Release anchor."),
        };

    private static T WithGithubReleaseDestination<T>(ReleaseCliOverrides? overrides, bool write,
        Func<GithubReleaseDestination, Task<T>> action)
    {
        using HttpClient http = overrides?.HttpClientFactory?.Invoke()
            ?? RegistryHttpClient.Create();
        using HttpClient? writeHttp = write && overrides?.HttpClientFactory is null
            ? RegistryHttpClient.CreateNoRedirect() : null;
        return action(new(http, RequiredEnv("GITHUB_TOKEN",
            "GitHub Release requires GITHUB_TOKEN.", overrides?.Environment),
            writeClient: writeHttp)).GetAwaiter().GetResult();
    }

    private static int ObserveBrowserStore(string preparationPath, string originals,
        TextWriter output, ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        _ = BrowserStoreDestinationAdapter.OriginalsAsync(preparation, originals,
            CancellationToken.None).GetAwaiter().GetResult();
        ReleaseObservationResult result;
        if (ReleaseDecisions.CanReadBeforeGate(
                BrowserStoreDestinationAdapter.VersionReadForStore(preparation.Target?.Store),
                preparation.Prepared.Subject.DryRun))
        {
            using HttpClient client = RegistryHttpClient.CreateNoRedirect();
            IBrowserStore store = (overrides?.BrowserStoreFactory ?? CreateBrowserStore)(
                preparation, false, client);
            result = store.ObserveAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        else
            result = BrowserStoreDestinationAdapter.Unobserved(preparation);
        return EmitObservation(ReleaseAdapters.BrowserStore, preparation, result, output);

        IBrowserStore CreateBrowserStore(ReleasePreparationResult prep, bool writeAccess,
            HttpClient client) => DefaultBrowserStore(prep, writeAccess, client,
                overrides?.Environment);
    }

    private static int PublishBrowserStore(string preparationPath, string originals,
        string initialPath, TextWriter output, ReleaseCliOverrides? overrides)
    {
        ReleasePreparationResult preparation = Preparation(preparationPath);
        ReleaseObservationResult initial = Observation(initialPath);
        if (!ReleaseEngine.MayEnterPublisher(ReleaseAdapters.BrowserStore, preparation, initial))
            throw new InvalidDataException("This request cannot enter the store publisher.");
        StoreFile[] files = BrowserStoreDestinationAdapter.OriginalsAsync(preparation, originals,
            CancellationToken.None).GetAwaiter().GetResult();
        using HttpClient client = RegistryHttpClient.CreateNoRedirect();
        IBrowserStore store = (overrides?.BrowserStoreFactory
            ?? ((prep, writeAccess, http) => DefaultBrowserStore(prep, writeAccess, http,
                overrides?.Environment)))(preparation, true, client);
        ReleasePublicationResult result = ReleaseAdapters.BrowserStore.PublishAsync(preparation,
            originals, files, initial, store, CancellationToken.None)
            .GetAwaiter().GetResult();
        return EmitPublication(result, output);
    }

    private static IBrowserStore DefaultBrowserStore(ReleasePreparationResult preparation,
        bool write, HttpClient client, Func<string, string?>? environment)
    {
        ReleaseSubject subject = preparation.Prepared.Subject;
        BrowserStoreTarget target = preparation.Target
            ?? throw new InvalidDataException("Missing browser-store target.");
        string version = preparation.Prepared.Files[0].Version;
        return target.Store switch
        {
            BrowserStorePreparation.ChromeWebStore => new ChromeWebStore(client, subject, target,
                version, RequiredEnv("CWS_ACCESS_TOKEN",
                    "Missing store credential: CWS_ACCESS_TOKEN", environment)),
            BrowserStorePreparation.FirefoxAmo when write => new FirefoxAmo(client, subject,
                target, version, RequiredEnv("AMO_JWT_ISSUER",
                    "Missing store credential: AMO_JWT_ISSUER", environment),
                RequiredEnv("AMO_JWT_SECRET", "Missing store credential: AMO_JWT_SECRET",
                    environment)),
            BrowserStorePreparation.EdgeAddons when write => new EdgeAddons(client, subject,
                target, version, RequiredEnv("EDGE_API_KEY",
                    "Missing store credential: EDGE_API_KEY", environment),
                RequiredEnv("EDGE_CLIENT_ID", "Missing store credential: EDGE_CLIENT_ID",
                    environment)),
            _ => throw new InvalidDataException("Store has no read outside its publisher."),
        };
    }

    private static int EmitObservation(IDestinationAdapter adapter,
        ReleasePreparationResult preparation, ReleaseObservationResult result, TextWriter output)
    {
        adapter.ValidateObservation(preparation, result);
        Write(output, result, TransferJson.Default.ReleaseObservationResult);
        return ReleaseEngine.ObservationProceeds(adapter.Profile(preparation.Prepared.Subject),
            result.Observation) ? 0 : 1;
    }

    private static int EmitPublication(ReleasePublicationResult result, TextWriter output)
    {
        Write(output, result, TransferJson.Default.ReleasePublicationResult);
        return result.Errors.Length == 0 ? 0 : 1;
    }

    private static ReleasePreparationResult Preparation(string path) =>
        Read(path, TransferJson.Default.ReleasePreparationResult);

    private static ReleaseObservationResult Observation(string path) =>
        Read(path, TransferJson.Default.ReleaseObservationResult);

    internal static T? ReadOptional<T>(string path, JsonTypeInfo<T> type) where T : class =>
        path == "-" ? null : Read(path, type);

    internal static void Write<T>(TextWriter output, T value, JsonTypeInfo<T> type) =>
        output.WriteLine(JsonSerializer.Serialize(value, type));

    internal static string RequiredEnv(string name, string message,
        Func<string, string?>? get = null) =>
        (get ?? Environment.GetEnvironmentVariable)(name) is { Length: > 0 } value
            ? value : throw new InvalidDataException(message);

    internal static string FreshScratch(string parent, string message) =>
        Path.IsPathFullyQualified(parent) && Directory.Exists(parent)
            ? Directory.CreateDirectory(Path.Combine(parent, Guid.NewGuid().ToString("N"))).FullName
            : throw new InvalidDataException(message);

    internal static T Read<T>(string path, JsonTypeInfo<T> type)
    {
        string text = File.ReadAllText(path, StrictJson.Utf8);
        using JsonDocument document = ParseTransfer(text);
        return document.RootElement.Deserialize(type)
            ?? throw new InvalidDataException("Missing required release input.");
    }

    private static JsonDocument ParseTransfer(string text)
    {
        try { return StrictJson.Parse(text); }
        catch (JsonException error)
        {
            JsonDocument.Parse(text).Dispose();
            throw new InvalidDataException("Duplicate release input property.", error);
        }
    }
}
