using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record PythonPackageRunRequest(string Checkout, string Scratch, string Uv,
    string Interpreter, string Helper, PythonMetadataMember[] Members, int DeadlineSeconds,
    IReadOnlyDictionary<string, string?> Environment);
internal sealed record PythonPackageCommand(CheckKey Check, NativeCommand Command,
    NativeCommandResult Result);
internal sealed record PythonOriginalDistribution(string Unit, string Build, PackageOutput Output,
    string Path, long Length, string Sha256);
internal sealed record PythonPackageFailure(CheckKey Check, string Error);
internal sealed record PythonPackageRunResult(string Candidate, CheckResult[] Results,
    PythonPackageCommand[] Commands, PythonOriginalDistribution[] Distributions,
    PythonPackageFailure[] Failures);

// Finite orchestration for the adopted contract; no dependency/backend/version evaluator.
internal static class PythonPackageExecution
{
    internal static Task<PythonPackageRunResult> RunAsync(CiPlan plan,
        PythonPackageRunRequest request, CancellationToken token) =>
        RunAsync(plan, request, NativeProcess.ExecuteAsync, token);

    internal static async Task<PythonPackageRunResult> RunAsync(CiPlan plan,
        PythonPackageRunRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        string checkout = Absolute(request.Checkout), scratch = Absolute(request.Scratch);
        _ = Absolute(request.Interpreter);
        _ = Absolute(request.Helper);
        if (!Directory.Exists(checkout) || !Directory.Exists(scratch) ||
            Within(scratch, checkout) || Within(checkout, scratch) ||
            Directory.EnumerateFileSystemEntries(scratch).Any() || request.DeadlineSeconds <= 0)
            throw new InvalidDataException(
                "Python package execution requires fresh external scratch.");
        for (DirectoryInfo? parent = new(scratch); parent is not null; parent = parent.Parent)
            if (File.Exists(Path.Combine(parent.FullName, ".git")) ||
                Directory.Exists(Path.Combine(parent.FullName, ".git")))
                throw new InvalidDataException("Python consumers require Git-free scratch.");
        ResultCollector.Collect(plan, []);
        if (plan.Scope != PythonFactsAssembler.Scope)
            throw new InvalidDataException("Unsupported Python package execution scope.");
        GitRevision revision = await new GitReader(checkout).ReadAsync(plan.Candidate, token);
        GitMaterialization materialization = await GitMaterialization.BindAsync(checkout,
            revision, token);
        await ValidateAsync(plan, request.Members, materialization, token);
        var results = new Dictionary<CheckKey, CheckResult>();
        var commands = new List<PythonPackageCommand>();
        var distributions = new List<PythonOriginalDistribution>();
        var failures = new List<PythonPackageFailure>();
        int ordinal = 0;
        foreach (PlannedCheck pack in plan.Checks.Where(item =>
                     item.Work.Key.Check == PythonPackageChecks.Build))
        {
            PackageTarget subject = pack.Work.Package!;
            string name = request.Members.Single(member =>
                member.Directory == subject.Directory).Name;
            string root = Path.Combine(scratch, "package-" + ordinal++);
            string tools = Path.Combine(root, "tools");
            string python = Python(tools);
            string archives = Path.Combine(root, "archives");
            var environment = new Dictionary<string, string?>(request.Environment,
                StringComparer.Ordinal)
            {
                ["UV_PROJECT_ENVIRONMENT"] = tools,
                ["UV_CACHE_DIR"] = Path.Combine(scratch, "uv-cache"),
                ["UV_PYTHON_DOWNLOADS"] = "never",
                ["PYTHONPATH"] = null,
                ["PYTHONHOME"] = null,
                ["VIRTUAL_ENV"] = null,
            };
            PythonOriginalDistribution[] originals = [];
            foreach (string kind in PythonPackageChecks.Kinds)
            {
                CheckSpec check = plan.Checks.Single(item =>
                    item.Work.Key == (pack.Work.Key with { Check = kind })).Work;
                CheckStatus status;
                if (token.IsCancellationRequested) status = CheckStatus.Cancelled;
                else if (check.Prerequisites.Any(key => results[key].Status != CheckStatus.Passed))
                    status = CheckStatus.Skipped;
                else try
                    {
                        Directory.CreateDirectory(root);
                        if (kind == PythonPackageChecks.Build)
                        {
                            Directory.CreateDirectory(archives);
                            await UvAsync(checkout, ["sync", "--frozen", "--only-group", "dev",
                                "--no-install-workspace", "--python", request.Interpreter]);
                            await UvAsync(checkout, ["sync", "--frozen", "--only-group",
                                "workflow-delivery-python", "--no-build-isolation", "--no-editable",
                                "--python", request.Interpreter]);
                            NativeCommandResult identity = await HelperAsync(python, checkout,
                                ["identity", Path.Combine(checkout, subject.Directory)]);
                            using JsonDocument native = JsonDocument.Parse(identity.Stdout);
                            if (native.RootElement.GetProperty("name").GetString() != name ||
                                native.RootElement.GetProperty("version").GetString() !=
                                    subject.ExpectedVersion)
                                throw new InvalidDataException(
                                    "Native Python version projection differs.");
                            await UvAsync(checkout, ["build", "--package", name,
                                "--no-build-isolation", "--python", python, "--out-dir", archives,
                                "--preview-features", "build-dependency-check"]);
                            // Build checks completeness and native output identity.
                            NativeCommandResult receipt = await HelperAsync(python, root,
                                ["outputs", archives, name, subject.ExpectedVersion,
                                    PythonPackageChecks.Preset]);
                            originals = ReadDistributions(subject, receipt.Stdout, archives);
                            distributions.AddRange(originals);
                        }
                        else if (kind == PythonPackageChecks.Contents)
                        {
                            foreach (PythonOriginalDistribution original in originals)
                            {
                                RequireOriginal(original);
                                await HelperAsync(python, root, ["archive", original.Path, name,
                                    subject.ExpectedVersion, PythonPackageChecks.Preset]);
                                RequireOriginal(original);
                            }
                        }
                        else
                        {
                            string outputKind = kind == PythonPackageChecks.Wheel
                                ? "python-wheel" : "python-sdist";
                            PythonOriginalDistribution original = originals.Single(item =>
                                item.Output.Kind == outputKind);
                            RequireOriginal(original);
                            string consumer = Directory.CreateDirectory(Path.Combine(root,
                                outputKind + "-consumer")).FullName;
                            string install = original.Path;
                            if (kind == PythonPackageChecks.Sdist)
                            {
                                // No Git coordinates or editable plugin enter this native build.
                                var neutral = new Dictionary<string, string?>(environment,
                                    StringComparer.Ordinal);
                                foreach (System.Collections.DictionaryEntry variable in
                                         System.Environment.GetEnvironmentVariables())
                                    if (((string)variable.Key).StartsWith("GIT_",
                                            StringComparison.Ordinal))
                                        neutral[(string)variable.Key] = null;
                                foreach (string key in request.Environment.Keys.Where(key =>
                                             key.StartsWith("GIT_", StringComparison.Ordinal)))
                                    neutral[key] = null;
                                string rebuilt = Directory.CreateDirectory(Path.Combine(consumer,
                                    "rebuilt")).FullName;
                                await RunAsync(new(request.Uv, consumer, ["build", original.Path,
                                    "--wheel", "--no-build-isolation", "--python", python,
                                    "--out-dir", rebuilt, "--preview-features",
                                    "build-dependency-check", "--offline"], request.DeadlineSeconds,
                                    neutral));
                                string[] outputs = Directory.GetFileSystemEntries(rebuilt);
                                if (outputs.Length != 1 || !outputs[0].EndsWith(".whl",
                                        StringComparison.Ordinal))
                                    throw new InvalidDataException(
                                        "Incomplete Git-free native wheel output.");
                                install = outputs[0];
                                await HelperAsync(python, consumer, ["archive", install, name,
                                    subject.ExpectedVersion, PythonPackageChecks.Preset]);
                            }
                            string venv = Path.Combine(consumer, "venv");
                            await UvAsync(consumer, ["venv", venv, "--python", request.Interpreter,
                                "--no-project"]);
                            await UvAsync(consumer, ["pip", "install", "--python", Python(venv),
                                "--no-deps", "--no-cache", "--offline", install]);
                            await HelperAsync(Python(venv), consumer, ["consume", name,
                                subject.ExpectedVersion, PythonPackageChecks.Preset]);
                            RequireOriginal(original);
                        }
                        token.ThrowIfCancellationRequested();
                        status = CheckStatus.Passed;
                    }
                    catch (PackageCommandFailure exception)
                    { status = exception.Status; failures.Add(new(check.Key, exception.Message)); }
                    catch (OperationCanceledException) { status = CheckStatus.Cancelled; }
                    catch (Exception exception) when (exception is IOException or
                        InvalidDataException or JsonException or
                        UnauthorizedAccessException or ArgumentException or
                            InvalidOperationException
                            or KeyNotFoundException)
                    { status = CheckStatus.Failed; failures.Add(new(check.Key,
                        exception.Message)); }
                results.Add(check.Key, new(plan.Candidate, check.Key, status));

                Task<NativeCommandResult> UvAsync(string directory, string[] arguments) =>
                    RunAsync(new(request.Uv, directory, arguments, request.DeadlineSeconds,
                        environment));
                Task<NativeCommandResult> HelperAsync(string executable, string directory,
                    string[] arguments) => RunAsync(new(executable, directory,
                        ["-I", request.Helper, .. arguments], request.DeadlineSeconds,
                            environment));
                async Task<NativeCommandResult> RunAsync(NativeCommand command)
                {
                    NativeCommandResult result = await execute(command, token);
                    commands.Add(new(check.Key, command, result));
                    if (!result.Succeeded || result.Error is not null)
                        throw new PackageCommandFailure(result.Termination switch
                        {
                            NativeTermination.Cancelled => CheckStatus.Cancelled,
                            NativeTermination.TimedOut => CheckStatus.TimedOut,
                            _ => CheckStatus.Failed,
                        },
                            result.Error ??
                            $"Native command {result.Termination}, exit {result.ExitCode}.");
                    return result;
                }
            }
        }
        return new(plan.Candidate, plan.Checks.Select(item => results[item.Work.Key]).ToArray(),
            commands.ToArray(), distributions.ToArray(), failures.ToArray());
    }

