namespace Hcoona.CelesphoniaModifier.Atlas;

internal static class AtlasStructuralScanValidator
{
    private static readonly LocatorPathComparer PathComparer = new();

    public static void ValidateAgainstSource(
        AtlasStructuralScanDocument document,
        AtlasSaveReadResult source,
        AtlasDocumentRole expectedRole,
        AtlasStructuralScannerLimits limits,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (document.DocumentRole != expectedRole)
        {
            throw new AtlasStructuralScanException(AtlasStructuralScanFailure.SourceMismatch);
        }

        ValidateStructure(document, limits, cancellationToken);
        ValidateCensusAgainstSource(
            document.Census,
            source.TokenCensus,
            source.GraphCensus,
            cancellationToken
        );
    }

    public static void ValidateStructure(
        AtlasStructuralScanDocument document,
        AtlasStructuralScannerLimits limits,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        limits.Validate();
        AtlasStructuralScanner.ValidateDocumentRole(document.DocumentRole);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<AtlasStructuralObservation> observations = document.Observations;
        if (observations.Count == 0)
        {
            throw InvalidLocator();
        }

        if (observations.Count > limits.MaximumObservations)
        {
            throw new AtlasStructuralScanException(AtlasStructuralScanFailure.ObservationLimit);
        }

        Dictionary<AtlasStructuralLocator, int> observationIndexes = new(PathComparer);
        Dictionary<AtlasStructuralLocator, List<int>> children = new(PathComparer);
        Dictionary<AtlasStructuralLocator, int> identities = new(PathComparer);
        long retainedSegments = 0;
        int emptyLocatorCount = 0;

        for (int index = 0; index < observations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AtlasStructuralObservation observation = observations[index] ?? throw InvalidLocator();
            ValidateLocator(observation.Locator, limits, ref retainedSegments);
            ValidatePrimarySubject(observation);
            if (observation.Locator.Segments.Count == 0)
            {
                emptyLocatorCount++;
            }

            if (!observationIndexes.TryAdd(observation.Locator, index))
            {
                throw new AtlasStructuralScanException(AtlasStructuralScanFailure.DuplicateLocator);
            }

            switch (observation)
            {
                case AtlasStructuralObjectObservation objectObservation:
                    ValidateObject(objectObservation, limits, ref retainedSegments, identities);
                    break;
                case AtlasStructuralArrayObservation arrayObservation:
                    ValidateArray(arrayObservation, limits, ref retainedSegments, identities);
                    break;
                case AtlasStructuralReferenceObservation referenceObservation:
                    ValidateLocator(
                        referenceObservation.TargetIdentityDefinitionLocator,
                        limits,
                        ref retainedSegments
                    );
                    if (
                        referenceObservation.TargetIdentityDefinitionLocator.Subject
                        != AtlasStructuralLocatorSubject.IdentityDefinition
                    )
                    {
                        throw InvalidLocator();
                    }

                    break;
                case AtlasStructuralScalarObservation:
                    break;
                default:
                    throw new AtlasStructuralScanException(
                        AtlasStructuralScanFailure.UnsupportedInternalState
                    );
            }
        }

        if (emptyLocatorCount != 1 || observations[0].Locator.Segments.Count != 0)
        {
            throw InvalidLocator();
        }

        for (int index = 1; index < observations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AtlasStructuralObservation observation = observations[index];
            AtlasStructuralLocator parentLocator = CreateParentLocator(observation.Locator);
            if (!observationIndexes.TryGetValue(parentLocator, out int parentIndex))
            {
                throw InvalidLocator();
            }

            AtlasStructuralObservation parent = observations[parentIndex];
            AtlasStructuralLocatorSegment finalSegment = observation.Locator.Segments[^1];
            bool validParent = (parent, finalSegment) switch
            {
                (AtlasStructuralObjectObservation, AtlasOrdinaryMemberLocatorSegment) => true,
                (AtlasStructuralArrayObservation, AtlasArrayElementLocatorSegment) => true,
                _ => false,
            };
            if (!validParent)
            {
                throw InvalidLocator();
            }

            if (!children.TryGetValue(parent.Locator, out List<int>? childIndexes))
            {
                childIndexes = [];
                children.Add(parent.Locator, childIndexes);
            }

            childIndexes.Add(index);
        }

        ValidateChildrenAndPreorder(observations, children, cancellationToken);

        foreach (AtlasStructuralObservation observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                observation is AtlasStructuralReferenceObservation reference
                && !identities.ContainsKey(reference.TargetIdentityDefinitionLocator)
            )
            {
                throw new AtlasStructuralScanException(
                    AtlasStructuralScanFailure.MissingReferenceTarget
                );
            }
        }

