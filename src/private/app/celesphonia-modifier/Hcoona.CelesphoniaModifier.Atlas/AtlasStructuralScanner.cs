namespace Hcoona.CelesphoniaModifier.Atlas;

public static class AtlasStructuralScanner
{
    public static AtlasStructuralScanResult Scan(
        AtlasSaveReadResult source,
        AtlasDocumentRole documentRole,
        AtlasStructuralScannerLimits? limits = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        AtlasStructuralScannerLimits effectiveLimits =
            limits ?? AtlasStructuralScannerLimits.Default;
        effectiveLimits.Validate();
        ValidateDocumentRole(documentRole);
        cancellationToken.ThrowIfCancellationRequested();

        AtlasStructuralScanDocument document = BuildDocument(
            source,
            documentRole,
            effectiveLimits,
            cancellationToken
        );
        AtlasStructuralScanValidator.ValidateAgainstSource(
            document,
            source,
            documentRole,
            effectiveLimits,
            cancellationToken
        );
        byte[] canonicalUtf8 = AtlasStructuralScanJson.SerializeValidated(
            document,
            effectiveLimits,
            cancellationToken
        );
        return new AtlasStructuralScanResult(document, canonicalUtf8);
    }

    internal static AtlasStructuralScanDocument BuildDocument(
        AtlasSaveReadResult source,
        AtlasDocumentRole documentRole,
        AtlasStructuralScannerLimits limits,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<Occurrence> occurrences = [];
        Dictionary<AtlasJsonExNode, VisitState> states = new(ReferenceEqualityComparer.Instance);
        Dictionary<AtlasJsonExNode, AtlasStructuralLocatorSegment[]> identityLocators = new(
            ReferenceEqualityComparer.Instance
        );
        Stack<TraversalFrame> stack = [];
        stack.Push(new TraversalFrame(source.Graph, [], Entered: false, NextChildIndex: 0));
        long retainedSegments = 0;

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TraversalFrame frame = stack.Pop();
            if (frame.Entered)
            {
                AtlasJsonExNode? child = null;
                AtlasStructuralLocatorSegment? childSegment = null;
                switch (frame.Node)
                {
                    case AtlasJsonExObject objectNode
                        when frame.NextChildIndex < objectNode.Members.Count:
                        child = objectNode.Members[frame.NextChildIndex].Value;
                        childSegment = new AtlasOrdinaryMemberLocatorSegment(frame.NextChildIndex);
                        break;
                    case AtlasJsonExArray arrayNode
                        when frame.NextChildIndex < arrayNode.Elements.Count:
                        child = arrayNode.Elements[frame.NextChildIndex];
                        childSegment = new AtlasArrayElementLocatorSegment(frame.NextChildIndex);
                        break;
                }

                if (child is not null && childSegment is not null)
                {
                    stack.Push(frame with { NextChildIndex = checked(frame.NextChildIndex + 1) });
                    stack.Push(
                        new TraversalFrame(
                            child,
                            AppendSegment(frame.Segments, childSegment, limits),
                            Entered: false,
                            NextChildIndex: 0
                        )
                    );
                }
                else
                {
                    states[frame.Node] = VisitState.Complete;
                }

                continue;
            }

            if (frame.Segments.Length > limits.MaximumLocatorDepth)
            {
                throw new AtlasStructuralScanException(
                    AtlasStructuralScanFailure.LocatorDepthLimit
                );
            }

            if (occurrences.Count >= limits.MaximumObservations)
            {
                throw new AtlasStructuralScanException(AtlasStructuralScanFailure.ObservationLimit);
            }

            if (frame.Node is not AtlasJsonExReference)
            {
                if (states.TryGetValue(frame.Node, out VisitState state))
                {
                    throw new AtlasStructuralScanException(
                        state == VisitState.Active
                            ? AtlasStructuralScanFailure.ContainmentCycle
                            : AtlasStructuralScanFailure.ContainmentAlias
                    );
                }

                states.Add(frame.Node, VisitState.Active);
            }

            AddRetainedSegments(ref retainedSegments, frame.Segments.Length, limits);
            occurrences.Add(new Occurrence(frame.Node, frame.Segments));

            if (HasIdentity(frame.Node))
            {
                AddRetainedSegments(ref retainedSegments, frame.Segments.Length, limits);
                if (!identityLocators.TryAdd(frame.Node, frame.Segments))
                {
                    throw new AtlasStructuralScanException(
                        AtlasStructuralScanFailure.UnsupportedInternalState
                    );
                }
            }

            if (frame.Node is AtlasJsonExReference)
            {
                continue;
            }

            switch (frame.Node)
            {
                case AtlasJsonExObject:
                case AtlasJsonExArray:
                    stack.Push(frame with { Entered = true });
                    break;
                case AtlasJsonExScalar:
                    states[frame.Node] = VisitState.Complete;
                    break;
                default:
                    throw new AtlasStructuralScanException(
                        AtlasStructuralScanFailure.UnsupportedInternalState
                    );
            }
        }