    private static async Task ValidateAsync(CiPlan plan, PythonMetadataMember[] members,
        GitMaterialization materialization, CancellationToken token)
    {
        foreach (var group in plan.Checks.GroupBy(item =>
                     (item.Work.Key.Target, item.Work.Key.Variant)))
        {
            PackageTarget subject = group.First().Work.Package ??
                throw new InvalidDataException("Missing Python package subject.");
            string text = await materialization.ReadOptionalTextAsync(subject.Declaration, token)
                ?? throw new InvalidDataException("Missing committed Python release declaration.");
            ReleaseUnitDeclaration declaration = ReleaseUnitDeclarationReader.Read(text,
                subject.Declaration);
            CheckSpec[] declared = PythonPackageChecks.Expand(
                declaration with { Builds = declaration.Builds.Where(build =>
                    build.Id == subject.Build).ToArray() }, members,
                new Dictionary<string, string> { [subject.Directory] = subject.ExpectedVersion });
            CheckSpec[] expected = declared.Where(check => check.Package!.Build == subject.Build)
                .ToArray();
            if (group.Count() != 4 || expected.Length != 4 ||
                await materialization.ReadOptionalTextAsync(subject.EntryPoint, token) is null)
                throw new InvalidDataException("Incomplete committed Python package obligations.");
            foreach (PlannedCheck item in group)
            {
                CheckSpec? match = expected.SingleOrDefault(check => check.Key == item.Work.Key);
                if (match is null || !item.Origins.SequenceEqual([CheckOrigin.Preset]) ||
                    !item.QualityPresets.SequenceEqual([PythonPackageChecks.Preset]) ||
                    item.Work.Runner != match.Runner || item.Work.Required != match.Required ||
                    item.Work.Dimensions.Count != 0 ||
                    !item.Work.Prerequisites.SequenceEqual(match.Prerequisites) ||
                    !ImpactPlanner.SamePackage(item.Work.Package, match.Package))
                    throw new InvalidDataException("Conflicting Python package quality contract.");
            }
        }
    }

