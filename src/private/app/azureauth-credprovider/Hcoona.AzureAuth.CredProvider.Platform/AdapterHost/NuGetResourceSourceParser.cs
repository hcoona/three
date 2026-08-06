using System.Diagnostics.CodeAnalysis;
using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

internal static class NuGetResourceSourceParser
{
    internal static NuGetResourceParseResult Parse(Uri? uri)
    {
        if (uri is null)
        {
            return NuGetResourceParseResult.ProtocolViolation();
        }

        if (!IsAzureArtifactsHost(uri.IdnHost))
        {
            return NuGetResourceParseResult.NoCredential();
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return NuGetResourceParseResult.ProtocolViolation();
        }

        if (!TryGetPathSegments(uri, out string[]? segments))
        {
            return NuGetResourceParseResult.ProtocolViolation();
        }

        if (
            !TryParseAzureArtifactsNuGetResource(
                uri.IdnHost,
                segments,
                out AzureArtifactsNuGetResourceShape? shape
            ) || shape is null
        )
        {
            return NuGetResourceParseResult.ProtocolViolation();
        }

        try
        {
            return NuGetResourceParseResult.Success(
                CanonicalResourceIdentity.Create(
                    uri.IdnHost,
                    shape.Organization,
                    uri,
                    shape.Project,
                    feed: shape.Feed
                )
            );
        }
        catch (ArgumentException)
        {
            return NuGetResourceParseResult.ProtocolViolation();
        }
    }

    private static bool IsAzureArtifactsHost(string host) =>
        string.Equals(host, "pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
        || TryGetLegacyVisualStudioOrganization(host, out _);

    private static bool TryParseAzureArtifactsNuGetResource(
        string host,
        string[] segments,
        [NotNullWhen(true)] out AzureArtifactsNuGetResourceShape? shape
    )
    {
        if (
            string.Equals(host, "pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
        )
        {
            if (segments.Length == 0 || string.IsNullOrWhiteSpace(segments[0]))
            {
                shape = null;
                return false;
            }

            shape = ParseNuGetResourceSegments(segments[0], segments.Skip(1).ToArray());
            return shape is not null;
        }

        if (!TryGetLegacyVisualStudioOrganization(host, out string? organization))
        {
            shape = null;
            return false;
        }

        string[] resourceSegments =
            segments.Length > 0 && IsSegment(segments[0], "DefaultCollection")
                ? segments[1..]
                : segments;
        shape = ParseNuGetResourceSegments(organization, resourceSegments);
        return shape is not null;
    }

    private static AzureArtifactsNuGetResourceShape? ParseNuGetResourceSegments(
        string organization,
        string[] resourceSegments
    )
    {
        if (
            resourceSegments.Length == 5
            && IsSegment(resourceSegments[0], "_packaging")
            && IsNuGetV3IndexSuffix(resourceSegments, 2)
        )
        {
            return new AzureArtifactsNuGetResourceShape(
                organization,
                Project: null,
                Feed: resourceSegments[1]
            );
        }

        if (
            resourceSegments.Length == 6
            && IsSegment(resourceSegments[1], "_packaging")
            && IsNuGetV3IndexSuffix(resourceSegments, 3)
        )
        {
            return new AzureArtifactsNuGetResourceShape(
                organization,
                Project: resourceSegments[0],
                Feed: resourceSegments[2]
            );
        }

        return null;
    }

    private static bool IsNuGetV3IndexSuffix(string[] segments, int startIndex) =>
        IsSegment(segments[startIndex], "nuget")
        && IsSegment(segments[startIndex + 1], "v3")
        && IsSegment(segments[startIndex + 2], "index.json");

    private static bool TryGetPathSegments(Uri uri, [NotNullWhen(true)] out string[]? segments)
    {
        string path = uri.AbsolutePath.StartsWith('/') ? uri.AbsolutePath[1..] : uri.AbsolutePath;
        if (path.Length == 0)
        {
            segments = [];
            return true;
        }

        var decodedSegments = new List<string>();
        foreach (string segment in path.Split('/', StringSplitOptions.None))
        {
            string decodedSegment;
            try
            {
                decodedSegment = Uri.UnescapeDataString(segment);
            }
            catch (UriFormatException)
            {
                segments = null;
                return false;
            }

            if (
                ContainsControlCharacters(decodedSegment)
                || decodedSegment.Contains('/', StringComparison.Ordinal)
                || decodedSegment.Contains('\\', StringComparison.Ordinal)
            )
            {
                segments = null;
                return false;
            }

            decodedSegments.Add(decodedSegment);
        }

        segments = decodedSegments.ToArray();
        return true;
    }

    private static bool TryGetLegacyVisualStudioOrganization(
        string host,
        [NotNullWhen(true)] out string? organization
    )
    {
        const string suffix = ".pkgs.visualstudio.com";
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            organization = null;
            return false;
        }

        organization = host[..^suffix.Length];
        return !string.IsNullOrWhiteSpace(organization);
    }

    private static bool IsSegment(string value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsControlCharacters(string? value) =>
        value is not null && value.Any(char.IsControl);

    private sealed record AzureArtifactsNuGetResourceShape(
        string Organization,
        string? Project,
        string Feed
    );
}

internal sealed record NuGetResourceParseResult(
    NuGetResourceParseStatus Status,
    CanonicalResourceIdentity? Resource
)
{
    internal static NuGetResourceParseResult Success(CanonicalResourceIdentity resource) =>
        new(NuGetResourceParseStatus.Success, resource);

    internal static NuGetResourceParseResult NoCredential() =>
        new(NuGetResourceParseStatus.NoCredential, Resource: null);

    internal static NuGetResourceParseResult ProtocolViolation() =>
        new(NuGetResourceParseStatus.ProtocolViolation, Resource: null);
}

internal enum NuGetResourceParseStatus
{
    Success,
    NoCredential,
    ProtocolViolation,
}