        AtlasStructuralScanCensus actual = CreateCensus(observations, cancellationToken);
        if (actual != document.Census)
        {
            throw new AtlasStructuralScanException(AtlasStructuralScanFailure.CensusMismatch);
        }

        ValidateInternalCensusEquations(actual, cancellationToken);
    }

    public static bool DocumentsEqual(
        AtlasStructuralScanDocument left,
        AtlasStructuralScanDocument right,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            left.DocumentRole != right.DocumentRole
            || left.Census != right.Census
            || left.Observations.Count != right.Observations.Count
        )
        {
            return false;
        }

        for (int index = 0; index < left.Observations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ObservationsEqual(left.Observations[index], right.Observations[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateObject(
        AtlasStructuralObjectObservation observation,
        AtlasStructuralScannerLimits limits,
        ref long retainedSegments,
        Dictionary<AtlasStructuralLocator, int> identities
    )
    {
        if (observation.ChildCount < 0)
        {
            throw InvalidLocator();
        }

        bool identityShape = observation.Shape == AtlasStructuralObjectShape.IdentityObject;
        if (
            observation.Shape
                is not AtlasStructuralObjectShape.PlainObject
                    and not AtlasStructuralObjectShape.IdentityObject
            || identityShape != observation.IdentityDefinitionPresent
            || observation.ClassMarkerPresent && !identityShape
        )
        {
            throw InvalidLocator();
        }

        if (observation.IdentityDefinitionLocator is not null)
        {
            ValidateIdentityLocator(
                observation,
                observation.IdentityDefinitionLocator,
                limits,
                ref retainedSegments,
                identities
            );
        }
    }

    private static void ValidateArray(
        AtlasStructuralArrayObservation observation,
        AtlasStructuralScannerLimits limits,
        ref long retainedSegments,
        Dictionary<AtlasStructuralLocator, int> identities
    )
    {
        if (observation.ChildCount < 0)
        {
            throw InvalidLocator();
        }

        bool identityShape = observation.Shape == AtlasStructuralArrayShape.IdentityArrayWrapper;
        if (
            observation.Shape
                is not AtlasStructuralArrayShape.PlainArray
                    and not AtlasStructuralArrayShape.IdentityArrayWrapper
            || identityShape != observation.IdentityDefinitionPresent
        )
        {
            throw InvalidLocator();
        }

        if (observation.IdentityDefinitionLocator is not null)
        {
            ValidateIdentityLocator(
                observation,
                observation.IdentityDefinitionLocator,
                limits,
                ref retainedSegments,
                identities
            );
        }
    }

    private static void ValidateIdentityLocator(
        AtlasStructuralObservation owner,
        AtlasStructuralLocator identity,
        AtlasStructuralScannerLimits limits,
        ref long retainedSegments,
        Dictionary<AtlasStructuralLocator, int> identities
    )
    {
        ValidateLocator(identity, limits, ref retainedSegments);
        if (
            identity.Subject != AtlasStructuralLocatorSubject.IdentityDefinition
            || !PathComparer.Equals(owner.Locator, identity)
            || !identities.TryAdd(identity, identities.Count)
        )
        {
            throw InvalidLocator();
        }
    }

    private static void ValidateChildrenAndPreorder(
        IReadOnlyList<AtlasStructuralObservation> observations,
        Dictionary<AtlasStructuralLocator, List<int>> children,
        CancellationToken cancellationToken
    )
    {
        for (int index = 0; index < observations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AtlasStructuralObservation observation = observations[index];
            children.TryGetValue(observation.Locator, out List<int>? childIndexes);
            int actualCount = childIndexes?.Count ?? 0;
            long expectedCount = observation switch
            {
                AtlasStructuralObjectObservation objectObservation => objectObservation.ChildCount,
                AtlasStructuralArrayObservation arrayObservation => arrayObservation.ChildCount,
                _ => 0,
            };
            if (actualCount != expectedCount)
            {
                throw InvalidLocator();
            }

            if (childIndexes is null)
            {
                continue;
            }

            for (int childOrdinal = 0; childOrdinal < childIndexes.Count; childOrdinal++)
            {
                AtlasStructuralLocatorSegment segment = observations[childIndexes[childOrdinal]]
                    .Locator
                    .Segments[^1];
                bool contiguous = segment switch
                {
                    AtlasOrdinaryMemberLocatorSegment ordinary => ordinary.Ordinal == childOrdinal,
                    AtlasArrayElementLocatorSegment array => array.Index == childOrdinal,
                    _ => false,
                };
                if (!contiguous)
                {
                    throw InvalidLocator();
                }
            }
        }

        Stack<int> stack = [];
        stack.Push(0);
        int expectedIndex = 0;
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = stack.Pop();
            if (index != expectedIndex)
            {
                throw InvalidLocator();
            }

            expectedIndex++;
            if (!children.TryGetValue(observations[index].Locator, out List<int>? childIndexes))
            {
                continue;
            }

            for (int child = childIndexes.Count - 1; child >= 0; child--)
            {
                stack.Push(childIndexes[child]);
            }
        }

        if (expectedIndex != observations.Count)
        {
            throw InvalidLocator();
        }
    }

    private static AtlasStructuralScanCensus CreateCensus(
        IReadOnlyList<AtlasStructuralObservation> observations,
        CancellationToken cancellationToken
    )
    {
        long objects = 0;
        long arrays = 0;
        long scalars = 0;
        long references = 0;
        long ordinaryEdges = 0;
        long arrayEdges = 0;
        long identities = 0;
        long classMarkers = 0;
        long identityArrays = 0;
        HashSet<AtlasStructuralLocator> referencedIdentities = new(PathComparer);

        foreach (AtlasStructuralObservation observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observation.Locator.Segments.Count > 0)
            {
                switch (observation.Locator.Segments[^1])
                {
                    case AtlasOrdinaryMemberLocatorSegment:
                        ordinaryEdges = CheckedIncrement(ordinaryEdges);
                        break;
                    case AtlasArrayElementLocatorSegment:
                        arrayEdges = CheckedIncrement(arrayEdges);
                        break;
                }
            }

            switch (observation)
            {
                case AtlasStructuralObjectObservation objectObservation:
                    objects = CheckedIncrement(objects);
                    if (objectObservation.IdentityDefinitionPresent)
                    {
                        identities = CheckedIncrement(identities);
                    }

                    if (objectObservation.ClassMarkerPresent)
                    {
                        classMarkers = CheckedIncrement(classMarkers);
                    }

                    break;
                case AtlasStructuralArrayObservation arrayObservation:
                    arrays = CheckedIncrement(arrays);
                    if (arrayObservation.IdentityDefinitionPresent)
                    {
                        identities = CheckedIncrement(identities);
                        identityArrays = CheckedIncrement(identityArrays);
                    }

                    break;
                case AtlasStructuralScalarObservation:
                    scalars = CheckedIncrement(scalars);
                    break;
                case AtlasStructuralReferenceObservation referenceObservation:
                    references = CheckedIncrement(references);
                    referencedIdentities.Add(referenceObservation.TargetIdentityDefinitionLocator);
                    break;
            }
        }

        return new AtlasStructuralScanCensus(
            NodeOccurrences: observations.Count,
            ObjectOccurrences: objects,
            ArrayOccurrences: arrays,
            ScalarOccurrences: scalars,
            ReferenceOccurrences: references,
            OrdinaryMemberEdges: ordinaryEdges,
            ArrayElementEdges: arrayEdges,
            IdentityDefinitions: identities,
            ClassMarkers: classMarkers,
            IdentityArrayWrappers: identityArrays,
            DistinctReferencedDefinitions: referencedIdentities.Count
        );
    }

    private static void ValidateInternalCensusEquations(
        AtlasStructuralScanCensus census,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            long variantTotal = checked(
                census.ObjectOccurrences
                + census.ArrayOccurrences
                + census.ScalarOccurrences
                + census.ReferenceOccurrences
            );
            long edgeTotal = checked(census.OrdinaryMemberEdges + census.ArrayElementEdges);
            if (
                HasNegativeValue(census)
                || variantTotal != census.NodeOccurrences
                || census.NodeOccurrences - 1 != edgeTotal
                || census.IdentityArrayWrappers > census.ArrayOccurrences
                || census.IdentityDefinitions > census.ObjectOccurrences + census.ArrayOccurrences
                || census.ClassMarkers > census.ObjectOccurrences
                || census.DistinctReferencedDefinitions > census.IdentityDefinitions
            )
            {
                throw new AtlasStructuralScanException(AtlasStructuralScanFailure.CensusMismatch);
            }
        }
        catch (OverflowException)
        {
            throw new AtlasStructuralScanException(AtlasStructuralScanFailure.CensusMismatch);
        }
    }

    private static void ValidateCensusAgainstSource(
        AtlasStructuralScanCensus census,
        AtlasTokenCensus tokens,
        AtlasGraphCensus graph,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            long ordinaryMemberEdges = checked(
                tokens.MemberOccurrences
                - tokens.IdentityMarkers
                - tokens.ClassMarkers
                - tokens.ArrayMarkers
                - tokens.ReferenceMarkers
            );
            long scalarOccurrences = checked(
                tokens.Scalars
                - tokens.IdentityMarkers
                - tokens.ClassMarkers
                - tokens.ReferenceMarkers
            );
            if (
                HasNegativeValue(tokens)
                || HasNegativeValue(graph)
                || ordinaryMemberEdges < 0
                || scalarOccurrences < 0
                || census.NodeOccurrences != graph.MaterializedNodes
                || census.IdentityDefinitions != tokens.IdentityMarkers
                || census.IdentityDefinitions != graph.IdentityDefinitions
                || census.ReferenceOccurrences != tokens.ReferenceMarkers
                || census.ReferenceOccurrences != graph.ReferenceEdges
                || census.ClassMarkers != tokens.ClassMarkers
                || census.IdentityArrayWrappers != tokens.ArrayMarkers
                || census.ArrayElementEdges != tokens.ArrayElements
                || census.OrdinaryMemberEdges != ordinaryMemberEdges
                || census.ScalarOccurrences != scalarOccurrences
                || census.DistinctReferencedDefinitions != graph.SharedTargets
            )
            {
                throw new AtlasStructuralScanException(AtlasStructuralScanFailure.CensusMismatch);
            }
        }
        catch (OverflowException)
        {
            throw new AtlasStructuralScanException(AtlasStructuralScanFailure.CensusMismatch);
        }
    }

    private static bool ObservationsEqual(
        AtlasStructuralObservation left,
        AtlasStructuralObservation right
    )
    {
        if (!left.Locator.Equals(right.Locator))
        {
            return false;
        }

        return (left, right) switch
        {
            (
                AtlasStructuralScalarObservation leftScalar,
                AtlasStructuralScalarObservation rightScalar
            ) => leftScalar.ScalarKind == rightScalar.ScalarKind,
            (
                AtlasStructuralObjectObservation leftObject,
                AtlasStructuralObjectObservation rightObject
            ) => leftObject.Shape == rightObject.Shape
                && leftObject.ChildCount == rightObject.ChildCount
                && leftObject.ClassMarkerPresent == rightObject.ClassMarkerPresent
                && NullableLocatorsEqual(
                    leftObject.IdentityDefinitionLocator,
                    rightObject.IdentityDefinitionLocator
                ),
            (
                AtlasStructuralArrayObservation leftArray,
                AtlasStructuralArrayObservation rightArray
            ) => leftArray.Shape == rightArray.Shape
                && leftArray.ChildCount == rightArray.ChildCount
                && NullableLocatorsEqual(
                    leftArray.IdentityDefinitionLocator,
                    rightArray.IdentityDefinitionLocator
                ),
            (
                AtlasStructuralReferenceObservation leftReference,
                AtlasStructuralReferenceObservation rightReference
            ) => leftReference.TargetIdentityDefinitionLocator.Equals(
                rightReference.TargetIdentityDefinitionLocator
            ),
            _ => false,
        };
    }

    private static bool NullableLocatorsEqual(
        AtlasStructuralLocator? left,
        AtlasStructuralLocator? right
    ) => left is null ? right is null : left.Equals(right);

    private static void ValidatePrimarySubject(AtlasStructuralObservation observation)
    {
        AtlasStructuralLocatorSubject expected =
            observation is AtlasStructuralReferenceObservation
                ? AtlasStructuralLocatorSubject.ReferenceOccurrence
                : AtlasStructuralLocatorSubject.NodeOccurrence;
        if (observation.Locator.Subject != expected)
        {
            throw InvalidLocator();
        }
    }

    private static void ValidateLocator(
        AtlasStructuralLocator locator,
        AtlasStructuralScannerLimits limits,
        ref long retainedSegments
    )
    {
        if (locator is null)
        {
            throw InvalidLocator();
        }

        if (
            locator.Subject
                is not AtlasStructuralLocatorSubject.NodeOccurrence
                    and not AtlasStructuralLocatorSubject.ReferenceOccurrence
                    and not AtlasStructuralLocatorSubject.IdentityDefinition
            || locator.Segments.Count > limits.MaximumLocatorDepth
        )
        {
            throw new AtlasStructuralScanException(
                locator.Segments.Count > limits.MaximumLocatorDepth
                    ? AtlasStructuralScanFailure.LocatorDepthLimit
                    : AtlasStructuralScanFailure.InvalidLocator
            );
        }

        foreach (AtlasStructuralLocatorSegment segment in locator.Segments)
        {
            switch (segment)
            {
                case AtlasOrdinaryMemberLocatorSegment { Ordinal: >= 0 }:
                case AtlasArrayElementLocatorSegment { Index: >= 0 }:
                    break;
                default:
                    throw InvalidLocator();
            }
        }

        try
        {
            retainedSegments = checked(retainedSegments + locator.Segments.Count);
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

    private static AtlasStructuralLocator CreateParentLocator(AtlasStructuralLocator locator)
    {
        int parentLength = locator.Segments.Count - 1;
        AtlasStructuralLocatorSegment[] parent = new AtlasStructuralLocatorSegment[parentLength];
        for (int index = 0; index < parentLength; index++)
        {
            parent[index] = locator.Segments[index];
        }

        return new AtlasStructuralLocator(AtlasStructuralLocatorSubject.NodeOccurrence, parent);
    }

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

    private static bool HasNegativeValue(AtlasStructuralScanCensus census) =>
        census.NodeOccurrences < 0
        || census.ObjectOccurrences < 0
        || census.ArrayOccurrences < 0
        || census.ScalarOccurrences < 0
        || census.ReferenceOccurrences < 0
        || census.OrdinaryMemberEdges < 0
        || census.ArrayElementEdges < 0
        || census.IdentityDefinitions < 0
        || census.ClassMarkers < 0
        || census.IdentityArrayWrappers < 0
        || census.DistinctReferencedDefinitions < 0;

    private static bool HasNegativeValue(AtlasTokenCensus census) =>
        census.Containers < 0
        || census.MemberOccurrences < 0
        || census.ArrayElements < 0
        || census.Scalars < 0
        || census.IdentityMarkers < 0
        || census.ClassMarkers < 0
        || census.ArrayMarkers < 0
        || census.ReferenceMarkers < 0;

    private static bool HasNegativeValue(AtlasGraphCensus census) =>
        census.MaterializedNodes < 0
        || census.IdentityDefinitions < 0
        || census.ReferenceEdges < 0
        || census.SharedTargets < 0
        || census.Cycles < 0;

    private static AtlasStructuralScanException InvalidLocator() =>
        new(AtlasStructuralScanFailure.InvalidLocator);

    private sealed class LocatorPathComparer : IEqualityComparer<AtlasStructuralLocator>
    {
        public bool Equals(AtlasStructuralLocator? x, AtlasStructuralLocator? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null || x.Segments.Count != y.Segments.Count)
            {
                return false;
            }

            for (int index = 0; index < x.Segments.Count; index++)
            {
                if (x.Segments[index] != y.Segments[index])
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(AtlasStructuralLocator obj)
        {
            HashCode hash = new();
            foreach (AtlasStructuralLocatorSegment segment in obj.Segments)
            {
                hash.Add(segment);
            }

            return hash.ToHashCode();
        }
    }
}
