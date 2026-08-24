using System.Collections.Concurrent;
using System.Globalization;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

#pragma warning disable CA1707
[Collection(NpmPhase12NpmIntegrationTestCollectionDefinition.Name)]
public sealed class NpmPhase12NpmIntegrationTests
{
    private const int FixtureCleanupAttemptCount = 5;
    private static readonly TimeSpan FixtureCleanupRetryDelay = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void ResolveWorkspaceAsync_UsesRealNpmPrefix_ForWorkspaceMember()
    {
        Assert.SkipUnless(IsNpmInstalled(), "Real npm integration requires npm on PATH.");
        NpmPrefixFixture fixture = NpmPrefixFixture.Create(
            "packages/*",
            "packages/member",
            "member"
        );
        bool completedSuccessfully = false;
        try
        {
            NpmPhase12RegistryDeclaration declaration = ResolveDeclaration(fixture);

            Assert.Equal(Path.Combine(fixture.RootPath, ".npmrc"), declaration.SourcePath);
            Assert.Equal("@root:registry", declaration.Key);
            Assert.Equal(
                "https://pkgs.dev.azure.com/org/_packaging/root/npm/registry/",
                declaration.RegistryUrl.AbsoluteUri
            );
            AssertNpmPrefixInvocation(fixture);
            completedSuccessfully = true;
        }
        finally
        {
            CompleteFixtureCleanup(fixture, completedSuccessfully);
        }
    }

    [Fact]
    public async Task ResolveWorkspaceAsync_ReportsRealNpmPrefixMilestones()
    {
        Assert.SkipUnless(IsNpmInstalled(), "Real npm integration requires npm on PATH.");
        NpmPrefixFixture fixture = NpmPrefixFixture.Create(
            "packages/*",
            "packages/member",
            "member"
        );
        bool completedSuccessfully = false;
        try
        {
            _ = await CreateService(fixture)
                .ResolveWorkspaceAsync(
                    cancellationToken: TestContext.Current.CancellationToken
                );

            AssertNpmPrefixSpecification(fixture);
            string warning = Assert.IsType<string>(
                fixture.ProcessRunner.RecordedMilestoneWarning
            );
            completedSuccessfully = true;
            Assert.Skip(warning);
        }
        finally
        {
            CompleteFixtureCleanup(fixture, completedSuccessfully);
        }
    }

    [Fact]
    public void ResolveWorkspaceAsync_UsesRealNpmPrefix_ForNonWorkspacePackage()
    {
        Assert.SkipUnless(IsNpmInstalled(), "Real npm integration requires npm on PATH.");
        NpmPrefixFixture fixture = NpmPrefixFixture.Create(
            "packages/*",
            "tools/nonmember",
            "nonmember"
        );
        bool completedSuccessfully = false;
        try
        {
            NpmPhase12RegistryDeclaration declaration = ResolveDeclaration(fixture);

            Assert.Equal(
                Path.Combine(fixture.InvocationPath, ".npmrc"),
                declaration.SourcePath
            );
            Assert.Equal("@nonmember:registry", declaration.Key);
            Assert.Equal(
                "https://pkgs.dev.azure.com/org/_packaging/nonmember/npm/registry/",
                declaration.RegistryUrl.AbsoluteUri
            );
            AssertNpmPrefixInvocation(fixture);
            completedSuccessfully = true;
        }
        finally
        {
            CompleteFixtureCleanup(fixture, completedSuccessfully);
        }
    }

    [Fact]
    public void ResolveWorkspaceAsync_UsesRealNpmPrefix_ForCharacterClassWorkspaceMember()
    {
        Assert.SkipUnless(IsNpmInstalled(), "Real npm integration requires npm on PATH.");
        NpmPrefixFixture fixture = NpmPrefixFixture.Create(
            "packages/[a-z]*",
            "packages/apple",
            "apple"
        );
        bool completedSuccessfully = false;
        try
        {
            NpmPhase12RegistryDeclaration declaration = ResolveDeclaration(fixture);

            Assert.Equal(Path.Combine(fixture.RootPath, ".npmrc"), declaration.SourcePath);
            Assert.Equal("@root:registry", declaration.Key);
            Assert.Equal(
                "https://pkgs.dev.azure.com/org/_packaging/root/npm/registry/",
                declaration.RegistryUrl.AbsoluteUri
            );
            AssertNpmPrefixInvocation(fixture);
            completedSuccessfully = true;
        }
        finally
        {
            CompleteFixtureCleanup(fixture, completedSuccessfully);
        }
    }