        List<AtlasStructuralObservation> observations = new(occurrences.Count);
        long objects = 0;
        long arrays = 0;
        long scalars = 0;
        long references = 0;
        long ordinaryEdges = 0;
        long arrayEdges = 0;
        long identities = 0;
        long classMarkers = 0;
        long identityArrayWrappers = 0;
        HashSet<AtlasJsonExNode> referencedDefinitions = new(ReferenceEqualityComparer.Instance);

        foreach (Occurrence occurrence in occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AtlasStructuralLocatorSegment[] segments = occurrence.Segments;
            if (segments.Length > 0)
            {
                switch (segments[^1])
                {
                    case AtlasOrdinaryMemberLocatorSegment:
                        ordinaryEdges = CheckedIncrement(ordinaryEdges);
                        break;
                    case AtlasArrayElementLocatorSegment:
                        arrayEdges = CheckedIncrement(arrayEdges);
                        break;
                    default:
                        throw new AtlasStructuralScanException(
                            AtlasStructuralScanFailure.UnsupportedInternalState
                        );
                }
            }

            AtlasStructuralObservation observation;
            switch (occurrence.Node)
            {
                case AtlasJsonExScalar scalar:
                    scalars = CheckedIncrement(scalars);
                    observation = new AtlasStructuralScalarObservation(
                        CreateLocator(AtlasStructuralLocatorSubject.NodeOccurrence, segments),
                        MapScalarKind(scalar.Scalar.Kind)
                    );
                    break;
                case AtlasJsonExObject objectNode:
                    objects = CheckedIncrement(objects);
                    AtlasStructuralLocator? objectIdentity = CreateIdentityLocator(
                        objectNode.Identity,
                        segments
                    );
                    if (objectIdentity is not null)
                    {
                        identities = CheckedIncrement(identities);
                    }

                    if (objectNode.OpaqueClass is not null)
                    {
                        classMarkers = CheckedIncrement(classMarkers);
                    }

                    observation = new AtlasStructuralObjectObservation(
                        CreateLocator(AtlasStructuralLocatorSubject.NodeOccurrence, segments),
                        objectNode.Identity.HasValue
                            ? AtlasStructuralObjectShape.IdentityObject
                            : AtlasStructuralObjectShape.PlainObject,
                        objectNode.Members.Count,
                        objectNode.OpaqueClass is not null,
                        objectIdentity
                    );
                    break;
                case AtlasJsonExArray arrayNode:
                    arrays = CheckedIncrement(arrays);
                    AtlasStructuralLocator? arrayIdentity = CreateIdentityLocator(
                        arrayNode.Identity,
                        segments
                    );
                    if (arrayIdentity is not null)
                    {
                        identities = CheckedIncrement(identities);
                        identityArrayWrappers = CheckedIncrement(identityArrayWrappers);
                    }

                    observation = new AtlasStructuralArrayObservation(
                        CreateLocator(AtlasStructuralLocatorSubject.NodeOccurrence, segments),
                        arrayNode.Identity.HasValue
                            ? AtlasStructuralArrayShape.IdentityArrayWrapper
                            : AtlasStructuralArrayShape.PlainArray,
                        arrayNode.Elements.Count,
                        arrayIdentity
                    );
                    break;
                case AtlasJsonExReference reference:
                    references = CheckedIncrement(references);
                    cancellationToken.ThrowIfCancellationRequested();
                    AtlasJsonExNode target = reference.Target;
                    if (
                        target is null
                        || !identityLocators.TryGetValue(
                            target,
                            out AtlasStructuralLocatorSegment[]? targetSegments
                        )
                    )
                    {
                        throw new AtlasStructuralScanException(
                            AtlasStructuralScanFailure.MissingReferenceTarget
                        );
                    }

                    AddRetainedSegments(ref retainedSegments, targetSegments.Length, limits);
                    referencedDefinitions.Add(target);
                    observation = new AtlasStructuralReferenceObservation(
                        CreateLocator(AtlasStructuralLocatorSubject.ReferenceOccurrence, segments),
                        CreateLocator(
                            AtlasStructuralLocatorSubject.IdentityDefinition,
                            targetSegments
                        )
                    );
                    break;
                default:
                    throw new AtlasStructuralScanException(
                        AtlasStructuralScanFailure.UnsupportedInternalState
                    );
            }

            observations.Add(observation);
        }