    private static PythonOriginalDistribution[] ReadDistributions(PackageTarget subject,
        string text, string directory)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        var originals = new List<PythonOriginalDistribution>();
        foreach (JsonElement value in document.RootElement.EnumerateArray())
        {
            string kind = value.GetProperty("kind").GetString()!;
            PackageOutput output = subject.Outputs.Single(item => item.Kind == kind);
            string path = Absolute(value.GetProperty("path").GetString()!);
            if (Path.GetDirectoryName(path) != directory ||
                originals.Any(item => item.Output == output))
                throw new InvalidDataException("Ambiguous original Python output.");
            var original = new PythonOriginalDistribution(subject.Unit, subject.Build, output,
                path, value.GetProperty("length").GetInt64(), value.GetProperty(
                    "sha256").GetString()!);
            RequireOriginal(original);
            originals.Add(original);
        }
        if (originals.Count != 2 || Directory.GetFileSystemEntries(directory).Length != 2)
            throw new InvalidDataException("Incomplete original Python output set.");
        return originals.ToArray();
    }

    private static void RequireOriginal(PythonOriginalDistribution original)
    {
        var info = new FileInfo(original.Path);
        if (!info.Exists || info.LinkTarget is not null || info.Length != original.Length)
            throw new InvalidDataException("Original Python distribution changed.");
        using FileStream stream = info.OpenRead();
        if (Convert.ToHexStringLower(SHA256.HashData(stream)) != original.Sha256)
            throw new InvalidDataException("Original Python distribution bytes changed.");
    }

    private sealed class PackageCommandFailure(CheckStatus status,
        string message) : Exception(message)
    { internal CheckStatus Status { get; } = status; }
    private static string Python(string venv) => Path.Combine(venv,
        OperatingSystem.IsWindows() ? "Scripts/python.exe" : "bin/python");
    private static string Absolute(string path) => Path.IsPathFullyQualified(path)
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
        : throw new InvalidDataException("Python package paths must be absolute.");
    private static bool Within(string child, string parent) => child == parent ||
        child.StartsWith(parent + Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