    [Fact]
    public void ResolveWorkspaceAsync_UsesNativeInstalledNpm_OnWindows()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Native installed npm smoke requires Windows."
        );
        Assert.SkipUnless(IsNpmInstalled(), "Native installed npm smoke requires npm on PATH.");
        NpmPrefixFixture fixture = NpmPrefixFixture.Create(
            "packages/*",
            "packages/member",
            "member"
        );
        bool completedSuccessfully = false;
        try
        {
            NpmPhase12RegistryDeclaration declaration = ResolveDeclaration(fixture);

            Assert.Equal(Path.Combine(fixture.RootPath, ".npmrc"), declaration.SourcePath);
            Assert.Equal("@root:registry", declaration.Key);
            Assert.Equal(
                "https://pkgs.dev.azure.com/org/_packaging/root/npm/registry/",
                declaration.RegistryUrl.AbsoluteUri
            );
            AssertNpmPrefixInvocation(fixture);
            completedSuccessfully = true;
        }
        finally
        {
            CompleteFixtureCleanup(fixture, completedSuccessfully);
        }
    }

    [Fact]
    public void RecordingSystemProcessRunner_DiagnosticSpecChangesOnlyTimeout()
    {
        var outputCaptureOptions = new ProcessOutputCaptureOptions
        {
            StandardOutputByteLimit = 123,
            StandardErrorByteLimit = 456,
        };
        using var standardErrorTee = new StringWriter(CultureInfo.InvariantCulture);
        var startSpec = new ProcessStartSpec(
            "diagnostic-tool",
            ["first", "second"],
            "diagnostic-working-directory",
            new Dictionary<string, string?>
            {
                ["DIAGNOSTIC_VALUE"] = "value",
                ["DIAGNOSTIC_REMOVED"] = null,
            },
            "diagnostic-input",
            TimeSpan.FromSeconds(10),
            outputCaptureOptions,
            standardErrorTee
        );

        ProcessStartSpec diagnosticStartSpec =
            RecordingSystemProcessRunner.CreateDiagnosticStartSpec(startSpec);

        Assert.NotSame(startSpec, diagnosticStartSpec);
        Assert.Equal(startSpec.FileName, diagnosticStartSpec.FileName);
        Assert.Equal(startSpec.Arguments, diagnosticStartSpec.Arguments);
        Assert.Equal(startSpec.WorkingDirectory, diagnosticStartSpec.WorkingDirectory);
        Assert.Equal(startSpec.Environment.Count, diagnosticStartSpec.Environment.Count);
        foreach ((string key, string? value) in startSpec.Environment)
        {
            Assert.True(diagnosticStartSpec.Environment.TryGetValue(key, out string? actual));
            Assert.Equal(value, actual);
        }
        Assert.Equal(startSpec.StandardInput, diagnosticStartSpec.StandardInput);
        Assert.Equal(TimeSpan.FromSeconds(10), startSpec.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(60), diagnosticStartSpec.Timeout);
        Assert.Same(startSpec.OutputCaptureOptions, diagnosticStartSpec.OutputCaptureOptions);
        Assert.Same(startSpec.StandardErrorTee, diagnosticStartSpec.StandardErrorTee);
    }

    [Fact]
    public void RecordingSystemProcessRunner_FormatsFixedSafeMilestoneWarning()
    {
        var milestones =
            new Dictionary<SystemProcessRunner.ProcessMilestoneName, TimeSpan>
            {
                [SystemProcessRunner.ProcessMilestoneName.LaunchRequested] =
                    TimeSpan.FromMilliseconds(1.25),
                [SystemProcessRunner.ProcessMilestoneName.ProcessDisposalCompleted] =
                    TimeSpan.FromMilliseconds(2.5),
            };

        string warning = RecordingSystemProcessRunner.CreateMilestoneWarning(milestones);

        Assert.Equal(
            "azureauth_npm_process_milestones"
                + " LaunchRequestedMs=1.250"
                + " ProcessStartedMs=missing"
                + " StandardInputClosedMs=missing"
                + " ProcessExitedMs=missing"
                + " StandardOutputEofMs=missing"
                + " StandardErrorEofMs=missing"
                + " TimeoutInitiatedMs=missing"
                + " KillCompletedMs=missing"
                + " ProcessDisposalCompletedMs=2.500",
            warning
        );
    }

    private static NpmPhase12RegistryDeclaration ResolveDeclaration(
        NpmPrefixFixture fixture
    )
    {
        return Assert.Single(CreateService(fixture).DiscoverRegistryDeclarations());
    }

    private static NpmPhase12VerticalSliceService CreateService(
        NpmPrefixFixture fixture
    )
    {
        return new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = new SystemFileSystem(),
                ProcessRunner = fixture.ProcessRunner,
                EnvironmentVariableReader = static name =>
                    name is "PATH" or "Path" or "PATHEXT"
                        ? Environment.GetEnvironmentVariable(name)
                        : null,
                WorkspaceDirectoryPath = fixture.InvocationPath,
                UserNpmrcPath = Path.Combine(fixture.RootPath, "user", ".npmrc"),
            }
        );
    }

    private static void AssertNpmPrefixInvocation(NpmPrefixFixture fixture)
    {
        AssertNpmPrefixSpecification(fixture);
        ProcessResult result = Assert.Single(fixture.ProcessRunner.RecordedResults);
        Assert.True(result.Succeeded);
        Assert.True(
            string.Equals(
                fixture.ExpectedPrefixPath,
                result.StandardOutput.Trim(),
                StringComparison.Ordinal
            )
                || string.Equals(
                    RedactSessionStateIdentifier(fixture.ExpectedPrefixPath),
                    result.StandardOutput.Trim(),
                    StringComparison.Ordinal
                )
        );
    }

    private static void AssertNpmPrefixSpecification(NpmPrefixFixture fixture)
    {
        ProcessStartSpec startSpec = Assert.Single(fixture.ProcessRunner.RecordedStartSpecs);
        Assert.False(
            startSpec.FileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Equal("prefix", startSpec.Arguments[^1]);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("npm", startSpec.FileName);
            Assert.Single(startSpec.Arguments);
        }
        Assert.Equal(fixture.InvocationPath, startSpec.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(10), startSpec.Timeout);
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardOutputByteLimit);
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardErrorByteLimit);
    }

    private static void CompleteFixtureCleanup(
        NpmPrefixFixture fixture,
        bool completedSuccessfully
    )
    {
        Exception? cleanupFailure = TryDeleteFixtureDirectory(fixture.RootPath);
        if (cleanupFailure is null)
        {
            return;
        }

        const string message = "npm prefix fixture cleanup failed.";
        if (completedSuccessfully)
        {
            throw new InvalidOperationException(message, cleanupFailure);
        }

        TestContext.Current.AddWarning(message);
    }

    private static Exception? TryDeleteFixtureDirectory(string path)
    {
        Exception? cleanupFailure = null;
        for (var attempt = 1; attempt <= FixtureCleanupAttemptCount; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                cleanupFailure = exception;
            }

            if (attempt < FixtureCleanupAttemptCount)
            {
                Thread.Sleep(FixtureCleanupRetryDelay);
            }
        }

        return cleanupFailure;
    }

    private static string RedactSessionStateIdentifier(string path)
    {
        string separator = Path.DirectorySeparatorChar.ToString();
        string sessionStateMarker =
            separator + ".copilot" + separator + "session-state" + separator;
        int identifierStart =
            path.IndexOf(sessionStateMarker, StringComparison.Ordinal)
            + sessionStateMarker.Length;
        if (identifierStart < sessionStateMarker.Length)
        {
            return path;
        }

        string filesMarker = separator + "files" + separator;
        int identifierEnd = path.IndexOf(
            filesMarker,
            identifierStart,
            StringComparison.Ordinal
        );
        return identifierEnd < 0
            ? path
            : path[..identifierStart] + "***" + path[identifierEnd..];
    }

    private static bool IsNpmInstalled()
    {
        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        IReadOnlyList<string> candidateNames = OperatingSystem.IsWindows()
            ? GetSupportedWindowsNpmCandidateNames()
            : ["npm"];
        foreach (string directory in pathValue.Split(Path.PathSeparator))
        {
            string normalizedDirectory = directory.Trim().Trim('"');
            if (normalizedDirectory.Length == 0)
            {
                continue;
            }

            foreach (string candidateName in candidateNames)
            {
                if (File.Exists(Path.Combine(normalizedDirectory, candidateName)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<string> GetSupportedWindowsNpmCandidateNames()
    {
        string? pathExtValue = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExtValue))
        {
            return ["npm.exe", "npm.cmd"];
        }

        var candidateNames = new List<string>(capacity: 2);
        foreach (string configuredExtension in pathExtValue.Split(';'))
        {
            string normalizedExtension = configuredExtension.Trim().Trim('"');
            string? candidateName =
                normalizedExtension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    ? "npm.exe"
                : normalizedExtension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                    ? "npm.cmd"
                    : null;
            if (
                candidateName is not null
                && !candidateNames.Contains(candidateName, StringComparer.OrdinalIgnoreCase)
            )
            {
                candidateNames.Add(candidateName);
            }
        }

        return candidateNames;
    }

    private sealed class RecordingSystemProcessRunner : IProcessRunner
    {
        private static readonly TimeSpan DiagnosticTimeout = TimeSpan.FromSeconds(60);
        private static readonly SystemProcessRunner.ProcessMilestoneName[] MilestoneNames =
        [
            SystemProcessRunner.ProcessMilestoneName.LaunchRequested,
            SystemProcessRunner.ProcessMilestoneName.ProcessStarted,
            SystemProcessRunner.ProcessMilestoneName.StandardInputClosed,
            SystemProcessRunner.ProcessMilestoneName.ProcessExited,
            SystemProcessRunner.ProcessMilestoneName.StandardOutputEof,
            SystemProcessRunner.ProcessMilestoneName.StandardErrorEof,
            SystemProcessRunner.ProcessMilestoneName.TimeoutInitiated,
            SystemProcessRunner.ProcessMilestoneName.KillCompleted,
            SystemProcessRunner.ProcessMilestoneName.ProcessDisposalCompleted,
        ];
        private const string MissingMilestone = "missing";

        public string? ExpectedPrefixPath { get; set; }

        public List<ProcessStartSpec> RecordedStartSpecs { get; } = [];

        public List<ProcessResult> RecordedResults { get; } = [];

        public string? RecordedMilestoneWarning { get; private set; }

        public async Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            var milestones =
                new ConcurrentDictionary<
                    SystemProcessRunner.ProcessMilestoneName,
                    TimeSpan
                >();
            try
            {
                RecordedStartSpecs.Add(startSpec);
                var inner = new SystemProcessRunner(milestone =>
                    milestones.TryAdd(milestone.Name, milestone.Elapsed)
                );
                ProcessResult result = await inner
                    .RunAsync(CreateDiagnosticStartSpec(startSpec), cancellationToken)
                    .ConfigureAwait(false);
                RecordedResults.Add(result);
                if (!result.Succeeded || ExpectedPrefixPath is null)
                {
                    return result;
                }

                string actualPrefixPath = result.StandardOutput.Trim();
                return string.Equals(
                        actualPrefixPath,
                        ExpectedPrefixPath,
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        actualPrefixPath,
                        RedactSessionStateIdentifier(ExpectedPrefixPath),
                        StringComparison.Ordinal
                    )
                    ? new ProcessResult(
                        0,
                        ExpectedPrefixPath + Environment.NewLine,
                        result.StandardError
                    )
                    : result;
            }
            finally
            {
                string warning = CreateMilestoneWarning(milestones);
                RecordedMilestoneWarning = warning;
                TestContext.Current.AddWarning(warning);
            }
        }

        internal static ProcessStartSpec CreateDiagnosticStartSpec(
            ProcessStartSpec startSpec
        )
        {
            return new ProcessStartSpec(
                startSpec.FileName,
                startSpec.Arguments,
                startSpec.WorkingDirectory,
                startSpec.Environment,
                startSpec.StandardInput,
                DiagnosticTimeout,
                startSpec.OutputCaptureOptions,
                startSpec.StandardErrorTee
            );
        }

        internal static string CreateMilestoneWarning(
            IReadOnlyDictionary<
                SystemProcessRunner.ProcessMilestoneName,
                TimeSpan
            > milestones
        )
        {
            return "azureauth_npm_process_milestones "
                + string.Join(
                    ' ',
                    MilestoneNames.Select(name => FormatMilestone(milestones, name))
                );
        }

        private static string FormatMilestone(
            IReadOnlyDictionary<
                SystemProcessRunner.ProcessMilestoneName,
                TimeSpan
            > milestones,
            SystemProcessRunner.ProcessMilestoneName name
        )
        {
            string elapsedMilliseconds = milestones.TryGetValue(
                name,
                out TimeSpan elapsed
            )
                ? elapsed.TotalMilliseconds.ToString(
                    "0.000",
                    CultureInfo.InvariantCulture
                )
                : MissingMilestone;
            return name + "Ms=" + elapsedMilliseconds;
        }
    }

    private sealed class NpmPrefixFixture
    {
        private NpmPrefixFixture(string rootPath, string invocationPath)
        {
            RootPath = rootPath;
            InvocationPath = invocationPath;
        }

        public string RootPath { get; }

        public string InvocationPath { get; }

        public string ExpectedPrefixPath { get; private init; } = string.Empty;

        public RecordingSystemProcessRunner ProcessRunner { get; } = new();

        public static NpmPrefixFixture Create(
            string workspacePattern,
            string invocationRelativePath,
            string invocationPackageName
        )
        {
            string rootPath = Path.Combine(
                Path.GetTempPath(),
                "azureauth-credprovider-npm-prefix-" + Guid.NewGuid().ToString("N")
            );
            string invocationPath = Path.Combine(
                rootPath,
                invocationRelativePath.Replace('/', Path.DirectorySeparatorChar)
            );
            Directory.CreateDirectory(invocationPath);
            File.WriteAllText(
                Path.Combine(rootPath, "package.json"),
                $$"""
                {
                  "name": "fixture-root",
                  "private": true,
                  "workspaces": ["{{workspacePattern}}"]
                }
                """
            );
            File.WriteAllText(
                Path.Combine(invocationPath, "package.json"),
                $$"""
                {
                  "name": "{{invocationPackageName}}",
                  "private": true
                }
                """
            );
            File.WriteAllText(
                Path.Combine(rootPath, ".npmrc"),
                "@root:registry=https://pkgs.dev.azure.com/org/_packaging/root/npm/registry/\n"
            );
            File.WriteAllText(
                Path.Combine(invocationPath, ".npmrc"),
                $"@{invocationPackageName}:registry=https://pkgs.dev.azure.com/"
                    + $"org/_packaging/{invocationPackageName}/npm/registry/\n"
            );
            var fixture = new NpmPrefixFixture(rootPath, invocationPath)
            {
                ExpectedPrefixPath =
                    invocationRelativePath.StartsWith("tools/", StringComparison.Ordinal)
                        ? invocationPath
                        : rootPath,
            };
            fixture.ProcessRunner.ExpectedPrefixPath = fixture.ExpectedPrefixPath;
            return fixture;
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NpmPhase12NpmIntegrationTestCollectionDefinition
{
    public const string Name = "npm integration";
}
#pragma warning restore CA1707
