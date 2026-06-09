using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Hcoona.AzureAuth.CredProvider.Contracts;

public sealed record CanonicalResourceIdentity
{
    public required string AzureDevOpsHost { get; init; }
    public required string Organization { get; init; }
    public string? Project { get; init; }
    public string? Feed { get; init; }
    public string? Repository { get; init; }
    public required Uri ServiceEndpoint { get; init; }

    public static CanonicalResourceIdentity Create(
        string azureDevOpsHost,
        string organization,
        Uri serviceEndpoint,
        string? project = null,
        string? feed = null,
        string? repository = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(azureDevOpsHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ArgumentNullException.ThrowIfNull(serviceEndpoint);
        ThrowIfLeadingOrTrailingWhiteSpace(azureDevOpsHost, nameof(azureDevOpsHost));
        ThrowIfLeadingOrTrailingWhiteSpace(organization, nameof(organization));
        ThrowIfLeadingOrTrailingWhiteSpace(project, nameof(project));
        ThrowIfLeadingOrTrailingWhiteSpace(feed, nameof(feed));
        ThrowIfLeadingOrTrailingWhiteSpace(repository, nameof(repository));

        var resource = new CanonicalResourceIdentity
        {
            AzureDevOpsHost = azureDevOpsHost.Trim().ToLowerInvariant(),
            Organization = organization.Trim(),
            Project = NullIfWhiteSpace(project),
            Feed = NullIfWhiteSpace(feed),
            Repository = NullIfWhiteSpace(repository),
            ServiceEndpoint = serviceEndpoint,
        };

        CanonicalResourceIdentityPolicy.EnsureValid(resource);
        return resource;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ThrowIfLeadingOrTrailingWhiteSpace(string? value, string paramName)
    {
        if (value is not null && !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Protocol violation: canonical resource identity factory inputs must not include "
                    + "leading or trailing whitespace.",
                paramName
            );
        }
    }
}

public static class CanonicalResourceIdentityPolicy
{
    public static bool IsSupportedServiceEndpoint(Uri serviceEndpoint)
    {
        ArgumentNullException.ThrowIfNull(serviceEndpoint);
        return GetServiceEndpointViolation(serviceEndpoint) is null;
    }

    public static string? GetServiceEndpointViolation(Uri serviceEndpoint)
    {
        ArgumentNullException.ThrowIfNull(serviceEndpoint);

        ServiceEndpointShape endpointShape;
        try
        {
            endpointShape = ParseSupportedServiceEndpoint(serviceEndpoint);
        }
        catch (UriFormatException)
        {
            return "Protocol violation: service endpoint must be a well-formed supported Azure "
                + "DevOps service URI.";
        }

        return endpointShape.Violation;
    }

    public static bool IsServiceEndpointCompatibleWithEcosystem(
        Uri serviceEndpoint,
        CredentialEcosystem ecosystem
    )
    {
        ArgumentNullException.ThrowIfNull(serviceEndpoint);

        ServiceEndpointShape endpointShape;
        try
        {
            endpointShape = ParseSupportedServiceEndpoint(serviceEndpoint);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (endpointShape.Violation is not null)
        {
            return false;
        }

        return ecosystem switch
        {
            CredentialEcosystem.Git => IsAzureReposGitHost(serviceEndpoint.IdnHost)
                && endpointShape.Components?.FeedKind == FeedEndpointKind.None,
            CredentialEcosystem.NuGet => endpointShape.Components?.FeedKind
                == FeedEndpointKind.NuGet,
            CredentialEcosystem.Python => endpointShape.Components?.FeedKind
                == FeedEndpointKind.Python,
            CredentialEcosystem.Npm or CredentialEcosystem.Pnpm or CredentialEcosystem.Yarn =>
                endpointShape.Components?.FeedKind == FeedEndpointKind.Npm,
            _ => false,
        };
    }

    public static void EnsureValid(CanonicalResourceIdentity resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        string? violation = GetViolation(resource);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(resource));
        }
    }

    public static bool IsValid(CanonicalResourceIdentity resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return GetViolation(resource) is null;
    }

