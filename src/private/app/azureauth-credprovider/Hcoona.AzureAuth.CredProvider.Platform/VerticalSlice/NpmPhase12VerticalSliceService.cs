using System.Diagnostics.CodeAnalysis;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record NpmPhase12VerticalSliceOptions
{
    public IFileSystem? FileSystem { get; init; }

    public Func<string, string?>? EnvironmentVariableReader { get; init; }

    public string? WorkspaceDirectoryPath { get; init; }

    public string? UserHomeDirectoryPath { get; init; }

    public string? UserNpmrcPath { get; init; }
}

public sealed record NpmPhase12RegistryDeclaration
{
    public required string SourcePath { get; init; }

    public required string Key { get; init; }

    public required Uri RegistryUrl { get; init; }

    public required CanonicalResourceIdentity ResourceIdentity { get; init; }

    public required NpmCompatibleAuthSelectors AuthSelectors { get; init; }
}

public sealed record NpmPhase12CredentialPlanRequest
{
    public required NpmPhase12RegistryDeclaration Declaration { get; init; }

    public required string AuthToken { get; init; }

    public CredentialEcosystem Ecosystem { get; init; } = CredentialEcosystem.Npm;

    public string? TargetNpmrcPath { get; init; }
}

public sealed class NpmPhase12VerticalSliceService
{
    private const string ProductId = "azureauth-credprovider";
    private const string ProductVersion = "phase12";
    private const string ManifestId = "phase12-npmrc-credential";
    private const string ConfigurePlanId = "phase12-npmrc-credential-plan";
    private const string ConfigureChangeSetId = "phase12-npmrc-credential-changeset";
    private const string NpmUserConfigEnvironmentVariable = "NPM_CONFIG_USERCONFIG";
    private const string LowercaseNpmUserConfigEnvironmentVariable = "npm_config_userconfig";
    private const string WorkspaceNpmrcFileName = ".npmrc";

    private readonly Func<string, string?> environmentVariableReader;
    private readonly IFileSystem fileSystem;
    private readonly string? workspaceDirectoryPath;
    private readonly string? userHomeDirectoryPath;
    private readonly string? userNpmrcPath;

    public NpmPhase12VerticalSliceService(NpmPhase12VerticalSliceOptions? options = null)
    {
        options ??= new NpmPhase12VerticalSliceOptions();
        fileSystem = options.FileSystem ?? new SystemFileSystem();
        environmentVariableReader =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        workspaceDirectoryPath = NormalizeOptionalPath(options.WorkspaceDirectoryPath);
        userHomeDirectoryPath = NormalizeOptionalPath(options.UserHomeDirectoryPath);
        userNpmrcPath = NormalizeOptionalPath(options.UserNpmrcPath);
    }

    public IReadOnlyList<NpmPhase12RegistryDeclaration> DiscoverRegistryDeclarations()
    {
        string? workspaceNpmrcPath = GetWorkspaceNpmrcPath();
        if (workspaceNpmrcPath is not null && fileSystem.FileExists(workspaceNpmrcPath))
        {
            NpmPhase12RegistryDeclaration[] workspaceDeclarations = ReadRegistryDeclarations(
                workspaceNpmrcPath
            );
            if (workspaceDeclarations.Length > 0)
            {
                return workspaceDeclarations;
            }
        }

        string resolvedUserNpmrcPath = ResolveUserNpmrcPath();
        return fileSystem.FileExists(resolvedUserNpmrcPath)
            ? ReadRegistryDeclarations(resolvedUserNpmrcPath)
            : [];
    }

