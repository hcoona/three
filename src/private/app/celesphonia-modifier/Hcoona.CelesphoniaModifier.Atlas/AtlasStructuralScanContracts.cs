namespace Hcoona.CelesphoniaModifier.Atlas;

public enum AtlasDocumentRole
{
    GlobalSave,
    ConfigSave,
    SlotSave,
}

public enum AtlasStructuralLocatorSubject
{
    NodeOccurrence,
    ReferenceOccurrence,
    IdentityDefinition,
}

public abstract record AtlasStructuralLocatorSegment
{
    private protected AtlasStructuralLocatorSegment() { }
}

public sealed record AtlasOrdinaryMemberLocatorSegment(long Ordinal)
    : AtlasStructuralLocatorSegment;

public sealed record AtlasArrayElementLocatorSegment(long Index) : AtlasStructuralLocatorSegment;

public sealed class AtlasStructuralLocator : IEquatable<AtlasStructuralLocator>
{
    private readonly AtlasStructuralLocatorSegment[] segments;
    private readonly IReadOnlyList<AtlasStructuralLocatorSegment> readOnlySegments;

    public AtlasStructuralLocator(
        AtlasStructuralLocatorSubject subject,
        IEnumerable<AtlasStructuralLocatorSegment> segments
    )
    {
        ArgumentNullException.ThrowIfNull(segments);
        Subject = subject;
        this.segments = [.. segments];
        readOnlySegments = Array.AsReadOnly(this.segments);
    }

    public AtlasStructuralLocatorSubject Subject { get; }

    public IReadOnlyList<AtlasStructuralLocatorSegment> Segments => readOnlySegments;

    public bool Equals(AtlasStructuralLocator? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Subject != other.Subject || segments.Length != other.segments.Length)
        {
            return false;
        }

        for (int index = 0; index < segments.Length; index++)
        {
            if (segments[index] != other.segments[index])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is AtlasStructuralLocator other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Subject);
        foreach (AtlasStructuralLocatorSegment segment in segments)
        {
            hash.Add(segment);
        }

        return hash.ToHashCode();
    }
}

public enum AtlasStructuralScalarKind
{
    Text,
    Number,
    True,
    False,
    Null,
}

public enum AtlasStructuralObjectShape
{
    PlainObject,
    IdentityObject,
}

public enum AtlasStructuralArrayShape
{
    PlainArray,
    IdentityArrayWrapper,
}

public abstract class AtlasStructuralObservation
{
    private protected AtlasStructuralObservation(AtlasStructuralLocator locator)
    {
        Locator = locator ?? throw new ArgumentNullException(nameof(locator));
    }

    public AtlasStructuralLocator Locator { get; }
}

public sealed class AtlasStructuralScalarObservation(
    AtlasStructuralLocator locator,
    AtlasStructuralScalarKind scalarKind
) : AtlasStructuralObservation(locator)
{
    public AtlasStructuralScalarKind ScalarKind { get; } = scalarKind;
}

public sealed class AtlasStructuralObjectObservation(
    AtlasStructuralLocator locator,
    AtlasStructuralObjectShape shape,
    long childCount,
    bool classMarkerPresent,
    AtlasStructuralLocator? identityDefinitionLocator
) : AtlasStructuralObservation(locator)
{
    public AtlasStructuralObjectShape Shape { get; } = shape;

    public long ChildCount { get; } = childCount;

    public bool ClassMarkerPresent { get; } = classMarkerPresent;

    public bool IdentityDefinitionPresent => IdentityDefinitionLocator is not null;

    public AtlasStructuralLocator? IdentityDefinitionLocator { get; } = identityDefinitionLocator;
}

public sealed class AtlasStructuralArrayObservation(
    AtlasStructuralLocator locator,
    AtlasStructuralArrayShape shape,
    long childCount,
    AtlasStructuralLocator? identityDefinitionLocator
) : AtlasStructuralObservation(locator)
{
    public AtlasStructuralArrayShape Shape { get; } = shape;

    public long ChildCount { get; } = childCount;

    public bool IdentityDefinitionPresent => IdentityDefinitionLocator is not null;

    public AtlasStructuralLocator? IdentityDefinitionLocator { get; } = identityDefinitionLocator;
}

public sealed class AtlasStructuralReferenceObservation(
    AtlasStructuralLocator locator,
    AtlasStructuralLocator targetIdentityDefinitionLocator
) : AtlasStructuralObservation(locator)
{
    public AtlasStructuralLocator TargetIdentityDefinitionLocator { get; } =
        targetIdentityDefinitionLocator
        ?? throw new ArgumentNullException(nameof(targetIdentityDefinitionLocator));
}

public sealed record AtlasStructuralScanCensus(
    long NodeOccurrences,
    long ObjectOccurrences,
    long ArrayOccurrences,
    long ScalarOccurrences,
    long ReferenceOccurrences,
    long OrdinaryMemberEdges,
    long ArrayElementEdges,
    long IdentityDefinitions,
    long ClassMarkers,
    long IdentityArrayWrappers,
    long DistinctReferencedDefinitions
);