    public static string? GetViolation(CanonicalResourceIdentity resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (string.IsNullOrWhiteSpace(resource.AzureDevOpsHost))
        {
            return "Protocol violation: Azure DevOps host is required.";
        }

        if (string.IsNullOrWhiteSpace(resource.Organization))
        {
            return "Protocol violation: Azure DevOps organization is required.";
        }

        if (
            HasLeadingOrTrailingWhiteSpace(resource.AzureDevOpsHost)
            || HasLeadingOrTrailingWhiteSpace(resource.Organization)
            || HasLeadingOrTrailingWhiteSpace(resource.Project)
            || HasLeadingOrTrailingWhiteSpace(resource.Feed)
            || HasLeadingOrTrailingWhiteSpace(resource.Repository)
        )
        {
            return "Protocol violation: canonical resource identity components must not include "
                + "leading or trailing whitespace.";
        }

        if (
            IsReservedIdentityComponent(resource.Organization)
            || IsReservedIdentityComponent(resource.Project)
            || IsReservedIdentityComponent(resource.Feed)
            || IsReservedIdentityComponent(resource.Repository)
        )
        {
            return "Protocol violation: canonical identity components must not use reserved "
                + "resource marker names.";
        }

        ServiceEndpointShape endpointShape;
        try
        {
            endpointShape = ParseSupportedServiceEndpoint(resource.ServiceEndpoint);
        }
        catch (UriFormatException)
        {
            return "Protocol violation: service endpoint must be a well-formed supported Azure "
                + "DevOps service URI.";
        }

        if (endpointShape.Violation is not null)
        {
            return endpointShape.Violation;
        }

        if (
            !string.Equals(
                resource.ServiceEndpoint.IdnHost,
                resource.AzureDevOpsHost,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return "Protocol violation: service endpoint host must match the canonical Azure "
                + "DevOps host.";
        }

        if (
            !string.Equals(
                endpointShape.Organization,
                resource.Organization,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return "Protocol violation: service endpoint organization must match the canonical "
                + "organization.";
        }

        if (!MatchesCanonicalComponent(endpointShape.Components?.Project, resource.Project))
        {
            return "Protocol violation: service endpoint project must match the canonical project.";
        }

        if (!MatchesCanonicalComponent(endpointShape.Components?.Feed, resource.Feed))
        {
            return "Protocol violation: service endpoint feed must match the canonical feed.";
        }

        if (!MatchesCanonicalComponent(endpointShape.Components?.Repository, resource.Repository))
        {
            return "Protocol violation: service endpoint repository must match the canonical "
                + "repository.";
        }

        return null;
    }

    private static ServiceEndpointShape ParseSupportedServiceEndpoint(Uri? serviceEndpoint)
    {
        if (serviceEndpoint is null || !serviceEndpoint.IsAbsoluteUri)
        {
            return new("Protocol violation: service endpoint must be absolute.", null, null);
        }

        if (
            !string.Equals(
                serviceEndpoint.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return new("Protocol violation: service endpoint must use HTTPS.", null, null);
        }

        if (!serviceEndpoint.IsDefaultPort)
        {
            return new(
                "Protocol violation: service endpoint must use the default HTTPS port.",
                null,
                null
            );
        }

        if (
            UriSecurityPolicy.HasUserInfoDelimiter(serviceEndpoint)
            || !string.IsNullOrEmpty(serviceEndpoint.Query)
            || !string.IsNullOrEmpty(serviceEndpoint.Fragment)
            || serviceEndpoint.AbsoluteUri.Contains('?', StringComparison.Ordinal)
            || serviceEndpoint.AbsoluteUri.Contains('#', StringComparison.Ordinal)
        )
        {
            return new(
                "Protocol violation: service endpoint must not include user info, query, or "
                    + "fragment.",
                null,
                null
            );
        }

        if (!IsSupportedAzureDevOpsHost(serviceEndpoint.IdnHost))
        {
            return new(
                "Protocol violation: service endpoint host must be a supported Azure DevOps or "
                    + "Azure Artifacts host.",
                null,
                null
            );
        }

        string[] endpointPathSegments = GetPathSegments(serviceEndpoint);
        string? legacyHostOrganization = TryGetLegacyVisualStudioOrganization(
            serviceEndpoint.IdnHost
        );
        string? endpointOrganization =
            legacyHostOrganization
            ?? (endpointPathSegments.Length == 0 ? null : endpointPathSegments[0]);
        if (string.IsNullOrWhiteSpace(endpointOrganization))
        {
            return new(
                "Protocol violation: service endpoint organization is required.",
                null,
                null
            );
        }

        if (IsReservedIdentityComponent(endpointOrganization))
        {
            return new(
                "Protocol violation: service endpoint identity components must not use reserved "
                    + "resource marker names.",
                null,
                null
            );
        }

        EndpointPathComponents? endpointComponents = ParseSupportedEndpointPath(
            endpointPathSegments,
            legacyHostOrganization is not null,
            IsAzureReposGitHost(serviceEndpoint.IdnHost)
        );
        if (endpointComponents is null)
        {
            return new(
                "Protocol violation: service endpoint path must use a supported Azure DevOps "
                    + "resource shape.",
                null,
                null
            );
        }

        return new(null, endpointOrganization, endpointComponents);
    }

    private static string[] GetPathSegments(Uri uri)
    {
        string path = uri.AbsolutePath.StartsWith('/') ? uri.AbsolutePath[1..] : uri.AbsolutePath;
        return path.Length == 0
            ? []
            : path.Split('/', StringSplitOptions.None).Select(DecodePathSegmentOrThrow).ToArray();
    }

    private static string DecodePathSegmentOrThrow(string segment)
    {
        string decoded = Uri.UnescapeDataString(segment);
        if (
            decoded.Contains('/', StringComparison.Ordinal)
            || decoded.Contains('\\', StringComparison.Ordinal)
        )
        {
            throw new UriFormatException(
                "Path segment must not contain encoded or decoded path separators."
            );
        }

        return decoded;
    }

    private static EndpointPathComponents? ParseSupportedEndpointPath(
        string[] segments,
        bool legacyOrganizationInHost,
        bool allowGitEndpointPath
    )
    {
        if (legacyOrganizationInHost)
        {
            string[] resourceSegments =
                segments.Length > 0 && IsSegment(segments[0], "DefaultCollection")
                    ? segments[1..]
                    : segments;

            if (resourceSegments.Length == 0)
            {
                return new EndpointPathComponents(Project: null, Feed: null, Repository: null);
            }

            if (
                allowGitEndpointPath
                && resourceSegments.Length == 3
                && IsSegment(resourceSegments[1], "_git")
            )
            {
                if (!HasRequiredEndpointPathComponents(resourceSegments[0], resourceSegments[2]))
                {
                    return null;
                }

                return new EndpointPathComponents(
                    Project: resourceSegments[0],
                    Feed: null,
                    Repository: resourceSegments[2]
                );
            }

            if (
                resourceSegments.Length >= 3
                && IsSegment(resourceSegments[1], "_packaging")
                && TryGetSupportedFeedEndpointKind(
                    resourceSegments,
                    3,
                    out FeedEndpointKind feedEndpointKind
                )
            )
            {
                if (!HasRequiredEndpointPathComponents(resourceSegments[0], resourceSegments[2]))
                {
                    return null;
                }

                return new EndpointPathComponents(
                    Project: resourceSegments[0],
                    Feed: resourceSegments[2],
                    Repository: null,
                    FeedKind: feedEndpointKind
                );
            }

            if (
                resourceSegments.Length >= 2
                && IsSegment(resourceSegments[0], "_packaging")
                && TryGetSupportedFeedEndpointKind(resourceSegments, 2, out feedEndpointKind)
            )
            {
                if (!HasRequiredEndpointPathComponents(resourceSegments[1]))
                {
                    return null;
                }

                return new EndpointPathComponents(
                    Project: null,
                    Feed: resourceSegments[1],
                    Repository: null,
                    FeedKind: feedEndpointKind
                );
            }

            return null;
        }

        if (segments.Length == 1)
        {
            return new EndpointPathComponents(Project: null, Feed: null, Repository: null);
        }

        if (allowGitEndpointPath && segments.Length == 4 && IsSegment(segments[2], "_git"))
        {
            if (!HasRequiredEndpointPathComponents(segments[1], segments[3]))
            {
                return null;
            }

            return new EndpointPathComponents(
                Project: segments[1],
                Feed: null,
                Repository: segments[3]
            );
        }

        if (
            segments.Length >= 4
            && IsSegment(segments[2], "_packaging")
            && TryGetSupportedFeedEndpointKind(segments, 4, out FeedEndpointKind feedKind)
        )
        {
            if (!HasRequiredEndpointPathComponents(segments[1], segments[3]))
            {
                return null;
            }

            return new EndpointPathComponents(
                Project: segments[1],
                Feed: segments[3],
                Repository: null,
                FeedKind: feedKind
            );
        }

        if (
            segments.Length >= 3
            && IsSegment(segments[1], "_packaging")
            && TryGetSupportedFeedEndpointKind(segments, 3, out feedKind)
        )
        {
            if (!HasRequiredEndpointPathComponents(segments[2]))
            {
                return null;
            }

            return new EndpointPathComponents(
                Project: null,
                Feed: segments[2],
                Repository: null,
                FeedKind: feedKind
            );
        }

        return null;
    }

    private static bool HasRequiredEndpointPathComponents(params string[] components)
    {
        foreach (string component in components)
        {
            if (string.IsNullOrWhiteSpace(component))
            {
                return false;
            }

            if (IsReservedIdentityComponent(component))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetSupportedFeedEndpointKind(
        string[] segments,
        int suffixStart,
        out FeedEndpointKind feedKind
    )
    {
        feedKind = FeedEndpointKind.None;
        int suffixLength = segments.Length - suffixStart;
        if (suffixLength > 0 && segments[^1].Length == 0)
        {
            int suffixLengthWithoutTerminalSlash = suffixLength - 1;
            if (
                !TryGetSupportedTerminalSlashFeedEndpointKind(
                    segments,
                    suffixStart,
                    suffixLengthWithoutTerminalSlash,
                    out feedKind
                )
            )
            {
                return false;
            }

            return true;
        }

        feedKind = suffixLength switch
        {
            0 => FeedEndpointKind.FeedRoot,
            1 when IsSegment(segments[suffixStart], "npm") => FeedEndpointKind.Npm,
            2
                when IsSegment(segments[suffixStart], "npm")
                    && IsSegment(segments[suffixStart + 1], "registry") => FeedEndpointKind.Npm,
            2
                when IsSegment(segments[suffixStart], "pypi")
                    && IsSegment(segments[suffixStart + 1], "simple") => FeedEndpointKind.Python,
            3
                when IsSegment(segments[suffixStart], "nuget")
                    && IsSegment(segments[suffixStart + 1], "v3")
                    && IsSegment(segments[suffixStart + 2], "index.json") => FeedEndpointKind.NuGet,
            _ => FeedEndpointKind.Unsupported,
        };

        return feedKind != FeedEndpointKind.Unsupported;
    }

    private static bool TryGetSupportedTerminalSlashFeedEndpointKind(
        string[] segments,
        int suffixStart,
        int suffixLength,
        out FeedEndpointKind feedKind
    )
    {
        feedKind = suffixLength switch
        {
            1 when IsSegment(segments[suffixStart], "npm") => FeedEndpointKind.Npm,
            2
                when IsSegment(segments[suffixStart], "npm")
                    && IsSegment(segments[suffixStart + 1], "registry") => FeedEndpointKind.Npm,
            2
                when IsSegment(segments[suffixStart], "pypi")
                    && IsSegment(segments[suffixStart + 1], "simple") => FeedEndpointKind.Python,
            _ => FeedEndpointKind.Unsupported,
        };

        return feedKind != FeedEndpointKind.Unsupported;
    }

    private static bool MatchesCanonicalComponent(
        string? endpointComponent,
        string? canonicalComponent
    ) =>
        endpointComponent is null
            ? string.IsNullOrWhiteSpace(canonicalComponent)
            : !string.IsNullOrWhiteSpace(canonicalComponent)
                && string.Equals(
                    endpointComponent,
                    canonicalComponent,
                    StringComparison.OrdinalIgnoreCase
                );

    private static bool HasLeadingOrTrailingWhiteSpace(string? value) =>
        value is not null && !string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static bool IsSupportedAzureDevOpsHost(string host)
    {
        if (
            string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        return TryGetLegacyVisualStudioOrganization(host) is not null;
    }

    private static bool IsSegment(string segment, string expected) =>
        string.Equals(segment, expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsReservedIdentityComponent(string? component) =>
        string.Equals(component, "_git", StringComparison.OrdinalIgnoreCase)
        || string.Equals(component, "_packaging", StringComparison.OrdinalIgnoreCase);

    private static bool IsAzureReposGitHost(string host) =>
        string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
        || (
            host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".pkgs.visualstudio.com", StringComparison.OrdinalIgnoreCase)
        );

    private static string? TryGetLegacyVisualStudioOrganization(string host)
    {
        const string packagingSuffix = ".pkgs.visualstudio.com";
        const string suffix = ".visualstudio.com";

        if (
            host.EndsWith(packagingSuffix, StringComparison.OrdinalIgnoreCase)
            && host.Length > packagingSuffix.Length
        )
        {
            string organization = host[..^packagingSuffix.Length];
            return IsLegacyOrganizationLabel(organization) ? organization : null;
        }

        if (
            host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && host.Length > suffix.Length
        )
        {
            string organization = host[..^suffix.Length];
            return IsLegacyOrganizationLabel(organization) ? organization : null;
        }

        return null;
    }

    private static bool IsLegacyOrganizationLabel(string organization) =>
        !string.IsNullOrWhiteSpace(organization)
        && !organization.Contains('.', StringComparison.Ordinal);

    private enum FeedEndpointKind
    {
        None,
        FeedRoot,
        NuGet,
        Python,
        Npm,
        Unsupported,
    }

    private sealed record EndpointPathComponents(
        string? Project,
        string? Feed,
        string? Repository,
        FeedEndpointKind FeedKind = FeedEndpointKind.None
    );

    private sealed record ServiceEndpointShape(
        string? Violation,
        string? Organization,
        EndpointPathComponents? Components
    );
}

public sealed record NpmCompatibleAuthSelectors
{
    public required string NpmAuthTokenKey { get; init; }
    public required string YarnAuthTokenKey { get; init; }
    public required string YarnAlwaysAuthKey { get; init; }
}

public static class NpmCompatibleAuthSelectorPolicy
{
    public static NpmCompatibleAuthSelectors Create(CanonicalResourceIdentity resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        CanonicalResourceIdentityPolicy.EnsureValid(resource);

        if (
            !CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                resource.ServiceEndpoint,
                CredentialEcosystem.Npm
            )
        )
        {
            throw new ArgumentException(
                "Protocol violation: npm-compatible auth selectors require an npm registry service "
                    + "endpoint.",
                nameof(resource)
            );
        }

        string registryUrl = GetRegistryUrl(resource.ServiceEndpoint);
        string npmSelector = registryUrl["https:".Length..];

        return new NpmCompatibleAuthSelectors
        {
            NpmAuthTokenKey = $"{npmSelector}/:_authToken",
            YarnAuthTokenKey = $"""npmRegistries["{registryUrl}"].npmAuthToken""",
            YarnAlwaysAuthKey = $"""npmRegistries["{registryUrl}"].npmAlwaysAuth""",
        };
    }

    private static string GetRegistryUrl(Uri serviceEndpoint)
    {
        string path = serviceEndpoint.AbsolutePath;
        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path[..^1];
        }

        return $"https://{serviceEndpoint.IdnHost}{path}";
    }
}

internal static class UriSecurityPolicy
{
    public static bool HasUserInfoDelimiter(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return true;
        }

        string absoluteUri = uri.AbsoluteUri;
        int authorityStart = absoluteUri.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
        {
            return false;
        }

        authorityStart += 3;
        int authorityEnd = absoluteUri.IndexOfAny(['/', '?', '#'], authorityStart);
        ReadOnlySpan<char> authority =
            authorityEnd < 0
                ? absoluteUri.AsSpan(authorityStart)
                : absoluteUri.AsSpan(authorityStart, authorityEnd - authorityStart);
        return authority.Contains('@');
    }
}

public sealed record CredentialRequest : IJsonOnDeserialized
{
    [JsonRequired]
    public int ContractMajor { get; init; } = ContractVersions.CredentialContractMajor;
    public required CredentialEcosystem Ecosystem { get; init; }
    public required CredentialOperation Operation { get; init; }
    public required CanonicalResourceIdentity Resource { get; init; }

    [JsonRequired]
    public required string ServiceIdentity { get; init; }
    public string? AccountHint { get; init; }
    public string? TenantHint { get; init; }
    public required TokenAudience RequestedAudience { get; init; }
    public required CredentialKind CredentialKind { get; init; }
    public required IdentityFlow IdentityFlow { get; init; }
    public required InteractivePolicy InteractivePolicy { get; init; }
    public required CachePolicyMode CachePolicy { get; init; }
    public CiContext? CiContext { get; init; }
    public IReadOnlyDictionary<string, string> ExtensionData { get; init; } =
        ContractMetadata.Empty;

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (ContractMajor != ContractVersions.CredentialContractMajor)
        {
            throw new ArgumentException(
                "Protocol violation: credential request contract major must be 1.",
                nameof(ContractMajor)
            );
        }
    }
}

public sealed record CiContext
{
    public bool ExplicitCiMode { get; init; }
    public string? Provider { get; init; }
    public bool HasAzurePipelinesSystemAccessToken { get; init; }
    public bool AllowsPersistentWrites { get; init; }
}

public static class CiProviderNames
{
    public const string AzurePipelines = "AzurePipelines";
}

public sealed record CredentialResult : IJsonOnDeserialized
{
    [JsonRequired]
    public int ContractMajor { get; init; } = ContractVersions.CredentialContractMajor;
    public required CredentialResultStatus Status { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? BearerToken { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? Account { get; init; }
    public string? Tenant { get; init; }
    public CacheKey? CacheKey { get; init; }
    public required string DiagnosticsCorrelationId { get; init; }
    public CredentialError? Error { get; init; }
    public IReadOnlyDictionary<string, string> ExtensionData { get; init; } =
        ContractMetadata.Empty;

    public bool ContainsCredentialMaterial => Password is not null || BearerToken is not null;

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {{ {1} = {2}, {3} = {4}, {5} = {6}, {7} = <redacted>, "
                + "{8} = <redacted>, {9} = {10}, {11} = {12}, {13} = {14}, "
                + "{15} = {16}, {17} = {18}, {19} = {20}, {21} = {22} safe entries }}",
            nameof(CredentialResult),
            nameof(ContractMajor),
            ContractMajor,
            nameof(Status),
            Status,
            nameof(Username),
            Username,
            nameof(Password),
            nameof(BearerToken),
            nameof(ExpiresAt),
            ExpiresAt,
            nameof(Account),
            Account,
            nameof(Tenant),
            Tenant,
            nameof(CacheKey),
            CacheKey,
            nameof(DiagnosticsCorrelationId),
            DiagnosticsCorrelationId,
            nameof(Error),
            Error,
            nameof(ExtensionData),
            ExtensionData.Count
        );

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (ContractMajor != ContractVersions.CredentialContractMajor)
        {
            throw new ArgumentException(
                "Protocol violation: credential result contract major must be 1.",
                nameof(ContractMajor)
            );
        }
    }
}

public sealed record CredentialError
{
    public required CredentialErrorKind Kind { get; init; }
    public required string Code { get; init; }
    public required string SafeMessage { get; init; }
    public IReadOnlyDictionary<string, string> SafeDetails { get; init; } = ContractMetadata.Empty;
}

public sealed record CacheKey : IJsonOnDeserialized
{
    [JsonRequired]
    public int SchemaMajor { get; init; } = ContractVersions.CacheKeySchemaMajor;
    public required string Value { get; init; }

    void IJsonOnDeserialized.OnDeserialized() => CacheKeySchema.EnsureValid(this);
}

public static class CacheKeySchema
{
    private const int ExpectedCacheKeyPartCount = 12;
    private const int HostPartIndex = 2;
    private const int OrganizationPartIndex = 3;
    private const int ProjectPartIndex = 4;
    private const int FeedPartIndex = 5;
    private const int RepositoryPartIndex = 6;
    private const int ServiceIdentityPartIndex = 7;
    private const int AccountPartIndex = 8;
    private const int TenantPartIndex = 9;
    private const int EcosystemPartIndex = 1;
    private const int AudiencePartIndex = 10;
    private const int CredentialKindPartIndex = 11;

    public static CacheKey Create(CredentialRequest request, string account, string tenant)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        if (!IdentityFlowPolicy.IsAcceptedMvpRequest(request))
        {
            throw new ArgumentException(
                "Protocol violation: credential request must be accepted before cache-key "
                    + "creation.",
                nameof(request)
            );
        }

        string? cacheProject =
            request.Ecosystem == CredentialEcosystem.Git ? null : request.Resource.Project;
        string? cacheRepository =
            request.Ecosystem == CredentialEcosystem.Git ? null : request.Resource.Repository;

        var parts = new[]
        {
            ContractVersions.CacheKeySchemaPrefix,
            Normalize(request.Ecosystem.ToString()),
            Normalize(request.Resource.AzureDevOpsHost),
            Normalize(request.Resource.Organization),
            Normalize(cacheProject),
            Normalize(request.Resource.Feed),
            Normalize(cacheRepository),
            Normalize(request.ServiceIdentity),
            Normalize(account),
            Normalize(tenant),
            Normalize(request.RequestedAudience.ToString()),
            Normalize(request.CredentialKind.ToString()),
        };

        var cacheKey = new CacheKey { Value = string.Join('|', parts) };
        EnsureValid(cacheKey);
        return cacheKey;
    }

    public static void EnsureValid(CacheKey cacheKey)
    {
        ArgumentNullException.ThrowIfNull(cacheKey);

        string? violation = GetViolation(cacheKey);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(cacheKey));
        }
    }

    public static bool IsValid(CacheKey cacheKey)
    {
        ArgumentNullException.ThrowIfNull(cacheKey);
        return GetViolation(cacheKey) is null;
    }

    public static CredentialKind GetCredentialKind(CacheKey cacheKey)
    {
        EnsureValid(cacheKey);
        string credentialKind = DecodeRequiredPartitionComponent(
            cacheKey.Value.Split('|')[CredentialKindPartIndex]
        );
        return credentialKind switch
        {
            "basicpassword" => CredentialKind.BasicPassword,
            "bearertoken" => CredentialKind.BearerToken,
            "npmauthtoken" => CredentialKind.NpmAuthToken,
            "nugetplugincredential" => CredentialKind.NuGetPluginCredential,
            "patcompatibility" => CredentialKind.PatCompatibility,
            _ => CredentialKind.Unspecified,
        };
    }

    internal static CacheKeyProtocolShape GetProtocolShape(CacheKey cacheKey)
    {
        EnsureValid(cacheKey);
        string[] parts = cacheKey.Value.Split('|');
        return new CacheKeyProtocolShape(
            ToEcosystem(DecodeRequiredPartitionComponent(parts[EcosystemPartIndex])),
            ToAudience(DecodeRequiredPartitionComponent(parts[AudiencePartIndex])),
            ToCredentialKind(DecodeRequiredPartitionComponent(parts[CredentialKindPartIndex])),
            HasOptionalPartitionComponent(parts[ProjectPartIndex]),
            HasOptionalPartitionComponent(parts[FeedPartIndex]),
            HasOptionalPartitionComponent(parts[RepositoryPartIndex])
        );
    }

    public static string? GetViolation(CacheKey cacheKey)
    {
        ArgumentNullException.ThrowIfNull(cacheKey);

        if (cacheKey.SchemaMajor != ContractVersions.CacheKeySchemaMajor)
        {
            return "Protocol violation: cache-key schema major must be 1.";
        }

        if (string.IsNullOrWhiteSpace(cacheKey.Value))
        {
            return "Protocol violation: cache-key value is required.";
        }

        string[] parts = cacheKey.Value.Split('|');
        if (parts.Length != ExpectedCacheKeyPartCount)
        {
            return "Protocol violation: cache-key value must contain the frozen v1 partition "
                + "shape.";
        }

        if (
            !string.Equals(
                parts[0],
                ContractVersions.CacheKeySchemaPrefix,
                StringComparison.Ordinal
            )
        )
        {
            return "Protocol violation: cache-key value must use the supported schema prefix.";
        }

        for (int i = 1; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length == 0)
            {
                return "Protocol violation: cache-key partition components must not be empty.";
            }

            if (string.Equals(part, "-", StringComparison.Ordinal))
            {
                if (!IsOptionalPartitionIndex(i))
                {
                    return "Protocol violation: cache-key required partition components must be "
                        + "encoded.";
                }

                continue;
            }

            if (
                !TryDecodePartitionComponent(part, out string decoded)
                || string.IsNullOrWhiteSpace(decoded)
            )
            {
                return "Protocol violation: cache-key partition components must be valid base64 or "
                    + "base64url.";
            }
        }

        if (
            !HasCanonicalDecodedPartitionValue(parts[EcosystemPartIndex])
            || !HasCanonicalDecodedPartitionValue(parts[HostPartIndex])
            || !HasCanonicalDecodedPartitionValue(parts[OrganizationPartIndex])
            || !HasCanonicalDecodedPartitionValue(parts[ProjectPartIndex])
            || !HasCanonicalDecodedPartitionValue(parts[FeedPartIndex])
            || !HasCanonicalDecodedPartitionValue(parts[RepositoryPartIndex])
            || !HasCanonicalDecodedPartitionValue(parts[ServiceIdentityPartIndex])
            || !HasCanonicalDecodedPartitionValue(parts[AccountPartIndex])
            || !HasCanonicalDecodedPartitionValue(parts[TenantPartIndex])
            || !HasCanonicalDecodedPartitionValue(parts[AudiencePartIndex])
            || !HasCanonicalDecodedPartitionValue(parts[CredentialKindPartIndex])
        )
        {
            return "Protocol violation: cache-key partition components must use the frozen "
                + "canonical lower-case form.";
        }

        string ecosystem = DecodeRequiredPartitionComponent(parts[EcosystemPartIndex]);
        string host = DecodeRequiredPartitionComponent(parts[HostPartIndex]);
        string organization = DecodeRequiredPartitionComponent(parts[OrganizationPartIndex]);
        string? project = DecodeOptionalPartitionComponent(parts[ProjectPartIndex]);
        string? feed = DecodeOptionalPartitionComponent(parts[FeedPartIndex]);
        string? repository = DecodeOptionalPartitionComponent(parts[RepositoryPartIndex]);
        string audience = DecodeRequiredPartitionComponent(parts[AudiencePartIndex]);
        string credentialKind = DecodeRequiredPartitionComponent(parts[CredentialKindPartIndex]);

        if (
            !IsSupportedEcosystem(ecosystem)
            || !IsSupportedAudience(audience)
            || !IsSupportedCredentialKind(credentialKind)
        )
        {
            return "Protocol violation: cache-key enum partition components must use supported v1 "
                + "values.";
        }

        if (
            !IsSupportedCacheKeyAzureDevOpsHost(host, ecosystem)
            || !CacheKeyHostMatchesOrganization(host, organization)
            || IsReservedIdentityComponent(organization)
            || IsReservedIdentityComponent(project)
            || IsReservedIdentityComponent(feed)
            || IsReservedIdentityComponent(repository)
        )
        {
            return "Protocol violation: cache-key resource identity partitions must match "
                + "supported canonical resource rules.";
        }

        if (
            ContainsPathSeparator(organization)
            || ContainsPathSeparator(project)
            || ContainsPathSeparator(feed)
            || ContainsPathSeparator(repository)
        )
        {
            return "Protocol violation: cache-key resource identity partitions must not contain "
                + "path separators.";
        }

        if (
            !IsCacheKeyResourceShapeAllowed(
                ecosystem,
                project,
                feed,
                repository,
                audience,
                credentialKind
            )
        )
        {
            return "Protocol violation: cache-key resource partitions must match the frozen "
                + "ecosystem resource shape.";
        }

        if (
            !ServiceIdentityContract.IsCanonical(
                DecodeRequiredPartitionComponent(parts[ServiceIdentityPartIndex])
            )
        )
        {
            return "Protocol violation: cache-key service identity partition must use canonical "
                + "lower-case form.";
        }

        return null;
    }

