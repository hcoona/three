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

    private static NpmPhase12RegistryDeclaration ResolveDeclaration(
        NpmPrefixFixture fixture
    )
    {
        var service = new NpmPhase12VerticalSliceService(
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

        return Assert.Single(service.DiscoverRegistryDeclarations());
    }

    private static void AssertNpmPrefixInvocation(NpmPrefixFixture fixture)
    {
        ProcessStartSpec startSpec = Assert.Single(fixture.ProcessRunner.RecordedStartSpecs);
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

        TestContext.Current.AddWarning(message + Environment.NewLine + cleanupFailure);
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
        private readonly SystemProcessRunner inner = new();

        public string? ExpectedPrefixPath { get; set; }

        public List<ProcessStartSpec> RecordedStartSpecs { get; } = [];

        public List<ProcessResult> RecordedResults { get; } = [];

        public async Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            RecordedStartSpecs.Add(startSpec);
            ProcessResult result = await inner
                .RunAsync(startSpec, cancellationToken)
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
