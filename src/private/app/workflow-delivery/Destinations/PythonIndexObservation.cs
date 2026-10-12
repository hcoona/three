using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record PythonIndexTools(string Interpreter, string Helper, string Scratch);
internal sealed record PythonIndexRead(string Stage, int? Status, string Detail);
internal sealed record PythonIndexObservationResult(ReleaseObservation Observation,
    PythonIndexRead[] Reads);

// Packaging interprets native coordinates. This component reads the selected
// public index and compares qualified originals; it has no publication capability.
internal sealed class PythonIndexObservation(HttpClient client, PythonIndexTools tools,
    TimeProvider? clock = null)
{
    private const string Package = "hcoona-release-smoke-python";
    private const int MetadataLimit = 2 * 1024 * 1024;
    private const int ArchiveLimit = 8 * 1024 * 1024;

    internal static HttpClient CreateClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        UseDefaultCredentials = false,
        AutomaticDecompression = DecompressionMethods.None,
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    internal Task<PythonIndexObservationResult> ObserveAsync(PreparedRelease prepared,
        CancellationToken token) => ObserveAsync(prepared, NativeProcess.ExecuteAsync, token);

    internal async Task<PythonIndexObservationResult> ObserveAsync(PreparedRelease prepared,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        ReleaseCompletion.Validate(prepared);
        if (prepared.Files.Length != 2 ||
            prepared.Files.Count(file => file.Kind == "python-wheel") != 1 ||
            prepared.Files.Count(file => file.Kind == "python-sdist") != 1 ||
            prepared.Files.Any(file => file.Name != Package || file.Size > ArchiveLimit ||
                file.Version != prepared.Files[0].Version) ||
            prepared.RequiredChecks.Any(id => !prepared.Checks.Any(check => check.Id == id &&
                check.Status == ReleaseCheckStatus.Passed)))
            throw new InvalidDataException("Unsupported or unqualified Python original pair.");
        string origin = (prepared.Subject.Channel, prepared.Subject.Destination) switch
        {
            ("buddy", "testpypi") => "test.pypi.org",
            ("official", "pypi") => "pypi.org",
            _ => throw new InvalidDataException("Unsupported public Python destination."),
        };
        string storage = origin == "test.pypi.org" ? "test-files.pythonhosted.org"
            : "files.pythonhosted.org";
        if (!Path.IsPathFullyQualified(tools.Interpreter) ||
            !Path.IsPathFullyQualified(tools.Helper) ||
            !Path.IsPathFullyQualified(tools.Scratch) || !Directory.Exists(tools.Scratch) ||
            Directory.EnumerateFileSystemEntries(tools.Scratch).Any())
            throw new InvalidDataException("Public index facts require owned empty tool scratch.");

        var reads = new List<PythonIndexRead>();
        var files = prepared.Files.Select(file => FileState(file, ReleaseFileState.Unknown))
            .ToArray();
        ReleaseCheckStatus? phaseFailure = null;
        string input = Path.Combine(tools.Scratch, Guid.NewGuid().ToString("N") + ".json");
        using var phase = new CancellationTokenSource(TimeSpan.FromSeconds(90),
            clock ?? TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, phase.Token);
        try
        {
            var indexUri = new Uri($"https://{origin}/simple/{Package}/");
            (int status, byte[] body) = await ReadAsync("index", indexUri, origin, MetadataLimit);
            if (status == 404) return All(ReleaseFileState.Absent);
            if (status != 200) return All(ReleaseFileState.Unknown);
            await File.WriteAllBytesAsync(input, body, lifetime.Token);
            NativeCommandResult native = await execute(new(tools.Interpreter, tools.Scratch,
                ["-I", "-B", tools.Helper, "index-facts", input, Package,
                    prepared.Files[0].Version], 30, InheritEnvironment: false), lifetime.Token);
            reads.Add(new("native-facts", null,
                $"Native facts terminated as {native.Termination}, exit {native.ExitCode}."));
            lifetime.Token.ThrowIfCancellationRequested();
            if (!native.Succeeded) return All(ReleaseFileState.Unknown);
            using JsonDocument facts = JsonDocument.Parse(native.Stdout);
            if (!facts.RootElement.GetProperty("matches_name").GetBoolean())
                return All(ReleaseFileState.Conflict);
            JsonElement entries = facts.RootElement.GetProperty("files");
            if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 4096)
                return All(ReleaseFileState.Unknown);
            string[] expected = prepared.Files.Select(file => Path.GetFileName(file.Path))
                .ToArray();
            string[] actual = entries.EnumerateArray().Select(entry =>
                entry.GetProperty("filename").GetString()!).ToArray();
            if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length ||
                actual.Any(name => !expected.Contains(name, StringComparer.Ordinal)))
                return All(ReleaseFileState.Conflict);
            for (int i = 0; i < prepared.Files.Length; i++)
            {
                ReleaseFile original = prepared.Files[i];
                JsonElement? entry = entries.EnumerateArray().Where(value =>
                    value.GetProperty("filename").GetString() == expected[i])
                    .Select(value => (JsonElement?)value).SingleOrDefault();
                if (entry is null)
                {
                    files[i] = FileState(original, ReleaseFileState.Absent);
                    continue;
                }
                JsonElement metadata = entry.Value;
                if (metadata.GetProperty("yanked").GetBoolean() ||
                    !StringComparer.OrdinalIgnoreCase.Equals(
                        metadata.GetProperty("sha256").GetString(), original.Sha256) ||
                    metadata.GetProperty("size") is { ValueKind: not JsonValueKind.Null } size &&
                        size.GetInt64() != original.Size)
                {
                    files[i] = FileState(original, ReleaseFileState.Conflict);
                    continue;
                }
                var archive = new Uri(indexUri, metadata.GetProperty("url").GetString());
                ValidateUri(archive, storage);
                (status, body) = await ReadAsync(original.Output, archive, storage, ArchiveLimit);
                if (status != 200) continue;
                string digest = Convert.ToHexStringLower(SHA256.HashData(body));
                files[i] = new(original.Output, original.Name, original.Version,
                    digest == original.Sha256 && body.LongLength == original.Size
                        ? ReleaseFileState.Exact : ReleaseFileState.Conflict,
                    body.LongLength, digest);
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return Result();
        }
        catch (OperationCanceledException)
        {
            phaseFailure = token.IsCancellationRequested ? ReleaseCheckStatus.Cancelled
                : ReleaseCheckStatus.TimedOut;
            reads.Add(new("operation", null, token.IsCancellationRequested
                ? "Cancelled." : "Read or native-fact deadline exceeded."));
            return Result();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or
            InvalidDataException or JsonException or KeyNotFoundException or
            InvalidOperationException or UriFormatException or ArgumentException or
            FormatException or OverflowException)
        {
            phaseFailure = ReleaseCheckStatus.Failed;
            reads.Add(new("operation", null, "Native facts or complete response unavailable."));
            return Result();
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
        }

        PythonIndexObservationResult All(ReleaseFileState state)
        {
            files = prepared.Files.Select(file => FileState(file, state)).ToArray();
            return Result();
        }
        PythonIndexObservationResult Result() => new(new(prepared.Subject, files,
            ["public-index-files"], [new("public-index-files",
                phaseFailure ?? (files.All(file => file.State is
                    ReleaseFileState.Absent or ReleaseFileState.Exact)
                    ? ReleaseCheckStatus.Passed : ReleaseCheckStatus.Failed))]), [.. reads]);

        async Task<(int, byte[])> ReadAsync(string stage, Uri uri, string host, int limit)
        {
            ValidateUri(uri, host);
            using var readTimer = new CancellationTokenSource(TimeSpan.FromSeconds(30),
                clock ?? TimeProvider.System);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,
                readTimer.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd(stage == "index"
                ? "application/vnd.pypi.simple.v1+json" : "application/octet-stream");
            request.Headers.AcceptEncoding.ParseAdd("identity");
            request.Headers.CacheControl = new() { NoCache = true };
            using HttpResponseMessage response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.RequestMessage?.RequestUri is { } actualUri && actualUri != uri)
                throw new InvalidDataException("Unexpected public response redirect.");
            int code = (int)response.StatusCode;
            reads.Add(new(stage, code, "Received public response headers."));
            if (code != 200 && code != 404) return (code, []);
            if (response.Content.Headers.ContentEncoding.Any(value =>
                !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unsupported public response encoding.");
            if (response.Content.Headers.ContentLength > limit)
                throw new IOException("Public response exceeds its body limit.");
            using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var content = new MemoryStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, deadline.Token)) != 0)
            {
                if (content.Length + count > limit)
                    throw new IOException("Public response exceeds its body limit.");
                await content.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
            }
            if (response.Content.Headers.ContentLength is { } length && content.Length != length)
                throw new IOException("Incomplete public response body.");
            deadline.Token.ThrowIfCancellationRequested();
            return (code, content.ToArray());
        }
    }

    private static ReleaseFileObservation FileState(ReleaseFile file, ReleaseFileState state)
        => new(file.Output, file.Name, file.Version, state, null, null);

    private static void ValidateUri(Uri uri, string host)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Host != host || uri.Port != 443 ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("URL differs from the selected public Python origin.");
    }
}