    private static bool IsOptionalPartitionIndex(int index) =>
        index is ProjectPartIndex or FeedPartIndex or RepositoryPartIndex;

    private static string DecodeRequiredPartitionComponent(string value)
    {
        _ = TryDecodePartitionComponent(value, out string decoded);
        return decoded;
    }

    private static string? DecodeOptionalPartitionComponent(string value) =>
        string.Equals(value, "-", StringComparison.Ordinal)
            ? null
            : DecodeRequiredPartitionComponent(value);

    private static bool HasOptionalPartitionComponent(string value) =>
        !string.Equals(value, "-", StringComparison.Ordinal);

    private static bool HasCanonicalDecodedPartitionValue(string value)
    {
        if (string.Equals(value, "-", StringComparison.Ordinal))
        {
            return true;
        }

        string decoded = DecodeRequiredPartitionComponent(value);
        return string.Equals(value, EncodePartitionComponent(decoded), StringComparison.Ordinal)
            && string.Equals(decoded, decoded.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    private static bool IsCacheKeyResourceShapeAllowed(
        string ecosystem,
        string? project,
        string? feed,
        string? repository,
        string audience,
        string credentialKind
    ) =>
        ecosystem switch
        {
            "git" => IsGitCacheKeyShape(project, feed, repository)
                && string.Equals(audience, "azuredevops", StringComparison.Ordinal)
                && credentialKind is "basicpassword" or "bearertoken" or "patcompatibility",
            "nuget" => IsPackageCacheKeyShape(
                feed,
                repository,
                audience,
                credentialKind,
                "nugetplugincredential"
            ),
            "python" => IsPackageCacheKeyShape(
                feed,
                repository,
                audience,
                credentialKind,
                "basicpassword"
            ),
            "npm" or "pnpm" or "yarn" => IsPackageCacheKeyShape(
                feed,
                repository,
                audience,
                credentialKind,
                "npmauthtoken"
            ),
            _ => false,
        };

    private static bool IsGitCacheKeyShape(string? project, string? feed, string? repository) =>
        string.IsNullOrWhiteSpace(feed)
        && string.IsNullOrWhiteSpace(project)
        && string.IsNullOrWhiteSpace(repository);

    private static bool IsPackageCacheKeyShape(
        string? feed,
        string? repository,
        string audience,
        string credentialKind,
        string expectedCredentialKind
    ) =>
        !string.IsNullOrWhiteSpace(feed)
        && string.IsNullOrWhiteSpace(repository)
        && string.Equals(audience, "azureartifacts", StringComparison.Ordinal)
        && string.Equals(credentialKind, expectedCredentialKind, StringComparison.Ordinal);

    private static bool IsSupportedCacheKeyAzureDevOpsHost(string host, string ecosystem) =>
        string.Equals(ecosystem, "git", StringComparison.Ordinal)
            ? IsAzureReposCacheKeyHost(host)
            : IsAzureReposOrArtifactsCacheKeyHost(host);

    private static bool IsAzureReposCacheKeyHost(string host) =>
        string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
        || (
            host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".pkgs.visualstudio.com", StringComparison.OrdinalIgnoreCase)
            && TryGetLegacyVisualStudioCacheKeyOrganization(host) is not null
        );

    private static bool IsAzureReposOrArtifactsCacheKeyHost(string host) =>
        string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase)
        || TryGetLegacyVisualStudioCacheKeyOrganization(host) is not null;