        AtlasStructuralScanCensus census = new(
            NodeOccurrences: observations.Count,
            ObjectOccurrences: objects,
            ArrayOccurrences: arrays,
            ScalarOccurrences: scalars,
            ReferenceOccurrences: references,
            OrdinaryMemberEdges: ordinaryEdges,
            ArrayElementEdges: arrayEdges,
            IdentityDefinitions: identities,
            ClassMarkers: classMarkers,
            IdentityArrayWrappers: identityArrayWrappers,
            DistinctReferencedDefinitions: referencedDefinitions.Count
        );
        return new AtlasStructuralScanDocument(documentRole, census, observations);
    }

    internal static void ValidateDocumentRole(AtlasDocumentRole documentRole)
    {
        if (
            documentRole
            is not AtlasDocumentRole.GlobalSave
                and not AtlasDocumentRole.ConfigSave
                and not AtlasDocumentRole.SlotSave
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(documentRole),
                "The document role is not supported."
            );
        }
    }

    private static AtlasStructuralLocatorSegment[] AppendSegment(
        AtlasStructuralLocatorSegment[] parent,
        AtlasStructuralLocatorSegment segment,
        AtlasStructuralScannerLimits limits
    )
    {
        if (parent.Length >= limits.MaximumLocatorDepth)
        {
            throw new AtlasStructuralScanException(AtlasStructuralScanFailure.LocatorDepthLimit);
        }

        AtlasStructuralLocatorSegment[] result = new AtlasStructuralLocatorSegment[
            parent.Length + 1
        ];
        parent.CopyTo(result, 0);
        result[^1] = segment;
        return result;
    }

    private static void AddRetainedSegments(
        ref long retainedSegments,
        int count,
        AtlasStructuralScannerLimits limits
    )
    {
        try
        {
            retainedSegments = checked(retainedSegments + count);
        }
        catch (OverflowException)
        {
            throw new AtlasStructuralScanException(AtlasStructuralScanFailure.RetainedSegmentLimit);
        }

        if (retainedSegments > limits.MaximumRetainedLocatorSegments)
        {
            throw new AtlasStructuralScanException(AtlasStructuralScanFailure.RetainedSegmentLimit);
        }
    }

    private static bool HasIdentity(AtlasJsonExNode node) =>
        node switch
        {
            AtlasJsonExObject objectNode => objectNode.Identity.HasValue,
            AtlasJsonExArray arrayNode => arrayNode.Identity.HasValue,
            _ => false,
        };

    private static AtlasStructuralLocator? CreateIdentityLocator(
        int? identity,
        AtlasStructuralLocatorSegment[] segments
    ) =>
        identity.HasValue
            ? CreateLocator(AtlasStructuralLocatorSubject.IdentityDefinition, segments)
            : null;

    private static AtlasStructuralLocator CreateLocator(
        AtlasStructuralLocatorSubject subject,
        AtlasStructuralLocatorSegment[] segments
    ) => new(subject, segments);

    private static long CheckedIncrement(long value)
    {
        try
        {
            return checked(value + 1);
        }
        catch (OverflowException)
        {
            throw new AtlasStructuralScanException(AtlasStructuralScanFailure.CensusMismatch);
        }
    }

    private static AtlasStructuralScalarKind MapScalarKind(AtlasJsonScalarKind kind) =>
        kind switch
        {
            AtlasJsonScalarKind.Text => AtlasStructuralScalarKind.Text,
            AtlasJsonScalarKind.Number => AtlasStructuralScalarKind.Number,
            AtlasJsonScalarKind.True => AtlasStructuralScalarKind.True,
            AtlasJsonScalarKind.False => AtlasStructuralScalarKind.False,
            AtlasJsonScalarKind.Null => AtlasStructuralScalarKind.Null,
            _ => throw new AtlasStructuralScanException(
                AtlasStructuralScanFailure.UnsupportedInternalState
            ),
        };

    private enum VisitState
    {
        Active,
        Complete,
    }

    private readonly record struct TraversalFrame(
        AtlasJsonExNode Node,
        AtlasStructuralLocatorSegment[] Segments,
        bool Entered,
        int NextChildIndex
    );

    private readonly record struct Occurrence(
        AtlasJsonExNode Node,
        AtlasStructuralLocatorSegment[] Segments
    );
}