    public ConfigurationChangePlan CreateUserCredentialPlan(
        NpmPhase12CredentialPlanRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AuthToken);
        if (request.AuthToken.Contains('\r', StringComparison.Ordinal)
            || request.AuthToken.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The npm auth token must not contain CR or LF.",
                nameof(request)
            );
        }

        if (request.Ecosystem is not CredentialEcosystem.Npm and not CredentialEcosystem.Pnpm)
        {
            throw new ArgumentException(
                "Phase 12 npmrc plans support only npm and pnpm ecosystems.",
                nameof(request)
            );
        }

        string targetNpmrcPath = fileSystem.GetFullPath(
            NullIfWhiteSpace(request.TargetNpmrcPath) ?? ResolveUserNpmrcPath()
        );
        string authTokenKey = request.Declaration.AuthSelectors.NpmAuthTokenKey;
        ConfigurationChange change = new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = ConfigurationTargetKind.Npmrc,
            TargetPathOrName = targetNpmrcPath,
            Key = authTokenKey,
            Value = request.AuthToken,
            IsSecretValue = true,
            RequiresOwnershipRecord = true,
        };

        return ConfigurationChangePlanPolicy.Create(
            ConfigurePlanId,
            ConfigureChangeSetId,
            ProductId,
            ConfigurationScope.User,
            new ConfigurationManifestMetadata
            {
                ManifestId = ManifestId,
                OwnerProductId = ProductId,
                EntrySelector = authTokenKey,
                ResourceIdentity = request.Declaration.ResourceIdentity,
                ProductVersion = ProductVersion,
                SafeMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ecosystem"] = ToContractEcosystemName(request.Ecosystem),
                    ["registry-key"] = request.Declaration.Key,
                },
            },
            [change],
            containsCredentialMaterial: true
        );
    }

    private NpmPhase12RegistryDeclaration[] ReadRegistryDeclarations(string npmrcPath)
    {
        string contents = fileSystem.ReadAllText(npmrcPath);
        var declarations = new List<NpmPhase12RegistryDeclaration>();
        foreach (string rawLine in SplitLines(contents))
        {
            string trimmedLine = rawLine.Trim();
            if (
                trimmedLine.Length == 0
                || trimmedLine.StartsWith('#')
                || trimmedLine.StartsWith(';')
            )
            {
                continue;
            }

            int separatorIndex = rawLine.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = rawLine[..separatorIndex].Trim();
            string value = rawLine[(separatorIndex + 1)..].Trim();
            if (!IsRegistryDeclarationKey(key))
            {
                continue;
            }

            if (TryCreateRegistryDeclaration(npmrcPath, key, value, out var declaration))
            {
                declarations.Add(declaration);
            }
        }

        return declarations.ToArray();
    }

    private bool TryCreateRegistryDeclaration(
        string sourcePath,
        string key,
        string value,
        [NotNullWhen(true)] out NpmPhase12RegistryDeclaration? declaration
    )
    {
        declaration = null;
        if (
            !Uri.TryCreate(value, UriKind.Absolute, out Uri? registryUrl)
            || !CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                registryUrl,
                CredentialEcosystem.Npm
            )
            || !TryCreateNpmResourceIdentity(registryUrl, out CanonicalResourceIdentity? resource)
        )
        {
            return false;
        }

        declaration = new NpmPhase12RegistryDeclaration
        {
            SourcePath = fileSystem.GetFullPath(sourcePath),
            Key = key,
            RegistryUrl = registryUrl,
            ResourceIdentity = resource,
            AuthSelectors = NpmCompatibleAuthSelectorPolicy.Create(resource),
        };
        return true;
    }

    private static bool TryCreateNpmResourceIdentity(
        Uri registryUrl,
        [NotNullWhen(true)] out CanonicalResourceIdentity? resource
    )
    {
        resource = null;
        string host = registryUrl.IdnHost;
        string[] segments = GetDecodedPathSegments(registryUrl);
        string? organization = null;
        string? project = null;
        string? feed = null;

        if (string.Equals(host, "pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            if (
                segments
                    is [var org, "_packaging", var feedName, "npm", "registry"]
            )
            {
                organization = org;
                feed = feedName;
            }

            if (
                segments
                    is [
                        var projectOrg,
                        var projectName,
                        "_packaging",
                        var projectFeedName,
                        "npm",
                        "registry",
                    ]
            )
            {
                organization = projectOrg;
                project = projectName;
                feed = projectFeedName;
            }
        }
        else if (string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            if (
                segments
                    is [var org, var projectName, "_packaging", var feedName, "npm", "registry"]
            )
            {
                organization = org;
                project = projectName;
                feed = feedName;
            }
        }
        else if (TryGetLegacyVisualStudioOrganization(host, out string? legacyOrganization))
        {
            organization = legacyOrganization;
            TryParseLegacyNpmSegments(host, segments, out project, out feed);
        }

        if (organization is null || feed is null)
        {
            return false;
        }

        try
        {
            resource = CanonicalResourceIdentity.Create(
                host,
                organization,
                registryUrl,
                project: project,
                feed: feed
            );
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseLegacyNpmSegments(
        string host,
        string[] segments,
        out string? project,
        out string? feed
    )
    {
        project = null;
        feed = null;
        if (host.EndsWith(".pkgs.visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            if (segments is ["_packaging", var feedName, "npm", "registry"])
            {
                feed = feedName;
                return true;
            }

            if (
                segments
                    is [
                        "DefaultCollection",
                        "_packaging",
                        var collectionFeedName,
                        "npm",
                        "registry",
                    ]
            )
            {
                feed = collectionFeedName;
                return true;
            }
        }
        else if (host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            if (
                segments
                    is [
                        "DefaultCollection",
                        var projectName,
                        "_packaging",
                        var feedName,
                        "npm",
                        "registry",
                    ]
            )
            {
                project = projectName;
                feed = feedName;
                return true;
            }
        }

        return false;
    }

    private string ResolveUserNpmrcPath()
    {
        string? configuredUserNpmrcPath =
            NullIfWhiteSpace(environmentVariableReader(NpmUserConfigEnvironmentVariable))
            ?? NullIfWhiteSpace(
                environmentVariableReader(LowercaseNpmUserConfigEnvironmentVariable)
            )
            ?? userNpmrcPath;
        if (configuredUserNpmrcPath is not null)
        {
            return fileSystem.GetFullPath(configuredUserNpmrcPath);
        }

        string home = userHomeDirectoryPath ?? GetHomeDirectory();
        return fileSystem.GetFullPath(Path.Combine(home, WorkspaceNpmrcFileName));
    }

    private string? GetWorkspaceNpmrcPath() =>
        workspaceDirectoryPath is null
            ? null
            : fileSystem.GetFullPath(Path.Combine(workspaceDirectoryPath, WorkspaceNpmrcFileName));

    private string GetHomeDirectory()
    {
        string? home = NullIfWhiteSpace(environmentVariableReader("HOME"));
        if (home is not null)
        {
            return fileSystem.GetFullPath(home);
        }

        string? userProfile = NullIfWhiteSpace(environmentVariableReader("USERPROFILE"));
        if (userProfile is not null)
        {
            return fileSystem.GetFullPath(userProfile);
        }

        throw new InvalidOperationException("User profile directory is unavailable.");
    }

    private static string[] SplitLines(string contents) =>
        contents.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static string[] GetDecodedPathSegments(Uri uri)
    {
        string path = uri.AbsolutePath.Trim('/');
        return path.Length == 0
            ? []
            : path.Split('/').Select(Uri.UnescapeDataString).ToArray();
    }

    private static bool IsRegistryDeclarationKey(string key) =>
        string.Equals(key, "registry", StringComparison.Ordinal)
        || key.EndsWith(":registry", StringComparison.Ordinal);

    private static bool TryGetLegacyVisualStudioOrganization(
        string host,
        out string? organization
    )
    {
        organization = null;
        const string packagingSuffix = ".pkgs.visualstudio.com";
        const string suffix = ".visualstudio.com";
        if (host.EndsWith(packagingSuffix, StringComparison.OrdinalIgnoreCase))
        {
            organization = host[..^packagingSuffix.Length];
            return organization.Length > 0 && !organization.Contains('.', StringComparison.Ordinal);
        }

        if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            organization = host[..^suffix.Length];
            return organization.Length > 0 && !organization.Contains('.', StringComparison.Ordinal);
        }

        return false;
    }

    private string? NormalizeOptionalPath(string? path) =>
        NullIfWhiteSpace(path) is { } value ? fileSystem.GetFullPath(value) : null;

    private static string ToContractEcosystemName(CredentialEcosystem ecosystem) =>
        ecosystem switch
        {
            CredentialEcosystem.Npm => "npm",
            CredentialEcosystem.Pnpm => "pnpm",
            _ => throw new ArgumentOutOfRangeException(nameof(ecosystem), ecosystem, null),
        };

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