    private static bool CacheKeyHostMatchesOrganization(string host, string organization)
    {
        string? legacyOrganization = TryGetLegacyVisualStudioCacheKeyOrganization(host);
        return legacyOrganization is null
            || string.Equals(legacyOrganization, organization, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetLegacyVisualStudioCacheKeyOrganization(string host)
    {
        const string packagingSuffix = ".pkgs.visualstudio.com";
        const string suffix = ".visualstudio.com";

        if (
            host.EndsWith(packagingSuffix, StringComparison.OrdinalIgnoreCase)
            && host.Length > packagingSuffix.Length
        )
        {
            string organization = host[..^packagingSuffix.Length];
            return IsLegacyCacheKeyOrganizationLabel(organization) ? organization : null;
        }

        if (
            host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && host.Length > suffix.Length
        )
        {
            string organization = host[..^suffix.Length];
            return IsLegacyCacheKeyOrganizationLabel(organization) ? organization : null;
        }

        return null;
    }

    private static bool IsLegacyCacheKeyOrganizationLabel(string organization) =>
        !string.IsNullOrWhiteSpace(organization)
        && !organization.Contains('.', StringComparison.Ordinal);

    private static bool IsReservedIdentityComponent(string? component) =>
        string.Equals(component, "_git", StringComparison.OrdinalIgnoreCase)
        || string.Equals(component, "_packaging", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsPathSeparator(string? component) =>
        component?.Contains('/', StringComparison.Ordinal) == true
        || component?.Contains('\\', StringComparison.Ordinal) == true;

    private static bool TryDecodePartitionComponent(string value, out string decoded)
    {
        decoded = string.Empty;
        if (value.Length == 0 || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (TryDecodeBase64(value, out decoded))
        {
            return true;
        }

        string base64Url = ToPaddedBase64Url(value);
        return base64Url.Length > 0 && TryDecodeBase64(base64Url, out decoded);
    }

    private static string ToPaddedBase64Url(string value)
    {
        if (!HasValidBase64UrlCharacters(value) || value.Length % 4 == 1)
        {
            return string.Empty;
        }

        string base64 = value.Replace('-', '+').Replace('_', '/');
        return base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
    }

    private static bool TryDecodeBase64(string value, out string decoded)
    {
        decoded = string.Empty;
        if (value.Length == 0 || value.Length % 4 != 0 || !HasValidBase64Characters(value))
        {
            return false;
        }

        try
        {
            decoded = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true
            ).GetString(Convert.FromBase64String(value));
            return true;
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool HasValidBase64Characters(string value)
    {
        int firstPadding = value.IndexOf('=', StringComparison.Ordinal);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool isPadding = c == '=';
            if (
                !(
                    (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c is '+' or '/'
                    || isPadding
                )
            )
            {
                return false;
            }

            if (isPadding && i < value.Length - 2)
            {
                return false;
            }

            if (!isPadding && firstPadding >= 0 && i > firstPadding)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasValidBase64UrlCharacters(string value) =>
        value.All(c =>
            (c >= 'A' && c <= 'Z')
            || (c >= 'a' && c <= 'z')
            || (c >= '0' && c <= '9')
            || c is '-' or '_' or '='
        );

    private static bool IsSupportedEcosystem(string value) =>
        value switch
        {
            "git" or "nuget" or "python" or "npm" or "pnpm" or "yarn" => true,
            _ => false,
        };

    private static bool IsSupportedAudience(string value) =>
        value switch
        {
            "azuredevops" or "azureartifacts" => true,
            _ => false,
        };

    private static bool IsSupportedCredentialKind(string value) =>
        value switch
        {
            "basicpassword"
            or "bearertoken"
            or "npmauthtoken"
            or "nugetplugincredential"
            or "patcompatibility" => true,
            _ => false,
        };

    private static CredentialEcosystem ToEcosystem(string value) =>
        value switch
        {
            "git" => CredentialEcosystem.Git,
            "nuget" => CredentialEcosystem.NuGet,
            "python" => CredentialEcosystem.Python,
            "npm" => CredentialEcosystem.Npm,
            "pnpm" => CredentialEcosystem.Pnpm,
            "yarn" => CredentialEcosystem.Yarn,
            _ => CredentialEcosystem.Unspecified,
        };

    private static TokenAudience ToAudience(string value) =>
        value switch
        {
            "azuredevops" => TokenAudience.AzureDevOps,
            "azureartifacts" => TokenAudience.AzureArtifacts,
            _ => TokenAudience.Unspecified,
        };

    private static CredentialKind ToCredentialKind(string value) =>
        value switch
        {
            "basicpassword" => CredentialKind.BasicPassword,
            "bearertoken" => CredentialKind.BearerToken,
            "npmauthtoken" => CredentialKind.NpmAuthToken,
            "nugetplugincredential" => CredentialKind.NuGetPluginCredential,
            "patcompatibility" => CredentialKind.PatCompatibility,
            _ => CredentialKind.Unspecified,
        };

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return EncodePartitionComponent(value.Trim().ToLowerInvariant());
    }

    private static string EncodePartitionComponent(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}

internal readonly record struct CacheKeyProtocolShape(
    CredentialEcosystem Ecosystem,
    TokenAudience Audience,
    CredentialKind CredentialKind,
    bool HasProject,
    bool HasFeed,
    bool HasRepository
);

public sealed record ConfigurationChangePlan : IJsonOnDeserialized
{
    [JsonRequired]
    public int ContractMajor { get; init; } = ContractVersions.ConfigurationChangePlanMajor;

    [JsonRequired]
    public required string PlanId { get; init; }

    [JsonRequired]
    public required string ChangeSetId { get; init; }

    [JsonRequired]
    public required string OwnerProductId { get; init; }

    [JsonRequired]
    public required ConfigurationScope Scope { get; init; }

    [JsonRequired]
    public ConfigurationAtomicityPolicy AtomicityPolicy { get; init; } =
        ConfigurationAtomicityPolicy.AtomicChangeSetRequired;

    [JsonRequired]
    public ConfigurationRollbackPolicy RollbackPolicy { get; init; } =
        ConfigurationRollbackPolicy.Required;

    [JsonRequired]
    public ConfigurationPlanState State { get; init; } = ConfigurationPlanState.Planned;

    [JsonRequired]
    public ConfigurationManifestCommitPolicy ManifestCommitPolicy { get; init; } =
        ConfigurationManifestCommitPolicy.CommitAfterDurableChanges;

    [JsonRequired]
    public required ConfigurationManifestMetadata Manifest { get; init; }
    public ConfigurationTemporaryContainer? TemporaryContainer { get; init; }

    [JsonRequired]
    public ConfigurationDeclarationPreservation DeclarationPreservation { get; init; } =
        ConfigurationDeclarationPreservation.NotApplicable;
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool ContainsCredentialMaterial { get; init; }

    [JsonRequired]
    public IReadOnlyList<ConfigurationChange> Changes { get; init; } =
        Array.Empty<ConfigurationChange>();
    public IReadOnlyDictionary<string, string> ExtensionData { get; init; } =
        ContractMetadata.Empty;

    void IJsonOnDeserialized.OnDeserialized() => ConfigurationChangePlanPolicy.EnsureValid(this);
}

public static class ConfigurationChangePlanPolicy
{
    public static ConfigurationChangePlan Create(
        string planId,
        string changeSetId,
        string ownerProductId,
        ConfigurationScope scope,
        ConfigurationManifestMetadata manifest,
        IReadOnlyList<ConfigurationChange>? changes = null,
        ConfigurationAtomicityPolicy atomicityPolicy =
            ConfigurationAtomicityPolicy.AtomicChangeSetRequired,
        ConfigurationRollbackPolicy rollbackPolicy = ConfigurationRollbackPolicy.Required,
        ConfigurationPlanState state = ConfigurationPlanState.Planned,
        ConfigurationManifestCommitPolicy manifestCommitPolicy =
            ConfigurationManifestCommitPolicy.CommitAfterDurableChanges,
        ConfigurationTemporaryContainer? temporaryContainer = null,
        ConfigurationDeclarationPreservation declarationPreservation =
            ConfigurationDeclarationPreservation.NotApplicable,
        DateTimeOffset? expiresAt = null,
        bool? containsCredentialMaterial = null,
        IReadOnlyDictionary<string, string>? extensionData = null
    )
    {
        IReadOnlyList<ConfigurationChange> planChanges =
            changes ?? Array.Empty<ConfigurationChange>();
        var plan = new ConfigurationChangePlan
        {
            PlanId = planId,
            ChangeSetId = changeSetId,
            OwnerProductId = ownerProductId,
            Scope = scope,
            AtomicityPolicy = atomicityPolicy,
            RollbackPolicy = rollbackPolicy,
            State = state,
            ManifestCommitPolicy = manifestCommitPolicy,
            Manifest = manifest,
            TemporaryContainer = temporaryContainer,
            DeclarationPreservation = declarationPreservation,
            ExpiresAt = expiresAt,
            ContainsCredentialMaterial =
                containsCredentialMaterial ?? ContainsCredentialMaterial(planChanges),
            Changes = planChanges,
            ExtensionData = extensionData ?? ContractMetadata.Empty,
        };

        EnsureValid(plan);
        return plan;
    }

    public static void EnsureValid(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string? violation = GetViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }
    }

    public static bool IsValid(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return GetViolation(plan) is null;
    }

    public static string? GetViolation(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.ContractMajor != ContractVersions.ConfigurationChangePlanMajor)
        {
            return "Protocol violation: configuration change plan contract major must be 1.";
        }

        if (
            string.IsNullOrWhiteSpace(plan.PlanId)
            || string.IsNullOrWhiteSpace(plan.ChangeSetId)
            || string.IsNullOrWhiteSpace(plan.OwnerProductId)
        )
        {
            return "Protocol violation: configuration change plan identifiers are required.";
        }

        if (!HasKnownPlanEnums(plan))
        {
            return "Protocol violation: configuration change plan enum values must use supported "
                + "v1 values.";
        }

        if (plan.Manifest is null)
        {
            return "Protocol violation: configuration change plan manifest is required.";
        }

        if (
            string.IsNullOrWhiteSpace(plan.Manifest.ManifestId)
            || string.IsNullOrWhiteSpace(plan.Manifest.OwnerProductId)
            || string.IsNullOrWhiteSpace(plan.Manifest.EntrySelector)
        )
        {
            return "Protocol violation: configuration change plan manifest required strings must "
                + "be non-empty.";
        }

        if (
            !string.Equals(
                plan.OwnerProductId,
                plan.Manifest.OwnerProductId,
                StringComparison.Ordinal
            )
        )
        {
            return "Protocol violation: configuration change plan owner product ID must match "
                + "manifest owner product ID.";
        }

        if (plan.Changes is null)
        {
            return "Protocol violation: configuration change plan changes are required.";
        }

        if (plan.Changes.Any(change => change is null))
        {
            return "Protocol violation: configuration change plan changes must not contain null "
                + "entries.";
        }

        string? temporaryContainerViolation = GetTemporaryContainerViolation(plan);
        if (temporaryContainerViolation is not null)
        {
            return temporaryContainerViolation;
        }

        if (plan.Scope == ConfigurationScope.WorkspaceReadOnly && plan.Changes.Count > 0)
        {
            return "Protocol violation: workspace read-only configuration plans must not carry "
                + "configuration changes.";
        }

        foreach (ConfigurationChange change in plan.Changes)
        {
            string? changeViolation = GetChangeViolation(change);
            if (changeViolation is not null)
            {
                return changeViolation;
            }
        }

        string? ciTemporaryTargetViolation = GetCiTemporaryTargetViolation(plan);
        if (ciTemporaryTargetViolation is not null)
        {
            return ciTemporaryTargetViolation;
        }

        if (!plan.ContainsCredentialMaterial && ContainsCredentialMaterial(plan.Changes))
        {
            return "Protocol violation: configuration change plans with secret changes must "
                + "advertise credential material.";
        }

        return null;
    }

    private static bool HasKnownPlanEnums(ConfigurationChangePlan plan) =>
        plan.Scope
            is ConfigurationScope.User
                or ConfigurationScope.WorkspaceReadOnly
                or ConfigurationScope.ExplicitPath
                or ConfigurationScope.CiTemporary
                or ConfigurationScope.Global
        && plan.AtomicityPolicy is ConfigurationAtomicityPolicy.AtomicChangeSetRequired
        && plan.RollbackPolicy is ConfigurationRollbackPolicy.Required
        && plan.State
            is ConfigurationPlanState.Planned
                or ConfigurationPlanState.Applied
                or ConfigurationPlanState.RolledBack
                or ConfigurationPlanState.Failed
        && plan.ManifestCommitPolicy is ConfigurationManifestCommitPolicy.CommitAfterDurableChanges
        && plan.DeclarationPreservation
            is ConfigurationDeclarationPreservation.NotApplicable
                or ConfigurationDeclarationPreservation.AuthOnlyWhenDeclarationsRemainVisible
                or ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig
                or ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig;

    private static string? GetTemporaryContainerViolation(ConfigurationChangePlan plan)
    {
        if (plan.Scope == ConfigurationScope.CiTemporary)
        {
            if (plan.TemporaryContainer is null)
            {
                return "Protocol violation: CI temporary configuration plans require a temporary "
                    + "container.";
            }

            if (string.IsNullOrWhiteSpace(plan.TemporaryContainer.ProductOwnedPath))
            {
                return "Protocol violation: CI temporary configuration plans require a valid "
                    + "product-owned temporary container.";
            }

            if (!plan.TemporaryContainer.DeleteContainerOnRollback)
            {
                return "Protocol violation: CI temporary configuration plans require temporary "
                    + "container cleanup on rollback.";
            }

            if (!plan.TemporaryContainer.DeleteContainerOnRemoval)
            {
                return "Protocol violation: CI temporary configuration plans require temporary "
                    + "container cleanup on removal.";
            }

            if (
                !IsCanonicalFullyQualifiedConfigurationPath(
                    plan.TemporaryContainer.ProductOwnedPath
                )
            )
            {
                return "Protocol violation: CI temporary configuration plans require a fully "
                    + "qualified canonical product-owned temporary container path.";
            }

            if (IsConfigurationFilesystemRoot(plan.TemporaryContainer.ProductOwnedPath))
            {
                return "Protocol violation: CI temporary configuration plans must not use a "
                    + "filesystem root as the product-owned path.";
            }

            if (
                plan.TemporaryContainer.Kind
                is not (
                    ConfigurationTemporaryContainerKind.NpmrcFile
                    or ConfigurationTemporaryContainerKind.TemporaryHome
                )
            )
            {
                return "Protocol violation: CI temporary configuration plans require a valid "
                    + "product-owned temporary container.";
            }

            string? activationEnvironmentViolation =
                GetTemporaryContainerActivationEnvironmentViolation(plan.TemporaryContainer);
            if (activationEnvironmentViolation is not null)
            {
                return activationEnvironmentViolation;
            }

            if (
                plan.DeclarationPreservation
                is not (
                    ConfigurationDeclarationPreservation.AuthOnlyWhenDeclarationsRemainVisible
                    or ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig
                    or ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig
                )
            )
            {
                return "Protocol violation: CI temporary configuration plans require a declaration "
                    + "preservation policy.";
            }

            return null;
        }

        if (plan.TemporaryContainer is not null)
        {
            return "Protocol violation: temporary containers are valid only for CI temporary "
                + "configuration plans.";
        }

        return plan.DeclarationPreservation == ConfigurationDeclarationPreservation.NotApplicable
            ? null
            : "Protocol violation: declaration preservation is valid only for CI temporary "
                + "configuration plans.";
    }

    private static string? GetTemporaryContainerActivationEnvironmentViolation(
        ConfigurationTemporaryContainer container
    )
    {
        return container.Kind switch
        {
            ConfigurationTemporaryContainerKind.NpmrcFile =>
                GetNpmrcFileActivationEnvironmentViolation(container),
            ConfigurationTemporaryContainerKind.TemporaryHome =>
                GetTemporaryHomeActivationEnvironmentViolation(container),
            _ => container.ActivationEnvironment is null
                ? null
                : "Protocol violation: activation environment metadata is valid only for CI "
                    + "temporary npmrc file or HOME containers.",
        };
    }

    private static string? GetActivationEnvironmentShapeViolation(
        ConfigurationActivationEnvironment? activationEnvironment,
        string containerDescription,
        out IReadOnlyDictionary<string, string> setVariables,
        out IReadOnlyList<string> clearVariables
    )
    {
        setVariables = ContractMetadata.Empty;
        clearVariables = Array.Empty<string>();

        if (activationEnvironment is null)
        {
            return string.Concat(
                "Protocol violation: ",
                containerDescription,
                " containers require activation environment metadata."
            );
        }

        setVariables = activationEnvironment.SetVariables;
        clearVariables = activationEnvironment.ClearVariables;
        if (setVariables is null || clearVariables is null)
        {
            return string.Concat(
                "Protocol violation: ",
                containerDescription,
                " activation environment metadata must include set and clear variables."
            );
        }

        if (
            setVariables.Any(variable =>
                string.IsNullOrWhiteSpace(variable.Key) || variable.Value is null
            ) || clearVariables.Any(string.IsNullOrWhiteSpace)
        )
        {
            return string.Concat(
                "Protocol violation: ",
                containerDescription,
                " activation environment variables must be non-empty."
            );
        }

        return null;
    }

    private static string? GetNpmrcFileActivationEnvironmentViolation(
        ConfigurationTemporaryContainer container
    )
    {
        string? shapeViolation = GetActivationEnvironmentShapeViolation(
            container.ActivationEnvironment,
            "CI temporary npmrc file",
            out IReadOnlyDictionary<string, string> setVariables,
            out IReadOnlyList<string> clearVariables
        );
        if (shapeViolation is not null)
        {
            return shapeViolation;
        }

        string? platform = container.ActivationEnvironment?.Platform;
        if (string.Equals(platform, "windows", StringComparison.Ordinal))
        {
            if (
                GetConfigurationPathKind(container.ProductOwnedPath)
                is not (ConfigurationPathKind.WindowsDrive or ConfigurationPathKind.WindowsUnc)
            )
            {
                return "Protocol violation: Windows CI temporary npmrc file activation requires a "
                    + "Windows product-owned path.";
            }

            if (
                setVariables.Count != 1
                || !HasVariableValue(
                    setVariables,
                    "NPM_CONFIG_USERCONFIG",
                    container.ProductOwnedPath
                )
                || clearVariables.Count != 0
            )
            {
                return "Protocol violation: Windows CI temporary npmrc file activation must set "
                    + "only NPM_CONFIG_USERCONFIG to the product-owned path and clear no "
                    + "variables.";
            }

            return null;
        }

        if (string.Equals(platform, "posix", StringComparison.Ordinal))
        {
            if (
                GetConfigurationPathKind(container.ProductOwnedPath)
                is not ConfigurationPathKind.PosixAbsolute
            )
            {
                return "Protocol violation: POSIX CI temporary npmrc file activation requires a "
                    + "POSIX product-owned path.";
            }

            if (
                setVariables.Count != 2
                || !HasVariableValue(
                    setVariables,
                    "NPM_CONFIG_USERCONFIG",
                    container.ProductOwnedPath
                )
                || !HasVariableValue(
                    setVariables,
                    "npm_config_userconfig",
                    container.ProductOwnedPath
                )
                || clearVariables.Count != 0
            )
            {
                return "Protocol violation: POSIX CI temporary npmrc file activation must set "
                    + "NPM_CONFIG_USERCONFIG and npm_config_userconfig to the product-owned "
                    + "path and clear no variables.";
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(platform))
        {
            return "Protocol violation: CI temporary npmrc file activation requires platform "
                + "metadata.";
        }

        return "Protocol violation: CI temporary npmrc file activation platform must be windows or "
            + "posix.";
    }

    private static string? GetTemporaryHomeActivationEnvironmentViolation(
        ConfigurationTemporaryContainer container
    )
    {
        string? shapeViolation = GetActivationEnvironmentShapeViolation(
            container.ActivationEnvironment,
            "CI temporary HOME",
            out IReadOnlyDictionary<string, string> setVariables,
            out IReadOnlyList<string> clearVariables
        );
        if (shapeViolation is not null)
        {
            return shapeViolation;
        }

        return
            GetConfigurationPathKind(container.ProductOwnedPath)
                is ConfigurationPathKind.WindowsDrive
                    or ConfigurationPathKind.WindowsUnc
            ? GetWindowsTemporaryHomeActivationEnvironmentViolation(
                container.ProductOwnedPath,
                setVariables,
                clearVariables
            )
            : GetPosixTemporaryHomeActivationEnvironmentViolation(
                container.ProductOwnedPath,
                setVariables,
                clearVariables
            );
    }

    private static string? GetWindowsTemporaryHomeActivationEnvironmentViolation(
        string productOwnedPath,
        IReadOnlyDictionary<string, string> setVariables,
        IReadOnlyList<string> clearVariables
    )
    {
        if (
            setVariables.Count != 2
            || !HasVariableValue(setVariables, "USERPROFILE", productOwnedPath)
            || !HasVariableValue(setVariables, "HOME", productOwnedPath)
            || clearVariables.Count != 2
            || !ContainsVariable(clearVariables, "HOMEDRIVE")
            || !ContainsVariable(clearVariables, "HOMEPATH")
        )
        {
            return "Protocol violation: Windows CI temporary HOME activation must set USERPROFILE "
                + "and HOME to the product-owned path and clear HOMEDRIVE and HOMEPATH.";
        }

        return null;
    }

    private static string? GetPosixTemporaryHomeActivationEnvironmentViolation(
        string productOwnedPath,
        IReadOnlyDictionary<string, string> setVariables,
        IReadOnlyList<string> clearVariables
    )
    {
        if (
            setVariables.Count != 1
            || !HasVariableValue(setVariables, "HOME", productOwnedPath)
            || clearVariables.Count != 0
        )
        {
            return "Protocol violation: POSIX CI temporary HOME activation must set only HOME to "
                + "the product-owned path.";
        }

        return null;
    }

    private static bool HasVariableValue(
        IReadOnlyDictionary<string, string> variables,
        string name,
        string value
    ) =>
        variables.TryGetValue(name, out string? actualValue)
        && string.Equals(actualValue, value, StringComparison.Ordinal);

    private static bool ContainsVariable(IEnumerable<string> variables, string name) =>
        variables.Any(variable => string.Equals(variable, name, StringComparison.Ordinal));

    private static string? GetChangeViolation(ConfigurationChange change)
    {
        if (!HasKnownChangeEnums(change))
        {
            return "Protocol violation: configuration change enum values must use supported v1 "
                + "values.";
        }

        if (
            string.IsNullOrWhiteSpace(change.TargetPathOrName)
            || string.IsNullOrWhiteSpace(change.Key)
        )
        {
            return "Protocol violation: configuration change target and key are required.";
        }

        if (ContainsLineBreak(change.Key))
        {
            return "Protocol violation: configuration change keys must not contain CR or LF.";
        }

        if (!change.RequiresOwnershipRecord)
        {
            return "Protocol violation: configuration changes must require product ownership "
                + "records.";
        }

        if (
            change.TargetKind == ConfigurationTargetKind.Yarnrc
            && IsYarnNpmAuthIdentKey(change.Key)
        )
        {
            return "Protocol violation: Yarn npmAuthIdent is unsupported and must not be "
                + "emitted as a product-owned configuration plan entry.";
        }

        if (RequiresValue(change.Operation) && change.Value is null)
        {
            return "Protocol violation: value-writing configuration changes require a value.";
        }

        if (!RequiresValue(change.Operation) && change.Value is not null)
        {
            return "Protocol violation: non-value configuration changes must not carry a value.";
        }

        if (
            RequiresValue(change.Operation)
            && IsLineConfigurationTarget(change.TargetKind)
            && ContainsLineBreak(change.Value!)
        )
        {
            return "Protocol violation: line-oriented configuration values must not contain CR or "
                + "LF.";
        }

        if (IsIntrinsicallySecretNpmCompatibleAuthValue(change) && ContainsLineBreak(change.Value!))
        {
            return "Protocol violation: npm-compatible secret auth values must not contain CR or "
                + "LF.";
        }

        if (IsIntrinsicallySecretNpmCompatibleAuthValue(change) && !change.IsSecretValue)
        {
            return "Protocol violation: npm-compatible auth values must be marked as secret.";
        }

        if (
            (
                change.Operation
                is ConfigurationChangeOperation.Update
                    or ConfigurationChangeOperation.Refresh
                    or ConfigurationChangeOperation.Remove
                    or ConfigurationChangeOperation.RemoveAdapter
            ) && string.IsNullOrWhiteSpace(change.PreviousOwnedEntryMetadata)
        )
        {
            return "Protocol violation: update, refresh, remove, and remove-adapter changes "
                + "require previous owned-entry metadata.";
        }

        return null;
    }

    private static bool RequiresValue(ConfigurationChangeOperation operation) =>
        operation
            is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh;

    private static bool IsLineConfigurationTarget(ConfigurationTargetKind targetKind) =>
        targetKind
            is ConfigurationTargetKind.GitConfig
                or ConfigurationTargetKind.Npmrc
                or ConfigurationTargetKind.Yarnrc;

    internal static bool IsIntrinsicallySecretNpmCompatibleAuthValue(ConfigurationChange change) =>
        change.Value is not null
        && RequiresValue(change.Operation)
        && (
            (change.TargetKind == ConfigurationTargetKind.Npmrc && IsNpmAuthTokenKey(change.Key))
            || (
                change.TargetKind == ConfigurationTargetKind.Yarnrc
                && IsYarnNpmAuthTokenKey(change.Key)
            )
        );

    private static bool IsNpmAuthTokenKey(string? key) =>
        key is not null
        && (
            string.Equals(key, "_authToken", StringComparison.Ordinal)
            || key.EndsWith(":_authToken", StringComparison.Ordinal)
        );

    private static bool IsYarnNpmAuthTokenKey(string? key) =>
        key is not null
        && (
            string.Equals(key, "npmAuthToken", StringComparison.Ordinal)
            || key.EndsWith(".npmAuthToken", StringComparison.Ordinal)
            || string.Equals(key, "npmAuthIdent", StringComparison.Ordinal)
            || key.EndsWith(".npmAuthIdent", StringComparison.Ordinal)
        );

    private static bool IsYarnNpmAuthIdentKey(string? key) =>
        key is not null
        && (
            string.Equals(key, "npmAuthIdent", StringComparison.Ordinal)
            || key.EndsWith(".npmAuthIdent", StringComparison.Ordinal)
        );

    private static bool ContainsLineBreak(string value) =>
        value.Contains('\r', StringComparison.Ordinal)
        || value.Contains('\n', StringComparison.Ordinal);

    private static string? GetCiTemporaryTargetViolation(ConfigurationChangePlan plan)
    {
        if (plan.Scope != ConfigurationScope.CiTemporary || plan.TemporaryContainer is null)
        {
            return null;
        }

        foreach (ConfigurationChange change in plan.Changes)
        {
            if (!IsCanonicalFullyQualifiedConfigurationPath(change.TargetPathOrName))
            {
                return "Protocol violation: CI temporary configuration changes require fully "
                    + "qualified canonical target paths.";
            }

            if (
                !TemporaryContainerKindSupportsTargetKind(
                    plan.TemporaryContainer.Kind,
                    change.TargetKind
                )
            )
            {
                return "Protocol violation: CI temporary configuration changes must use a target "
                    + "kind compatible with the declared temporary container.";
            }

            if (
                !TargetsDeclaredTemporaryContainer(plan.TemporaryContainer, change.TargetPathOrName)
            )
            {
                return "Protocol violation: CI temporary configuration changes must target only "
                    + "the declared product-owned temporary container.";
            }
        }

        return null;
    }

    private static bool TemporaryContainerKindSupportsTargetKind(
        ConfigurationTemporaryContainerKind containerKind,
        ConfigurationTargetKind targetKind
    ) =>
        containerKind switch
        {
            ConfigurationTemporaryContainerKind.NpmrcFile => targetKind
                == ConfigurationTargetKind.Npmrc,
            ConfigurationTemporaryContainerKind.TemporaryHome => targetKind
                == ConfigurationTargetKind.Yarnrc,
            _ => false,
        };

    private static bool TargetsDeclaredTemporaryContainer(
        ConfigurationTemporaryContainer container,
        string targetPath
    ) =>
        container.Kind switch
        {
            ConfigurationTemporaryContainerKind.NpmrcFile => ConfigurationPathsEqual(
                targetPath,
                container.ProductOwnedPath
            ),
            ConfigurationTemporaryContainerKind.TemporaryHome => ConfigurationPathsEqual(
                targetPath,
                CombineConfigurationPath(container.ProductOwnedPath, ".yarnrc.yml")
            ),
            _ => false,
        };

    private static bool IsCanonicalFullyQualifiedConfigurationPath(string path) =>
        !HasLeadingOrTrailingWhiteSpace(path)
        && !HasWindowsExtendedPathPrefix(path)
        && IsFullyQualifiedConfigurationPath(path)
        && !ContainsRelativeConfigurationPathSegment(path)
        && string.Equals(path, CanonicalizeConfigurationPath(path), StringComparison.Ordinal);

    private static bool HasLeadingOrTrailingWhiteSpace(string value) =>
        !string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static bool HasWindowsExtendedPathPrefix(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal)
        || path.StartsWith(@"\\.\", StringComparison.Ordinal)
        || path.StartsWith("//?/", StringComparison.Ordinal)
        || path.StartsWith("//./", StringComparison.Ordinal);

    private static bool IsFullyQualifiedConfigurationPath(string path)
    {
        switch (GetConfigurationPathKind(path))
        {
            case ConfigurationPathKind.WindowsDrive:
                return true;
            case ConfigurationPathKind.WindowsUnc:
                string[] uncSegments = NormalizeConfigurationPath(path)
                    .Split('/', StringSplitOptions.RemoveEmptyEntries);
                return uncSegments.Length >= 2;
            case ConfigurationPathKind.PosixAbsolute:
                return true;
            default:
                return false;
        }
    }

    private static bool ContainsRelativeConfigurationPathSegment(string path)
    {
        string[] segments = NormalizeConfigurationPath(path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment is "." or "..");
    }

    private static bool ConfigurationPathsEqual(string left, string right) =>
        ConfigurationPathKindsMatch(left, right)
        && string.Equals(
            NormalizeConfigurationPath(left),
            NormalizeConfigurationPath(right),
            GetConfigurationPathComparison(left, right)
        );

    private static string CombineConfigurationPath(string directoryPath, string childName) =>
        NormalizeConfigurationPath(directoryPath) + "/" + childName;

    private static bool IsConfigurationFilesystemRoot(string path)
    {
        string normalized = NormalizeConfigurationPath(path);
        return GetConfigurationPathKind(path) switch
        {
            ConfigurationPathKind.PosixAbsolute => normalized.Length == 0,
            ConfigurationPathKind.WindowsDrive => normalized.Length == 2 && normalized[1] == ':',
            ConfigurationPathKind.WindowsUnc => normalized
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Length == 2,
            _ => false,
        };
    }

    private static string CanonicalizeConfigurationPath(string path)
    {
        ConfigurationPathKind kind = GetConfigurationPathKind(path);
        string canonical = kind switch
        {
            ConfigurationPathKind.WindowsDrive => path.Replace('/', '\\'),
            ConfigurationPathKind.WindowsUnc => path.Replace('\\', '/'),
            _ => path,
        };
        char separator = kind == ConfigurationPathKind.WindowsDrive ? '\\' : '/';
        int rootLength = kind == ConfigurationPathKind.WindowsUnc ? 2 : 0;
        string duplicateSeparator = new(separator, 2);
        while (canonical.IndexOf(duplicateSeparator, rootLength, StringComparison.Ordinal) >= 0)
        {
            canonical =
                canonical[..rootLength]
                + canonical[rootLength..]
                    .Replace(duplicateSeparator, separator.ToString(), StringComparison.Ordinal);
        }

        if (
            (
                kind == ConfigurationPathKind.PosixAbsolute
                && string.Equals(canonical, "/", StringComparison.Ordinal)
            )
            || (
                kind == ConfigurationPathKind.WindowsDrive
                && canonical.Length == 3
                && char.IsLetter(canonical[0])
                && canonical[1] == ':'
                && canonical[2] == '\\'
            )
        )
        {
            return canonical;
        }

        return canonical.TrimEnd(separator);
    }

    private static string NormalizeConfigurationPath(string path)
    {
        ConfigurationPathKind kind = GetConfigurationPathKind(path);
        string normalized = kind
            is ConfigurationPathKind.WindowsDrive
                or ConfigurationPathKind.WindowsUnc
            ? path.Replace('\\', '/')
            : path;
        int rootLength = kind == ConfigurationPathKind.WindowsUnc ? 2 : 0;
        while (normalized.IndexOf("//", rootLength, StringComparison.Ordinal) >= 0)
        {
            normalized =
                normalized[..rootLength]
                + normalized[rootLength..].Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized.TrimEnd('/');
    }

    private static StringComparison GetConfigurationPathComparison(string left, string right) =>
        GetConfigurationPathKind(left)
            is ConfigurationPathKind.WindowsDrive
                or ConfigurationPathKind.WindowsUnc
        || GetConfigurationPathKind(right)
            is ConfigurationPathKind.WindowsDrive
                or ConfigurationPathKind.WindowsUnc
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool ConfigurationPathKindsMatch(string left, string right)
    {
        ConfigurationPathKind leftKind = GetConfigurationPathKind(left);
        return leftKind != ConfigurationPathKind.Invalid
            && leftKind == GetConfigurationPathKind(right);
    }

    private static ConfigurationPathKind GetConfigurationPathKind(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return ConfigurationPathKind.Invalid;
        }

        if (
            path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal)
        )
        {
            return ConfigurationPathKind.WindowsUnc;
        }

        if (
            path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/')
        )
        {
            return ConfigurationPathKind.WindowsDrive;
        }

        if (path[0] == '/')
        {
            return path.Contains('\\', StringComparison.Ordinal)
                ? ConfigurationPathKind.Invalid
                : ConfigurationPathKind.PosixAbsolute;
        }

        return ConfigurationPathKind.Invalid;
    }

    private enum ConfigurationPathKind
    {
        Invalid,
        WindowsDrive,
        WindowsUnc,
        PosixAbsolute,
    }

    private static bool HasKnownChangeEnums(ConfigurationChange change) =>
        change.Operation
            is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Remove
                or ConfigurationChangeOperation.EnsureFile
                or ConfigurationChangeOperation.InstallAdapter
                or ConfigurationChangeOperation.RemoveAdapter
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh
        && change.TargetKind
            is ConfigurationTargetKind.GitConfig
                or ConfigurationTargetKind.NuGetPluginLayout
                or ConfigurationTargetKind.PythonKeyringBackend
                or ConfigurationTargetKind.KeyringShim
                or ConfigurationTargetKind.Npmrc
                or ConfigurationTargetKind.Yarnrc
                or ConfigurationTargetKind.CiTemporaryFile;

    private static bool ContainsCredentialMaterial(IEnumerable<ConfigurationChange>? changes) =>
        changes?.Any(change =>
            change is { IsSecretValue: true }
            || (change is not null && IsIntrinsicallySecretNpmCompatibleAuthValue(change))
        ) == true;
}

public sealed record ConfigurationManifestMetadata
{
    [JsonRequired]
    public required string ManifestId { get; init; }

    [JsonRequired]
    public required string OwnerProductId { get; init; }

    [JsonRequired]
    public required string EntrySelector { get; init; }
    public string? ProductVersion { get; init; }
    public string? PreviousOwnedEntryHash { get; init; }
    public IReadOnlyDictionary<string, string> SafeMetadata { get; init; } = ContractMetadata.Empty;
}

public sealed record ConfigurationTemporaryContainer
{
    [JsonRequired]
    public required ConfigurationTemporaryContainerKind Kind { get; init; }

    [JsonRequired]
    public required string ProductOwnedPath { get; init; }
    public ConfigurationActivationEnvironment? ActivationEnvironment { get; init; }
    public bool DeleteContainerOnRollback { get; init; } = true;
    public bool DeleteContainerOnRemoval { get; init; } = true;
}

public sealed record ConfigurationActivationEnvironment
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Platform { get; init; }

    [JsonRequired]
    public required IReadOnlyDictionary<string, string> SetVariables { get; init; }

    [JsonRequired]
    public required IReadOnlyList<string> ClearVariables { get; init; }
}

public sealed record ConfigurationChange
{
    [JsonRequired]
    public required ConfigurationChangeOperation Operation { get; init; }

    [JsonRequired]
    public required ConfigurationTargetKind TargetKind { get; init; }

    [JsonRequired]
    public required string TargetPathOrName { get; init; }

    [JsonRequired]
    public required string Key { get; init; }
    public string? Value { get; init; }
    public required bool RequiresOwnershipRecord { get; init; }
    public bool IsSecretValue { get; init; }
    public string? PreviousOwnedEntryMetadata { get; init; }
    public bool PreserveDeclarationsAndComments { get; init; } = true;

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {{ {1} = {2}, {3} = {4}, {5} = {6}, {7} = {8}, {9} = {10}, "
                + "{11} = {12}, {13} = {14}, {15} = {16}, {17} = {18} }}",
            nameof(ConfigurationChange),
            nameof(Operation),
            Operation,
            nameof(TargetKind),
            TargetKind,
            nameof(TargetPathOrName),
            TargetPathOrName,
            nameof(Key),
            Key,
            nameof(Value),
            IsSecretValue
            || ConfigurationChangePlanPolicy.IsIntrinsicallySecretNpmCompatibleAuthValue(this)
                ? "<redacted>"
                : Value,
            nameof(RequiresOwnershipRecord),
            RequiresOwnershipRecord,
            nameof(IsSecretValue),
            IsSecretValue,
            nameof(PreviousOwnedEntryMetadata),
            PreviousOwnedEntryMetadata,
            nameof(PreserveDeclarationsAndComments),
            PreserveDeclarationsAndComments
        );
}

public sealed record DoctorCheck : IJsonOnDeserialized
{
    [JsonRequired]
    public int ContractMajor { get; init; } = ContractVersions.DoctorCheckMajor;
    public required string CheckId { get; init; }
    public required DoctorCheckStatus Status { get; init; }
    public required DoctorCheckSeverity Severity { get; init; }
    public required string Target { get; init; }
    public required string Summary { get; init; }
    public required string DiagnosticsCorrelationId { get; init; }
    public string? ObservedValue { get; init; }
    public string? ExpectedValue { get; init; }
    public string? Remediation { get; init; }
    public IReadOnlyDictionary<string, string> SafeDetails { get; init; } = ContractMetadata.Empty;

    void IJsonOnDeserialized.OnDeserialized() => DoctorCheckPolicy.EnsureValid(this);
}

public static class DoctorCheckPolicy
{
    public static void EnsureValid(DoctorCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        string? violation = GetViolation(check);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(check));
        }
    }

    public static bool IsValid(DoctorCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return GetViolation(check) is null;
    }

    public static string? GetViolation(DoctorCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (check.ContractMajor != ContractVersions.DoctorCheckMajor)
        {
            return "Protocol violation: doctor check contract major must be 1.";
        }

        if (
            string.IsNullOrWhiteSpace(check.CheckId)
            || string.IsNullOrWhiteSpace(check.Target)
            || string.IsNullOrWhiteSpace(check.Summary)
            || string.IsNullOrWhiteSpace(check.DiagnosticsCorrelationId)
        )
        {
            return "Protocol violation: doctor check required identity fields must be non-empty.";
        }

        return HasKnownStatusAndSeverity(check)
            ? null
            : "Protocol violation: doctor check status and severity must use supported v1 values.";
    }

    private static bool HasKnownStatusAndSeverity(DoctorCheck check) =>
        check.Status
            is DoctorCheckStatus.Pass
                or DoctorCheckStatus.Warning
                or DoctorCheckStatus.Fail
                or DoctorCheckStatus.Skipped
                or DoctorCheckStatus.Unsupported
                or DoctorCheckStatus.Deferred
                or DoctorCheckStatus.NotApplicable
        && check.Severity
            is DoctorCheckSeverity.Info
                or DoctorCheckSeverity.Warning
                or DoctorCheckSeverity.Error;
}

public sealed record AdapterHostResult : IJsonOnDeserialized
{
    [JsonRequired]
    public int ContractMajor { get; init; } = ContractVersions.AdapterHostResultMajor;
    public required AdapterProtocol Protocol { get; init; }
    public required AdapterHostExitCode ExitCode { get; init; }
    public required bool WriteProtocolStdout { get; init; }
    public required bool WriteDiagnosticStderr { get; init; }
    public string? SafeDiagnosticCode { get; init; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (ContractMajor != ContractVersions.AdapterHostResultMajor)
        {
            throw new ArgumentException(
                "Protocol violation: adapter host result contract major must be 1.",
                nameof(ContractMajor)
            );
        }
    }
}

public static class AdapterHostResultMapper
{
    public const string GitCredentialHelperBearerTokenUsername = "AzureDevOps";

    public static AdapterHostResult Map(AdapterProtocol protocol, CredentialResult result) =>
        Map(protocol, CredentialOperation.Get, result);

    public static AdapterHostResult Map(
        AdapterProtocol protocol,
        CredentialOperation operation,
        CredentialResult result
    )
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!IsKnownAdapterProtocol(protocol))
        {
            return Create(
                AdapterProtocol.Unspecified,
                AdapterHostExitCode.ConfigurationError,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                "UnsupportedAdapterProtocol"
            );
        }

        if (
            protocol == AdapterProtocol.GitCredentialHelper
            && !IsSupportedGitCredentialHelperOperation(operation)
        )
        {
            return Create(
                protocol,
                AdapterHostExitCode.ConfigurationError,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                "ProtocolViolation"
            );
        }

        if (result.ContractMajor != ContractVersions.CredentialContractMajor)
        {
            return Create(
                protocol,
                AdapterHostExitCode.ConfigurationError,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                "UnsupportedContractMajor"
            );
        }

        if (result.CacheKey is not null && !CacheKeySchema.IsValid(result.CacheKey))
        {
            return Create(
                protocol,
                AdapterHostExitCode.ConfigurationError,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                "UnsupportedCacheKeySchemaMajor"
            );
        }

        if (result.Status == CredentialResultStatus.Success && result.Error is not null)
        {
            return Create(
                protocol,
                AdapterHostExitCode.ConfigurationError,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                "ProtocolViolation"
            );
        }

        if (
            result.Status == CredentialResultStatus.Success
            && !HasRequiredSuccessMaterial(protocol, operation, result)
        )
        {
            return Create(
                protocol,
                AdapterHostExitCode.ConfigurationError,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                "ProtocolViolation"
            );
        }

        if (
            result.Status == CredentialResultStatus.Success
            && result.CacheKey is not null
            && !IsCacheKeyCoherentWithAdapterProtocol(protocol, result.CacheKey)
        )
        {
            return Create(
                protocol,
                AdapterHostExitCode.ConfigurationError,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                "ProtocolViolation"
            );
        }

        if (
            result.Status == CredentialResultStatus.Success
            && !IsSuccessMaterialCoherentWithCacheKey(protocol, operation, result)
        )
        {
            return Create(
                protocol,
                AdapterHostExitCode.ConfigurationError,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                "ProtocolViolation"
            );
        }

        if (
            result.Error is not null
            && TryMapHardCredentialError(
                protocol,
                result.Error,
                out AdapterHostResult hardCredentialErrorResult
            )
        )
        {
            return hardCredentialErrorResult;
        }

        return result.Status switch
        {
            CredentialResultStatus.Success => Create(
                protocol,
                AdapterHostExitCode.Success,
                writeProtocolStdout: WritesProtocolStdoutOnSuccess(protocol, operation),
                writeDiagnosticStderr: false,
                result.Error?.Code
            ),
            CredentialResultStatus.NoCredential => Create(
                protocol,
                AdapterHostExitCode.NoCredential,
                writeProtocolStdout: false,
                writeDiagnosticStderr: false,
                result.Error?.Code
            ),
            CredentialResultStatus.InteractionRequired
            or CredentialResultStatus.InteractionBlocked => Create(
                protocol,
                AdapterHostExitCode.InteractionRequired,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                result.Error?.Code
            ),
            CredentialResultStatus.Unauthorized => Create(
                protocol,
                AdapterHostExitCode.Unauthorized,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                result.Error?.Code
            ),
            CredentialResultStatus.CacheUnavailable => Create(
                protocol,
                AdapterHostExitCode.CacheUnavailable,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                result.Error?.Code
            ),
            CredentialResultStatus.IntegrityFailure => Create(
                protocol,
                AdapterHostExitCode.IntegrityFailure,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                result.Error?.Code
            ),
            CredentialResultStatus.ProtocolViolation => Create(
                protocol,
                AdapterHostExitCode.ConfigurationError,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                result.Error?.Code
            ),
            CredentialResultStatus.Fatal => Create(
                protocol,
                AdapterHostExitCode.Fatal,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                result.Error?.Code
            ),
            _ => Create(
                protocol,
                AdapterHostExitCode.ConfigurationError,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                result.Error?.Code
            ),
        };
    }

    public static bool TryMapGitCredentialHelperBasicMaterial(
        CredentialResult result,
        [NotNullWhen(true)] out string? username,
        [NotNullWhen(true)] out string? password
    )
    {
        ArgumentNullException.ThrowIfNull(result);

        if (
            !string.IsNullOrEmpty(result.BearerToken)
            && (!string.IsNullOrEmpty(result.Username) || !string.IsNullOrEmpty(result.Password))
        )
        {
            username = null;
            password = null;
            return false;
        }

        bool hasBasicMaterial = HasBasicCredentialSuccessMaterial(result.Username, result.Password);
        bool hasBearerMaterial =
            !string.IsNullOrEmpty(result.BearerToken)
            && !ContainsBasicCredentialProtocolLineBreak(result.BearerToken);

        if (hasBasicMaterial)
        {
            username = result.Username!;
            password = result.Password!;
            return true;
        }

        if (hasBearerMaterial)
        {
            username = GitCredentialHelperBearerTokenUsername;
            password = result.BearerToken!;
            return true;
        }

        username = null;
        password = null;
        return false;
    }

    private static AdapterHostResult Create(
        AdapterProtocol protocol,
        AdapterHostExitCode exitCode,
        bool writeProtocolStdout,
        bool writeDiagnosticStderr,
        string? code
    ) =>
        new()
        {
            Protocol = protocol,
            ExitCode = exitCode,
            WriteProtocolStdout = writeProtocolStdout,
            WriteDiagnosticStderr = writeDiagnosticStderr,
            SafeDiagnosticCode = code,
        };

    private static bool TryMapHardCredentialError(
        AdapterProtocol protocol,
        CredentialError error,
        out AdapterHostResult result
    )
    {
        result = error.Kind switch
        {
            CredentialErrorKind.InteractionRequired or CredentialErrorKind.InteractionBlocked =>
                Create(
                    protocol,
                    AdapterHostExitCode.InteractionRequired,
                    writeProtocolStdout: false,
                    writeDiagnosticStderr: true,
                    error.Code
                ),
            CredentialErrorKind.Unauthorized => Create(
                protocol,
                AdapterHostExitCode.Unauthorized,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                error.Code
            ),
            CredentialErrorKind.CacheUnavailable => Create(
                protocol,
                AdapterHostExitCode.CacheUnavailable,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                error.Code
            ),
            CredentialErrorKind.CredentialUnavailable
            or CredentialErrorKind.FlowDeferred
            or CredentialErrorKind.FlowDisabled
            or CredentialErrorKind.UnsupportedFlow
            or CredentialErrorKind.PolicyViolation
            or CredentialErrorKind.ProtocolViolation => Create(
                protocol,
                AdapterHostExitCode.ConfigurationError,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                error.Code
            ),
            CredentialErrorKind.IntegrityFailure => Create(
                protocol,
                AdapterHostExitCode.IntegrityFailure,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                error.Code
            ),
            CredentialErrorKind.Fatal => Create(
                protocol,
                AdapterHostExitCode.Fatal,
                writeProtocolStdout: false,
                writeDiagnosticStderr: true,
                error.Code
            ),
            _ => null!,
        };

        return result is not null;
    }

    private static bool IsKnownAdapterProtocol(AdapterProtocol protocol) =>
        protocol
            is AdapterProtocol.GitCredentialHelper
                or AdapterProtocol.NuGetPlugin
                or AdapterProtocol.PythonKeyringBackend
                or AdapterProtocol.KeyringHelper
                or AdapterProtocol.NpmConfiguration;

    private static bool IsSupportedGitCredentialHelperOperation(CredentialOperation operation) =>
        operation
            is CredentialOperation.Get
                or CredentialOperation.Store
                or CredentialOperation.Erase;

    private static bool HasRequiredSuccessMaterial(
        AdapterProtocol protocol,
        CredentialOperation operation,
        CredentialResult result
    ) =>
        protocol switch
        {
            AdapterProtocol.GitCredentialHelper => operation
                is CredentialOperation.Store
                    or CredentialOperation.Erase
                ? !ContainsCredentialMaterial(result)
                : TryMapGitCredentialHelperBasicMaterial(result, out _, out _),
            AdapterProtocol.PythonKeyringBackend or AdapterProtocol.KeyringHelper =>
                !string.IsNullOrEmpty(result.Password),
            AdapterProtocol.NuGetPlugin => HasBasicCredentialSuccessMaterial(
                result.Username,
                result.Password
            ),
            AdapterProtocol.NpmConfiguration => !string.IsNullOrEmpty(result.BearerToken)
                && !ContainsBasicCredentialProtocolLineBreak(result.BearerToken),
            _ => false,
        };

    private static bool IsSuccessMaterialCoherentWithCacheKey(
        AdapterProtocol protocol,
        CredentialOperation operation,
        CredentialResult result
    )
    {
        if (
            protocol != AdapterProtocol.GitCredentialHelper
            || operation != CredentialOperation.Get
            || result.CacheKey is null
        )
        {
            return true;
        }

        CredentialKind credentialKind = CacheKeySchema.GetCredentialKind(result.CacheKey);
        bool hasBasicMaterial = HasBasicCredentialSuccessMaterial(result.Username, result.Password);
        bool hasBearerMaterial =
            !string.IsNullOrEmpty(result.BearerToken)
            && !ContainsBasicCredentialProtocolLineBreak(result.BearerToken);

        return credentialKind switch
        {
            CredentialKind.BearerToken => hasBearerMaterial && !hasBasicMaterial,
            CredentialKind.BasicPassword or CredentialKind.PatCompatibility => hasBasicMaterial
                && !hasBearerMaterial,
            _ => false,
        };
    }

    private static bool IsCacheKeyCoherentWithAdapterProtocol(
        AdapterProtocol protocol,
        CacheKey cacheKey
    )
    {
        CacheKeyProtocolShape shape = CacheKeySchema.GetProtocolShape(cacheKey);
        return protocol switch
        {
            AdapterProtocol.GitCredentialHelper => shape.Ecosystem == CredentialEcosystem.Git
                && shape.Audience == TokenAudience.AzureDevOps
                && shape.CredentialKind
                    is CredentialKind.BasicPassword
                        or CredentialKind.BearerToken
                        or CredentialKind.PatCompatibility
                && !shape.HasProject
                && !shape.HasFeed
                && !shape.HasRepository,
            AdapterProtocol.NuGetPlugin => shape.Ecosystem == CredentialEcosystem.NuGet
                && shape.Audience == TokenAudience.AzureArtifacts
                && shape.CredentialKind == CredentialKind.NuGetPluginCredential
                && shape.HasFeed
                && !shape.HasRepository,
            AdapterProtocol.PythonKeyringBackend or AdapterProtocol.KeyringHelper => shape.Ecosystem
                == CredentialEcosystem.Python
                && shape.Audience == TokenAudience.AzureArtifacts
                && shape.CredentialKind == CredentialKind.BasicPassword
                && shape.HasFeed
                && !shape.HasRepository,
            AdapterProtocol.NpmConfiguration => shape.Ecosystem
                is CredentialEcosystem.Npm
                    or CredentialEcosystem.Pnpm
                    or CredentialEcosystem.Yarn
                && shape.Audience == TokenAudience.AzureArtifacts
                && shape.CredentialKind == CredentialKind.NpmAuthToken
                && shape.HasFeed
                && !shape.HasRepository,
            _ => false,
        };
    }

    private static bool WritesProtocolStdoutOnSuccess(
        AdapterProtocol protocol,
        CredentialOperation operation
    ) =>
        protocol is AdapterProtocol.GitCredentialHelper
            ? operation == CredentialOperation.Get
            : protocol is AdapterProtocol.NuGetPlugin or AdapterProtocol.KeyringHelper;

    private static bool ContainsCredentialMaterial(CredentialResult result) =>
        !string.IsNullOrEmpty(result.Username)
        || !string.IsNullOrEmpty(result.Password)
        || !string.IsNullOrEmpty(result.BearerToken);

    private static bool HasBasicCredentialSuccessMaterial(string? username, string? password) =>
        !string.IsNullOrEmpty(username)
        && !string.IsNullOrEmpty(password)
        && !ContainsBasicCredentialProtocolLineBreak(username)
        && !ContainsBasicCredentialProtocolLineBreak(password);

    private static bool ContainsBasicCredentialProtocolLineBreak(string value) =>
        value.AsSpan().IndexOfAny('\r', '\n') >= 0;
}

public sealed record KeyringHelperRequest : IJsonOnDeserialized
{
    [JsonRequired]
    public int ContractMajor { get; init; } = ContractVersions.KeyringHelperMajor;
    public required string Command { get; init; }
    public required Uri Service { get; init; }
    public string? Username { get; init; }
    public required KeyringHelperMode Mode { get; init; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (ContractMajor != ContractVersions.KeyringHelperMajor)
        {
            throw new ArgumentException(
                "Protocol violation: keyring helper contract major must be 2.",
                nameof(ContractMajor)
            );
        }
    }
}

public sealed record KeyringHelperResponse : IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public int ContractMajor { get; init; } = ContractVersions.KeyringHelperMajor;
    public required AdapterHostExitCode ExitCode { get; init; }
    public required string Stdout { get; init; }
    public required string Stderr { get; init; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {{ {1} = {2}, {3} = {4}, {5} = <redacted>, {6} = <redacted> }}",
            nameof(KeyringHelperResponse),
            nameof(ContractMajor),
            ContractMajor,
            nameof(ExitCode),
            ExitCode,
            nameof(Stdout),
            nameof(Stderr)
        );

    void IJsonOnDeserialized.OnDeserialized() => EnsureValid();

    void IJsonOnSerializing.OnSerializing() => EnsureValid();

    private void EnsureValid()
    {
        if (ContractMajor != ContractVersions.KeyringHelperMajor)
        {
            throw new ArgumentException(
                "Protocol violation: keyring helper response contract major must be 2.",
                nameof(ContractMajor)
            );
        }

        ArgumentNullException.ThrowIfNull(Stdout);
        ArgumentNullException.ThrowIfNull(Stderr);

        if (
            ExitCode is AdapterHostExitCode.Success or AdapterHostExitCode.NoCredential
            && Stderr.Length != 0
        )
        {
            throw new ArgumentException(
                "Protocol violation: keyring helper success/no-credential response stderr must be "
                    + "empty.",
                nameof(Stderr)
            );
        }

        if (ExitCode != AdapterHostExitCode.Success && Stdout.Length != 0)
        {
            throw new ArgumentException(
                "Protocol violation: keyring helper failure response stdout must be empty.",
                nameof(Stdout)
            );
        }

        if (ExitCode == AdapterHostExitCode.Success)
        {
            if (Stdout.Contains('\r'))
            {
                throw new ArgumentException(
                    "Protocol violation: keyring helper success response stdout must not contain "
                        + "CR.",
                    nameof(Stdout)
                );
            }

            if (!Stdout.EndsWith('\n'))
            {
                throw new ArgumentException(
                    "Protocol violation: keyring helper success response stdout must be LF-"
                        + "terminated.",
                    nameof(Stdout)
                );
            }

            string[] records = Stdout.Split('\n');
            if (records.Length is not 2 and not 3 || records[^1].Length != 0)
            {
                throw new ArgumentException(
                    "Protocol violation: keyring helper success response stdout must contain one "
                        + "or two LF-terminated records.",
                    nameof(Stdout)
                );
            }

            for (int i = 0; i < records.Length - 1; i++)
            {
                if (records[i].Length == 0)
                {
                    throw new ArgumentException(
                        "Protocol violation: keyring helper success response stdout records must "
                            + "be non-empty.",
                        nameof(Stdout)
                    );
                }
            }
        }
    }
}

public sealed record KeyringHelperIntegrityContract : IJsonOnDeserialized
{
    [JsonRequired]
    public int ContractMajor { get; init; } = ContractVersions.KeyringHelperMajor;
    public required string ProductId { get; init; }
    public required string AbsoluteHelperPath { get; init; }
    public required string Sha256 { get; init; }