public sealed class AtlasStructuralScanDocument
{
    public const string CurrentSchemaVersion = "atlas-structural-scan/v1";

    private readonly AtlasStructuralObservation[] observations;
    private readonly IReadOnlyList<AtlasStructuralObservation> readOnlyObservations;

    public AtlasStructuralScanDocument(
        AtlasDocumentRole documentRole,
        AtlasStructuralScanCensus census,
        IEnumerable<AtlasStructuralObservation> observations
    )
    {
        ArgumentNullException.ThrowIfNull(census);
        ArgumentNullException.ThrowIfNull(observations);
        DocumentRole = documentRole;
        Census = census;
        this.observations = [.. observations];
        readOnlyObservations = Array.AsReadOnly(this.observations);
    }

    public string SchemaVersion { get; } = CurrentSchemaVersion;

    public AtlasDocumentRole DocumentRole { get; }

    public AtlasStructuralScanCensus Census { get; }

    public IReadOnlyList<AtlasStructuralObservation> Observations => readOnlyObservations;
}

public sealed class AtlasStructuralScanResult
{
    private readonly byte[] canonicalUtf8;

    internal AtlasStructuralScanResult(
        AtlasStructuralScanDocument document,
        ReadOnlySpan<byte> canonicalUtf8
    )
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        this.canonicalUtf8 = canonicalUtf8.ToArray();
    }

    public AtlasStructuralScanDocument Document { get; }

    public ReadOnlyMemory<byte> CanonicalUtf8 => canonicalUtf8.ToArray();

    public byte[] GetCanonicalUtf8Bytes() => canonicalUtf8.ToArray();
}

public sealed record AtlasStructuralScannerLimits
{
    public static AtlasStructuralScannerLimits Default { get; } = new();

    public int MaximumObservations { get; init; } = 1_000_000;

    public int MaximumLocatorDepth { get; init; } = 256;

    public long MaximumRetainedLocatorSegments { get; init; } = 8_000_000;

    public int MaximumCanonicalUtf8Bytes { get; init; } = 256 * 1024 * 1024;

    internal void Validate()
    {
        if (
            MaximumObservations < 1
            || MaximumLocatorDepth < 0
            || MaximumRetainedLocatorSegments < 0
            || MaximumCanonicalUtf8Bytes < 1
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(AtlasStructuralScannerLimits),
                "Structural scanner limits are out of range."
            );
        }
    }
}

public enum AtlasStructuralScanFailure
{
    ObservationLimit,
    LocatorDepthLimit,
    RetainedSegmentLimit,
    CanonicalSerializationLimit,
    DuplicateLocator,
    InvalidLocator,
    ContainmentAlias,
    ContainmentCycle,
    MissingReferenceTarget,
    CensusMismatch,
    MalformedScanDocument,
    SourceMismatch,
    UnsupportedInternalState,
}

public sealed class AtlasStructuralScanException : Exception
{
    public AtlasStructuralScanException(
        AtlasStructuralScanFailure failure,
        Exception? innerException = null
    )
        : base(GetMessage(failure), innerException)
    {
        Failure = failure;
    }

    public AtlasStructuralScanFailure Failure { get; }

    private static string GetMessage(AtlasStructuralScanFailure failure) =>
        failure switch
        {
            AtlasStructuralScanFailure.ObservationLimit =>
                "The structural scan exceeds its observation limit.",
            AtlasStructuralScanFailure.LocatorDepthLimit =>
                "The structural scan exceeds its locator depth limit.",
            AtlasStructuralScanFailure.RetainedSegmentLimit =>
                "The structural scan exceeds its retained locator segment limit.",
            AtlasStructuralScanFailure.CanonicalSerializationLimit =>
                "The structural scan exceeds its canonical serialization limit.",
            AtlasStructuralScanFailure.DuplicateLocator =>
                "The structural scan contains a duplicate locator.",
            AtlasStructuralScanFailure.InvalidLocator =>
                "The structural scan contains an invalid locator.",
            AtlasStructuralScanFailure.ContainmentAlias =>
                "The source graph contains a repeated containment node.",
            AtlasStructuralScanFailure.ContainmentCycle =>
                "The source graph contains a containment cycle.",
            AtlasStructuralScanFailure.MissingReferenceTarget =>
                "The source graph contains an unavailable reference target.",
            AtlasStructuralScanFailure.CensusMismatch =>
                "The structural scan census is inconsistent.",
            AtlasStructuralScanFailure.MalformedScanDocument =>
                "The structural scan document is malformed.",
            AtlasStructuralScanFailure.SourceMismatch =>
                "The structural scan document does not match its expected source.",
            _ => "The structural scanner reached an unsupported internal state.",
        };
}