    [JsonRequired]
    public KeyringHelperIntegrityPlatform Platform { get; init; } =
        KeyringHelperIntegrityPlatform.Unspecified;

    [JsonRequired]
    public KeyringOwnerValidationRequirement OwnerValidation { get; init; } =
        KeyringOwnerValidationRequirement.Required;

    [JsonRequired]
    public KeyringSymlinkPolicy SymlinkPolicy { get; init; } = KeyringSymlinkPolicy.RejectSymlinks;

    [JsonRequired]
    public KeyringDigestPolicy DigestPolicy { get; init; } = KeyringDigestPolicy.Sha256Required;

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (ContractMajor != ContractVersions.KeyringHelperMajor)
        {
            throw new ArgumentException(
                "Protocol violation: keyring helper integrity contract major must be 2.",
                nameof(ContractMajor)
            );
        }
    }
}

public static class KeyringHelperIntegrityPolicy
{
    /// <summary>
    /// Validates helper integrity contract policy for the current trusted runtime platform.
    /// This does not inspect the filesystem and is not sufficient before helper execution.
    /// </summary>
    public static void EnsureContractPolicyValid(KeyringHelperIntegrityContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        EnsureContractPolicyValid(contract, GetTrustedRuntimePlatform());
    }

    /// <summary>
    /// Validates helper integrity contract policy for a trusted runtime platform.
    /// This does not inspect the filesystem and is not sufficient before helper execution.
    /// </summary>
    public static void EnsureContractPolicyValid(
        KeyringHelperIntegrityContract contract,
        KeyringHelperIntegrityPlatform trustedRuntimePlatform
    )
    {
        ArgumentNullException.ThrowIfNull(contract);

        string? violation = GetContractPolicyViolation(contract, trustedRuntimePlatform);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(contract));
        }
    }

    /// <summary>
    /// Performs structural-only validation of the self-declared helper integrity metadata.
    /// Path syntax is checked with the declared platform's rules.
    /// This is not sufficient before helper execution because it does not inspect the
    /// filesystem or bind the policy to a trusted runtime platform.
    /// </summary>
    public static void EnsureStructurallyValid(KeyringHelperIntegrityContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        string? violation = GetStructuralViolation(contract);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(contract));
        }
    }

    public static void EnsureContractPolicyValidForCurrentRuntime(
        KeyringHelperIntegrityContract contract
    )
    {
        ArgumentNullException.ThrowIfNull(contract);

        EnsureContractPolicyValid(contract);
    }

    /// <summary>
    /// Returns whether helper integrity contract policy is valid for the current trusted runtime
    /// platform. This does not inspect the filesystem and is not sufficient before helper
    /// execution.
    /// </summary>
    public static bool IsContractPolicyValid(KeyringHelperIntegrityContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return IsContractPolicyValid(contract, GetTrustedRuntimePlatform());
    }

    /// <summary>
    /// Returns whether helper integrity contract policy is valid for a trusted runtime platform.
    /// This does not inspect the filesystem and is not sufficient before helper execution.
    /// </summary>
    public static bool IsContractPolicyValid(
        KeyringHelperIntegrityContract contract,
        KeyringHelperIntegrityPlatform trustedRuntimePlatform
    )
    {
        ArgumentNullException.ThrowIfNull(contract);
        return GetContractPolicyViolation(contract, trustedRuntimePlatform) is null;
    }

    /// <summary>
    /// Returns whether the self-declared helper integrity metadata is structurally valid.
    /// Path syntax is checked with the declared platform's rules.
    /// This is not sufficient before helper execution because it does not inspect the
    /// filesystem or bind the policy to a trusted runtime platform.
    /// </summary>
    public static bool IsStructurallyValid(KeyringHelperIntegrityContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return GetStructuralViolation(contract) is null;
    }

    public static bool IsContractPolicyValidForCurrentRuntime(
        KeyringHelperIntegrityContract contract
    )
    {
        ArgumentNullException.ThrowIfNull(contract);

        return IsContractPolicyValid(contract);
    }

    /// <summary>
    /// Gets the helper integrity contract policy violation for the current trusted runtime
    /// platform.
    /// This does not inspect the filesystem and is not sufficient before helper execution.
    /// </summary>
    public static string? GetContractPolicyViolation(KeyringHelperIntegrityContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return GetContractPolicyViolation(contract, GetTrustedRuntimePlatform());
    }

    /// <summary>
    /// Gets the structural-only validation violation for self-declared helper integrity metadata.
    /// Path syntax is checked with the declared platform's rules.
    /// This is not sufficient before helper execution because it does not inspect the
    /// filesystem or bind the policy to a trusted runtime platform.
    /// </summary>
    public static string? GetStructuralViolation(KeyringHelperIntegrityContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        if (contract.ContractMajor != ContractVersions.KeyringHelperMajor)
        {
            return "Protocol violation: keyring helper integrity contract major must be 2.";
        }

        if (string.IsNullOrWhiteSpace(contract.ProductId))
        {
            return "Protocol violation: keyring helper integrity product ID is required.";
        }

        if (string.IsNullOrWhiteSpace(contract.AbsoluteHelperPath))
        {
            return "Protocol violation: keyring helper path must be absolute.";
        }

        string? pathViolation = GetAbsoluteHelperPathSyntaxViolation(
            contract.AbsoluteHelperPath,
            contract.Platform
        );
        if (pathViolation is not null)
        {
            return pathViolation;
        }

        if (!IsSha256Hex(contract.Sha256))
        {
            return "Protocol violation: keyring helper SHA-256 digest is required.";
        }

        return contract.Platform switch
        {
            KeyringHelperIntegrityPlatform.Linux when IsStrongLinuxPolicy(contract) => null,
            KeyringHelperIntegrityPlatform.Windows
            or KeyringHelperIntegrityPlatform.MacOs when IsWeakWindowsMacOsPolicy(contract) => null,
            KeyringHelperIntegrityPlatform.Linux =>
                "Protocol violation: Linux keyring helper integrity policy must be the explicit "
                    + "strong policy.",
            KeyringHelperIntegrityPlatform.Windows or KeyringHelperIntegrityPlatform.MacOs =>
                "Protocol violation: Windows/macOS keyring helper integrity policy must be the "
                    + "explicit weak policy.",
            _ => "Protocol violation: keyring helper integrity platform must be explicit and "
                + "supported.",
        };
    }

    public static string? GetContractPolicyViolation(
        KeyringHelperIntegrityContract contract,
        KeyringHelperIntegrityPlatform trustedRuntimePlatform
    )
    {
        ArgumentNullException.ThrowIfNull(contract);

        string? structuralViolation = GetStructuralViolation(contract);
        if (structuralViolation is not null)
        {
            return structuralViolation;
        }

        if (!IsSupportedPlatform(trustedRuntimePlatform))
        {
            return "Protocol violation: trusted runtime platform must be Windows, macOS, or Linux.";
        }

        if (contract.Platform != trustedRuntimePlatform)
        {
            return "Protocol violation: keyring helper integrity platform must match the trusted "
                + "runtime platform.";
        }

        return GetAbsoluteHelperPathSyntaxViolation(
            contract.AbsoluteHelperPath,
            trustedRuntimePlatform
        );
    }

    public static KeyringHelperIntegrityPlatform GetTrustedRuntimePlatform()
    {
        if (OperatingSystem.IsLinux())
        {
            return KeyringHelperIntegrityPlatform.Linux;
        }

        if (OperatingSystem.IsWindows())
        {
            return KeyringHelperIntegrityPlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return KeyringHelperIntegrityPlatform.MacOs;
        }

        return KeyringHelperIntegrityPlatform.Unspecified;
    }

    private static bool IsStrongLinuxPolicy(KeyringHelperIntegrityContract contract) =>
        contract.OwnerValidation == KeyringOwnerValidationRequirement.Required
        && contract.SymlinkPolicy == KeyringSymlinkPolicy.RejectSymlinks
        && contract.DigestPolicy == KeyringDigestPolicy.Sha256Required;

    private static bool IsWeakWindowsMacOsPolicy(KeyringHelperIntegrityContract contract) =>
        contract.OwnerValidation == KeyringOwnerValidationRequirement.DeferredNotAvailable
        && contract.SymlinkPolicy == KeyringSymlinkPolicy.BestEffortRejectSymlinks
        && contract.DigestPolicy == KeyringDigestPolicy.Sha256RequiredWeakPath;

    private static bool IsSupportedPlatform(KeyringHelperIntegrityPlatform platform) =>
        platform
            is KeyringHelperIntegrityPlatform.Linux
                or KeyringHelperIntegrityPlatform.Windows
                or KeyringHelperIntegrityPlatform.MacOs;

    private static string? GetAbsoluteHelperPathSyntaxViolation(
        string absoluteHelperPath,
        KeyringHelperIntegrityPlatform platform
    )
    {
        if (!IsSupportedPlatform(platform))
        {
            return null;
        }

        bool isAbsolute =
            platform == KeyringHelperIntegrityPlatform.Windows
                ? IsWindowsFullyQualifiedPath(absoluteHelperPath)
                : IsPosixAbsolutePath(absoluteHelperPath);
        if (!isAbsolute)
        {
            return "Protocol violation: keyring helper path must be absolute for the declared or "
                + "trusted platform.";
        }

        bool containsUnsafeComponent =
            platform == KeyringHelperIntegrityPlatform.Windows
                ? WindowsPathContainsUnsafeDirectoryComponent(absoluteHelperPath)
                : PosixPathContainsCurrentOrParentDirectoryComponent(absoluteHelperPath);
        return containsUnsafeComponent
            ? "Protocol violation: keyring helper path must not contain '.' or '..' path "
                + "components "
                + "or Windows path components with trailing spaces or periods."
            : null;
    }

    private static bool IsPosixAbsolutePath(string path) => path.Length > 0 && path[0] == '/';

    private static bool PosixPathContainsCurrentOrParentDirectoryComponent(string path) =>
        PathContainsCurrentOrParentDirectoryComponent(path, IsPosixDirectorySeparator);

    private static bool IsWindowsFullyQualifiedPath(string path) =>
        IsWindowsDriveFullyQualifiedPath(path) || IsWindowsUncFullyQualifiedPath(path);

    private static bool WindowsPathContainsUnsafeDirectoryComponent(string path) =>
        PathContainsDirectoryComponent(
            path,
            IsWindowsDirectorySeparator,
            IsUnsafeWindowsPathComponent
        );

    private static bool PathContainsCurrentOrParentDirectoryComponent(
        string path,
        Func<char, bool> isDirectorySeparator
    ) =>
        PathContainsDirectoryComponent(
            path,
            isDirectorySeparator,
            IsCurrentOrParentDirectoryComponent
        );

    private static bool PathContainsDirectoryComponent(
        string path,
        Func<char, bool> isDirectorySeparator,
        Func<string, int, int, bool> isUnsafeComponent
    )
    {
        var componentStart = 0;
        for (var index = 0; index <= path.Length; index++)
        {
            if (index < path.Length && !isDirectorySeparator(path[index]))
            {
                continue;
            }

            var componentLength = index - componentStart;
            if (isUnsafeComponent(path, componentStart, componentLength))
            {
                return true;
            }

            componentStart = index + 1;
        }

        return false;
    }

    private static bool IsUnsafeWindowsPathComponent(
        string path,
        int componentStart,
        int componentLength
    )
    {
        if (componentLength == 0)
        {
            return false;
        }

        if (IsCurrentOrParentDirectoryComponent(path, componentStart, componentLength))
        {
            return true;
        }

        char lastCharacter = path[componentStart + componentLength - 1];
        return lastCharacter is ' ' or '.';
    }

    private static bool IsCurrentOrParentDirectoryComponent(
        string path,
        int componentStart,
        int componentLength
    ) =>
        componentLength == 1 && path[componentStart] == '.'
        || componentLength == 2 && path[componentStart] == '.' && path[componentStart + 1] == '.';

    private static bool IsWindowsDriveFullyQualifiedPath(string path) =>
        path.Length >= 3
        && IsAsciiLetter(path[0])
        && path[1] == ':'
        && IsWindowsDirectorySeparator(path[2]);

    private static bool IsWindowsUncFullyQualifiedPath(string path)
    {
        if (path.Length < 5 || !IsWindowsDirectorySeparator(path[0]) || path[0] != path[1])
        {
            return false;
        }

        int serverStart = 2;
        int serverEnd = IndexOfWindowsDirectorySeparator(path, serverStart);
        if (serverEnd <= serverStart)
        {
            return false;
        }

        int shareStart = serverEnd + 1;
        int shareEnd = IndexOfWindowsDirectorySeparator(path, shareStart);
        return shareEnd > shareStart || shareEnd < 0 && shareStart < path.Length;
    }

    private static int IndexOfWindowsDirectorySeparator(string path, int startIndex)
    {
        for (int index = startIndex; index < path.Length; index++)
        {
            if (IsWindowsDirectorySeparator(path[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsPosixDirectorySeparator(char value) => value == '/';

    private static bool IsWindowsDirectorySeparator(char value) => value is '\\' or '/';

    private static bool IsSha256Hex(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}

public static class KeyringHelperV2
{
    public const string CommandName = "python-keyring";
    public const string GetVerb = "get";

    public static IReadOnlyList<string> BuildArguments(KeyringHelperRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureValidRequest(request);

        var arguments = new List<string>
        {
            CommandName,
            GetVerb,
            "--protocol-version",
            ContractVersions.KeyringHelperMajor.ToString(CultureInfo.InvariantCulture),
            "--service",
            request.Service.AbsoluteUri,
        };

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            arguments.Add("--username");
            arguments.Add(request.Username);
        }

        arguments.Add("--mode");
        arguments.Add(ToModeArgument(request.Mode));
        return arguments;
    }

    public static KeyringHelperResponse ToResponse(
        KeyringHelperRequest request,
        CredentialResult result
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        string? requestViolation = GetRequestViolation(request);
        if (requestViolation is not null)
        {
            return new KeyringHelperResponse
            {
                ExitCode = AdapterHostExitCode.ConfigurationError,
                Stdout = string.Empty,
                Stderr = requestViolation,
            };
        }

        var mapped = AdapterHostResultMapper.Map(AdapterProtocol.KeyringHelper, result);
        if (mapped.ExitCode != AdapterHostExitCode.Success || !mapped.WriteProtocolStdout)
        {
            return new KeyringHelperResponse
            {
                ExitCode = mapped.ExitCode,
                Stdout = string.Empty,
                Stderr = mapped.WriteDiagnosticStderr
                    ? GetDiagnosticStderr(mapped, result)
                    : string.Empty,
            };
        }

        string? password = result.Password;
        string? username = result.Username ?? request.Username;
        if (
            string.IsNullOrEmpty(password)
            || (request.Mode == KeyringHelperMode.Credentials && string.IsNullOrEmpty(username))
        )
        {
            return new KeyringHelperResponse
            {
                ExitCode = AdapterHostExitCode.ConfigurationError,
                Stdout = string.Empty,
                Stderr =
                    "Protocol violation: success response does not contain required credential "
                    + "material.",
            };
        }

        if (
            ContainsLineBreak(password)
            || (
                request.Mode == KeyringHelperMode.Credentials
                && username is not null
                && ContainsLineBreak(username)
            )
        )
        {
            return new KeyringHelperResponse
            {
                ExitCode = AdapterHostExitCode.ConfigurationError,
                Stdout = string.Empty,
                Stderr =
                    "Protocol violation: success response credential fields must not contain CR or "
                    + "LF.",
            };
        }

        return new KeyringHelperResponse
        {
            ExitCode = AdapterHostExitCode.Success,
            Stdout =
                request.Mode == KeyringHelperMode.Credentials
                    ? string.Concat(username, "\n", password, "\n")
                    : string.Concat(password, "\n"),
            Stderr = string.Empty,
        };
    }

    private static bool ContainsLineBreak(string value) =>
        value.Contains('\r') || value.Contains('\n');

    private static string GetDiagnosticStderr(AdapterHostResult mapped, CredentialResult result)
    {
        if (IsMapperOwnedValidationDiagnostic(mapped.SafeDiagnosticCode, result))
        {
            return mapped.SafeDiagnosticCode!;
        }

        return result.Error?.SafeMessage ?? mapped.SafeDiagnosticCode ?? result.Status.ToString();
    }

    private static bool IsMapperOwnedValidationDiagnostic(
        string? safeDiagnosticCode,
        CredentialResult result
    ) =>
        safeDiagnosticCode switch
        {
            "UnsupportedAdapterProtocol" => true,
            "UnsupportedContractMajor" => result.ContractMajor
                != ContractVersions.CredentialContractMajor,
            "UnsupportedCacheKeySchemaMajor" => result.CacheKey is not null
                && !CacheKeySchema.IsValid(result.CacheKey),
            "ProtocolViolation" => result.Status == CredentialResultStatus.Success
                && result.Error is not null,
            _ => false,
        };

    private static string ToModeArgument(KeyringHelperMode mode) =>
        mode switch
        {
            KeyringHelperMode.Password => "password",
            KeyringHelperMode.Credentials => "creds",
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unknown keyring helper mode is invalid."
            ),
        };

    private static void EnsureValidRequest(KeyringHelperRequest request)
    {
        string? violation = GetRequestViolation(request);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(request));
        }
    }

    private static string? GetRequestViolation(KeyringHelperRequest request)
    {
        if (request.ContractMajor != ContractVersions.KeyringHelperMajor)
        {
            return "Protocol violation: keyring helper contract major must be 2.";
        }

        if (!StringComparer.Ordinal.Equals(request.Command, CommandName))
        {
            return "Protocol violation: keyring helper command must be python-keyring.";
        }

        if (request.Service is null)
        {
            return "Protocol violation: keyring helper service must be a non-null absolute URI "
                + "without user info, query, or fragment.";
        }

        string? serviceViolation = GetKeyringServiceEndpointViolation(request.Service);
        if (serviceViolation is not null)
        {
            return serviceViolation;
        }

        return request.Mode switch
        {
            KeyringHelperMode.Password or KeyringHelperMode.Credentials => null,
            _ => "Protocol violation: keyring helper mode must be password or creds.",
        };
    }

    private static string? GetKeyringServiceEndpointViolation(Uri service)
    {
        if (!service.IsAbsoluteUri)
        {
            return "Protocol violation: keyring helper service must be absolute.";
        }

        if (!string.Equals(service.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return "Protocol violation: keyring helper service must use HTTPS.";
        }

        if (!service.IsDefaultPort)
        {
            return "Protocol violation: keyring helper service must use the default HTTPS port.";
        }

        if (
            UriSecurityPolicy.HasUserInfoDelimiter(service)
            || !string.IsNullOrEmpty(service.Query)
            || !string.IsNullOrEmpty(service.Fragment)
            || service.AbsoluteUri.Contains('?', StringComparison.Ordinal)
            || service.AbsoluteUri.Contains('#', StringComparison.Ordinal)
        )
        {
            return "Protocol violation: keyring helper service must not include user info, query, "
                + "or fragment.";
        }

        string host = service.IdnHost;
        string? legacyHostOrganization = TryGetLegacyPackagingOrganization(host);
        if (
            !string.Equals(host, "pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase)
            && legacyHostOrganization is null
        )
        {
            return "Protocol violation: keyring helper service host must be a supported Azure "
                + "Artifacts host.";
        }

        if (IsReservedIdentityComponent(legacyHostOrganization))
        {
            return "Protocol violation: keyring helper service identity components must not use "
                + "reserved resource marker names.";
        }

        string[] segments;
        try
        {
            segments = GetPathSegments(service);
        }
        catch (UriFormatException)
        {
            return "Protocol violation: keyring helper service path must be a well-formed Azure "
                + "Artifacts Python feed endpoint.";
        }

        if (!HasPythonFeedEndpointShape(segments, legacyHostOrganization is not null))
        {
            return "Protocol violation: keyring helper service path must be an Azure Artifacts "
                + "Python feed endpoint ending in _packaging/{feed}/pypi/simple.";
        }

        if (legacyHostOrganization is not null && string.IsNullOrWhiteSpace(legacyHostOrganization))
        {
            return "Protocol violation: keyring helper service organization is required.";
        }

        return null;
    }

    private static bool HasPythonFeedEndpointShape(string[] segments, bool legacyOrganizationInHost)
    {
        if (segments.Length > 0 && segments[^1].Length == 0)
        {
            segments = segments[..^1];
        }

        if (segments.Length == 0 || segments.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        string[] resourceSegments = legacyOrganizationInHost
            ? segments.Length > 0 && IsSegment(segments[0], "DefaultCollection")
                ? segments[1..]
                : segments
            : segments;

        if (!legacyOrganizationInHost)
        {
            if (
                resourceSegments.Length is not (5 or 6)
                || string.IsNullOrWhiteSpace(resourceSegments[0])
                || IsReservedIdentityComponent(resourceSegments[0])
            )
            {
                return false;
            }

            resourceSegments = resourceSegments[1..];
        }

        return resourceSegments.Length switch
        {
            4 => IsPythonFeedEndpointTail(resourceSegments, packagingIndex: 0),
            5 => !string.IsNullOrWhiteSpace(resourceSegments[0])
                && IsPythonFeedEndpointTail(resourceSegments, packagingIndex: 1),
            _ => false,
        };
    }

    private static bool IsPythonFeedEndpointTail(string[] segments, int packagingIndex) =>
        IsSegment(segments[packagingIndex], "_packaging")
        && !string.IsNullOrWhiteSpace(segments[packagingIndex + 1])
        && !IsReservedIdentityComponent(segments[packagingIndex + 1])
        && (packagingIndex == 0 || !IsReservedIdentityComponent(segments[0]))
        && IsSegment(segments[packagingIndex + 2], "pypi")
        && IsSegment(segments[packagingIndex + 3], "simple");

    private static string[] GetPathSegments(Uri uri)
    {
        string path = uri.AbsolutePath.StartsWith('/') ? uri.AbsolutePath[1..] : uri.AbsolutePath;
        return path.Length == 0
            ? []
            : path.Split('/', StringSplitOptions.None).Select(DecodePathSegmentOrThrow).ToArray();
    }

    private static string DecodePathSegmentOrThrow(string segment)
    {
        string decoded = Uri.UnescapeDataString(segment);
        if (
            decoded.Contains('/', StringComparison.Ordinal)
            || decoded.Contains('\\', StringComparison.Ordinal)
        )
        {
            throw new UriFormatException(
                "Path segment must not contain encoded or decoded path separators."
            );
        }

        return decoded;
    }

    private static bool IsSegment(string segment, string expected) =>
        string.Equals(segment, expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsReservedIdentityComponent(string? component) =>
        string.Equals(component, "_git", StringComparison.OrdinalIgnoreCase)
        || string.Equals(component, "_packaging", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetLegacyPackagingOrganization(string host)
    {
        const string packagingSuffix = ".pkgs.visualstudio.com";

        if (
            !host.EndsWith(packagingSuffix, StringComparison.OrdinalIgnoreCase)
            || host.Length <= packagingSuffix.Length
        )
        {
            return null;
        }

        string organization = host[..^packagingSuffix.Length];
        return
            !string.IsNullOrWhiteSpace(organization)
            && !organization.Contains('.', StringComparison.Ordinal)
            ? organization
            : null;
    }
}

public static class IdentityFlowPolicy
{
    public static IdentityFlowState GetMvpState(IdentityFlow flow) =>
        flow switch
        {
            IdentityFlow.InteractiveBrowser => IdentityFlowState.AcceptedMvp,
            IdentityFlow.DeviceCode => IdentityFlowState.AcceptedMvp,
            IdentityFlow.PatCompatibility => IdentityFlowState.AcceptedMvp,
            IdentityFlow.AzurePipelinesSystemAccessToken => IdentityFlowState.AcceptedMvp,
            IdentityFlow.ServicePrincipal => IdentityFlowState.Deferred,
            IdentityFlow.ManagedIdentity => IdentityFlowState.Deferred,
            IdentityFlow.WorkloadIdentityFederation => IdentityFlowState.Deferred,
            _ => IdentityFlowState.Unsupported,
        };

    public static bool CanUsePatCompatibility(CredentialRequest request) =>
        request.IdentityFlow == IdentityFlow.PatCompatibility
        && request.CredentialKind == CredentialKind.PatCompatibility
        && IsAcceptedMvpRequest(request);

    public static bool IsSilentFallbackAllowed(
        IdentityFlow requestedFlow,
        IdentityFlow fallbackFlow
    )
    {
        _ = requestedFlow;
        _ = fallbackFlow;
        return false;
    }

    public static bool IsAcceptedMvpRequest(CredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return HasKnownRequiredRequestEnums(request)
            && request.Resource is not null
            && ServiceIdentityContract.IsCanonical(request.ServiceIdentity)
            && CanonicalResourceIdentityPolicy.IsValid(request.Resource)
            && IsResourceShapeAllowed(request)
            && GetMvpState(request.IdentityFlow) == IdentityFlowState.AcceptedMvp
            && (
                request.IdentityFlow != IdentityFlow.PatCompatibility
                || request.CredentialKind == CredentialKind.PatCompatibility
            )
            && (
                request.IdentityFlow == IdentityFlow.PatCompatibility
                || request.CredentialKind != CredentialKind.PatCompatibility
            )
            && (
                request.IdentityFlow != IdentityFlow.AzurePipelinesSystemAccessToken
                || request.Ecosystem != CredentialEcosystem.Git
                || request.CredentialKind == CredentialKind.BearerToken
            )
            && IsInteractivePolicyAllowed(request)
            && IsCiPolicyAllowed(request);
    }

    private static bool IsInteractivePolicyAllowed(CredentialRequest request) =>
        request.IdentityFlow is not (IdentityFlow.InteractiveBrowser or IdentityFlow.DeviceCode)
        || request.InteractivePolicy
            is InteractivePolicy.HostToolAllows
                or InteractivePolicy.UserAllowed;

    private static bool IsCiPolicyAllowed(CredentialRequest request)
    {
        if (request.CiContext is { AllowsPersistentWrites: true })
        {
            return false;
        }

        if (request.CiContext is { ExplicitCiMode: true } ciContext)
        {
            return request.IdentityFlow == IdentityFlow.AzurePipelinesSystemAccessToken
                && string.Equals(
                    ciContext.Provider,
                    CiProviderNames.AzurePipelines,
                    StringComparison.Ordinal
                )
                && ciContext.HasAzurePipelinesSystemAccessToken
                && request.CachePolicy == CachePolicyMode.NonPersistentCi;
        }

        return request.IdentityFlow != IdentityFlow.AzurePipelinesSystemAccessToken;
    }

    private static bool IsResourceShapeAllowed(CredentialRequest request) =>
        request.Ecosystem switch
        {
            CredentialEcosystem.Git => string.IsNullOrWhiteSpace(request.Resource.Feed)
                && request.RequestedAudience == TokenAudience.AzureDevOps
                && request.CredentialKind
                    is CredentialKind.BasicPassword
                        or CredentialKind.BearerToken
                        or CredentialKind.PatCompatibility
                && CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                    request.Resource.ServiceEndpoint,
                    request.Ecosystem
                ),
            CredentialEcosystem.NuGet => IsPackageResourceShapeAllowed(
                request,
                CredentialKind.NuGetPluginCredential
            ),
            CredentialEcosystem.Python => IsPackageResourceShapeAllowed(
                request,
                CredentialKind.BasicPassword
            ),
            CredentialEcosystem.Npm or CredentialEcosystem.Pnpm or CredentialEcosystem.Yarn =>
                IsPackageResourceShapeAllowed(request, CredentialKind.NpmAuthToken),
            _ => false,
        };

    private static bool IsPackageResourceShapeAllowed(
        CredentialRequest request,
        CredentialKind expectedCredentialKind
    ) =>
        !string.IsNullOrWhiteSpace(request.Resource.Feed)
        && string.IsNullOrWhiteSpace(request.Resource.Repository)
        && request.RequestedAudience == TokenAudience.AzureArtifacts
        && request.CredentialKind == expectedCredentialKind
        && CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
            request.Resource.ServiceEndpoint,
            request.Ecosystem
        );

    private static bool HasKnownRequiredRequestEnums(CredentialRequest request) =>
        request.ContractMajor == ContractVersions.CredentialContractMajor
        && request.Ecosystem
            is CredentialEcosystem.Git
                or CredentialEcosystem.NuGet
                or CredentialEcosystem.Python
                or CredentialEcosystem.Npm
                or CredentialEcosystem.Pnpm
                or CredentialEcosystem.Yarn
        && request.Operation
            is CredentialOperation.Get
                or CredentialOperation.Store
                or CredentialOperation.Erase
                or CredentialOperation.Refresh
                or CredentialOperation.Configure
                or CredentialOperation.Doctor
        && request.RequestedAudience is TokenAudience.AzureDevOps or TokenAudience.AzureArtifacts
        && request.CredentialKind
            is CredentialKind.BasicPassword
                or CredentialKind.BearerToken
                or CredentialKind.NpmAuthToken
                or CredentialKind.NuGetPluginCredential
                or CredentialKind.PatCompatibility
        && request.IdentityFlow
            is IdentityFlow.InteractiveBrowser
                or IdentityFlow.DeviceCode
                or IdentityFlow.PatCompatibility
                or IdentityFlow.AzurePipelinesSystemAccessToken
                or IdentityFlow.ServicePrincipal
                or IdentityFlow.ManagedIdentity
                or IdentityFlow.WorkloadIdentityFederation
        && request.InteractivePolicy
            is InteractivePolicy.Never
                or InteractivePolicy.HostToolAllows
                or InteractivePolicy.UserAllowed
        && request.CachePolicy
            is CachePolicyMode.NoCache
                or CachePolicyMode.ProductPersistentCacheDisabled
                or CachePolicyMode.NonPersistentCi;
}

internal static class ServiceIdentityContract
{
    public static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal);
}

public static class ContractCompatibility
{
    public static bool IsSupportedMajor(int actualMajor, int supportedMajor) =>
        actualMajor == supportedMajor;

    public static bool AllowsAdditiveField(int actualMajor, int supportedMajor, string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        return IsSupportedMajor(actualMajor, supportedMajor);
    }

    public static bool RequiresMajorVersionChange(string changeKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(changeKind);
        return changeKind switch
        {
            "remove-field"
            or "rename-field"
            or "change-field-type"
            or "change-field-requiredness"
            or "change-field-meaning"
            or "change-meaning"
            or "change-enum-representation"
            or "change-protocol-stdout"
            or "change-required-stdout"
            or "change-protocol-stderr"
            or "change-required-stderr"
            or "change-protocol-exit-code"
            or "change-exit-code"
            or "weaken-security-policy"
            or "weaken-cache-partitioning"
            or "allow-plaintext-secret-diagnostics"
            or "add-silent-pat-fallback"
            or "make-integrity-check-optional" => true,
            "add-optional-field" => false,
            _ => true,
        };
    }

    public static bool RequiresMajorVersionChange(ContractBreakingChangeKind changeKind) =>
        changeKind switch
        {
            ContractBreakingChangeKind.Unspecified
            or ContractBreakingChangeKind.RemoveField
            or ContractBreakingChangeKind.RenameField
            or ContractBreakingChangeKind.ChangeFieldType
            or ContractBreakingChangeKind.ChangeFieldRequiredness
            or ContractBreakingChangeKind.ChangeFieldMeaning
            or ContractBreakingChangeKind.ChangeEnumRepresentation
            or ContractBreakingChangeKind.ChangeProtocolStdout
            or ContractBreakingChangeKind.ChangeProtocolStderr
            or ContractBreakingChangeKind.ChangeProtocolExitCode
            or ContractBreakingChangeKind.WeakenSecurityPolicy
            or ContractBreakingChangeKind.WeakenCachePartitioning
            or ContractBreakingChangeKind.AllowPlaintextSecretDiagnostics
            or ContractBreakingChangeKind.AddSilentPatFallback
            or ContractBreakingChangeKind.MakeIntegrityCheckOptional => true,
            _ => true,
        };
}

public static class ContractMetadata
{
    public static readonly IReadOnlyDictionary<string, string> Empty = new ReadOnlyDictionary<
        string,
        string
    >(new Dictionary<string, string>());
}
