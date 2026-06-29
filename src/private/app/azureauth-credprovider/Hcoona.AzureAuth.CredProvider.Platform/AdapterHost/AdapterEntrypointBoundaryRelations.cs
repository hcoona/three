namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

internal static class AdapterEntrypointBoundaryRelations
{
    internal static bool HasProtocolBoundaryConflict(
        AdapterEntrypointDescriptor entrypoint,
        string? executableName,
        IReadOnlyList<string> arguments,
        bool useWindowsExecutableSemantics,
        bool ignoreExecutableBoundary)
    {
        ArgumentNullException.ThrowIfNull(entrypoint);
        ArgumentNullException.ThrowIfNull(arguments);

        return ignoreExecutableBoundary
            ? entrypoint.IsExecutableOnlyBoundary ||
              HasProtocolMatchOrConfusableArgumentBoundary(entrypoint, arguments)
            : entrypoint.MatchesExecutableBoundary(
                  executableName,
                  useWindowsExecutableSemantics) &&
              entrypoint.HasConfusableProtocolArgumentBoundary(arguments);
    }

    internal static bool HasProtocolMatchOrConfusableArgumentBoundary(
        AdapterEntrypointDescriptor entrypoint,
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(entrypoint);
        ArgumentNullException.ThrowIfNull(arguments);

        return entrypoint.MatchesArgumentBoundary(arguments) ||
               entrypoint.HasConfusableProtocolArgumentBoundary(arguments);
    }

    internal static bool IsInvocationCoveredByProtocol(
        AdapterEntrypointDescriptor subset,
        AdapterEntrypointDescriptor protocolCoverage)
    {
        ArgumentNullException.ThrowIfNull(subset);
        ArgumentNullException.ThrowIfNull(protocolCoverage);

        if (protocolCoverage.Mode != AdapterInvocationMode.Protocol ||
            !ProtocolCoversHumanArgumentBoundary(subset, protocolCoverage))
        {
            return false;
        }

        return HasExecutableSubsetRelation(
                   subset,
                   protocolCoverage,
                   useWindowsExecutableSemantics: false) ||
               HasExecutableSubsetRelation(
                   subset,
                   protocolCoverage,
                   useWindowsExecutableSemantics: true);
    }

    internal static bool IsInvocationCoveredByProtocolUnion(
        AdapterEntrypointDescriptor subset,
        IReadOnlyList<AdapterEntrypointDescriptor> protocolCoverage,
        out AdapterEntrypointDescriptor[] coveringProtocolEntrypoints)
    {
        ArgumentNullException.ThrowIfNull(subset);
        ArgumentNullException.ThrowIfNull(protocolCoverage);

        coveringProtocolEntrypoints = Array.Empty<AdapterEntrypointDescriptor>();
        if (!subset.HasExecutableConstraint)
        {
            return false;
        }

        AdapterEntrypointDescriptor[] argumentCoveringProtocolEntrypoints = protocolCoverage
            .Where(static entrypoint => entrypoint.Mode == AdapterInvocationMode.Protocol)
            .Where(entrypoint => ProtocolCoversHumanArgumentBoundary(subset, entrypoint))
            .ToArray();
        if (argumentCoveringProtocolEntrypoints.Length < 2)
        {
            return false;
        }

        return TryGetExecutableUnionCoverage(
                   subset,
                   argumentCoveringProtocolEntrypoints,
                   useWindowsExecutableSemantics: false,
                   out coveringProtocolEntrypoints) ||
               TryGetExecutableUnionCoverage(
                   subset,
                   argumentCoveringProtocolEntrypoints,
                   useWindowsExecutableSemantics: true,
                   out coveringProtocolEntrypoints);
    }

    internal static ConstraintRelation CompareDeclaredExecutableConstraints(
        AdapterEntrypointDescriptor left,
        AdapterEntrypointDescriptor right,
        bool useWindowsExecutableSemantics)
    {
        if (!left.HasExecutableConstraint &&
            !right.HasExecutableConstraint)
        {
            return ConstraintRelation.Equal;
        }

        if (!left.HasExecutableConstraint)
        {
            return ConstraintRelation.Superset;
        }

        if (!right.HasExecutableConstraint)
        {
            return ConstraintRelation.Subset;
        }

        HashSet<string> leftNames = GetNormalizedExecutableNameSet(
            left.ExecutableNames,
            useWindowsExecutableSemantics);
        HashSet<string> rightNames = GetNormalizedExecutableNameSet(
            right.ExecutableNames,
            useWindowsExecutableSemantics);
        bool leftSubsetOfRight = leftNames.IsSubsetOf(rightNames);
        bool rightSubsetOfLeft = rightNames.IsSubsetOf(leftNames);
        if (leftSubsetOfRight &&
            rightSubsetOfLeft)
        {
            return ConstraintRelation.Equal;
        }

        if (leftSubsetOfRight)
        {
            return ConstraintRelation.Subset;
        }

        if (rightSubsetOfLeft)
        {
            return ConstraintRelation.Superset;
        }

        return ConstraintRelation.Incomparable;
    }

    internal static ConstraintRelation CompareArgumentConstraints(
        AdapterEntrypointDescriptor left,
        AdapterEntrypointDescriptor right)
    {
        bool leftSubsetOfRight = IsArgumentSubset(left, right);
        bool rightSubsetOfLeft = IsArgumentSubset(right, left);
        if (leftSubsetOfRight &&
            rightSubsetOfLeft)
        {
            return ConstraintRelation.Equal;
        }

        if (leftSubsetOfRight)
        {
            return ConstraintRelation.Subset;
        }

        if (rightSubsetOfLeft)
        {
            return ConstraintRelation.Superset;
        }

        return ConstraintRelation.Incomparable;
    }

    private static bool HasExecutableSubsetRelation(
        AdapterEntrypointDescriptor subset,
        AdapterEntrypointDescriptor superset,
        bool useWindowsExecutableSemantics)
    {
        ConstraintRelation relation = CompareDeclaredExecutableConstraints(
            subset,
            superset,
            useWindowsExecutableSemantics);
        return relation == ConstraintRelation.Equal ||
               relation == ConstraintRelation.Subset;
    }

    private static bool TryGetExecutableUnionCoverage(
        AdapterEntrypointDescriptor subset,
        IReadOnlyList<AdapterEntrypointDescriptor> supersets,
        bool useWindowsExecutableSemantics,
        out AdapterEntrypointDescriptor[] coveringSupersets)
    {
        HashSet<string> uncoveredExecutableNames = GetNormalizedExecutableNameSet(
            subset.ExecutableNames,
            useWindowsExecutableSemantics);
        var coveringSupersetsBuilder = new List<AdapterEntrypointDescriptor>();

        foreach (AdapterEntrypointDescriptor superset in supersets)
        {
            if (!superset.HasExecutableConstraint)
            {
                coveringSupersetsBuilder.Add(superset);
                coveringSupersets = coveringSupersetsBuilder.ToArray();
                return true;
            }

            HashSet<string> executableNames = GetNormalizedExecutableNameSet(
                superset.ExecutableNames,
                useWindowsExecutableSemantics);
            if (!uncoveredExecutableNames.Overlaps(executableNames))
            {
                continue;
            }

            uncoveredExecutableNames.ExceptWith(executableNames);
            coveringSupersetsBuilder.Add(superset);
            if (uncoveredExecutableNames.Count == 0)
            {
                coveringSupersets = coveringSupersetsBuilder.ToArray();
                return true;
            }
        }

        coveringSupersets = Array.Empty<AdapterEntrypointDescriptor>();
        return false;
    }

    private static HashSet<string> GetNormalizedExecutableNameSet(
        IReadOnlyList<string> executableNames,
        bool useWindowsExecutableSemantics)
    {
        StringComparer comparer = useWindowsExecutableSemantics
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var normalizedNames = new HashSet<string>(comparer);
        foreach (string executableName in executableNames)
        {
            normalizedNames.Add(AdapterEntrypointDescriptor.NormalizeExecutableName(
                executableName,
                useWindowsExecutableSemantics));
        }

        return normalizedNames;
    }

    private static bool IsArgumentSubset(
        AdapterEntrypointDescriptor subset,
        AdapterEntrypointDescriptor superset)
    {
        return subset.ArgumentMatchMode switch
        {
            AdapterArgumentMatchMode.Any =>
                superset.ArgumentMatchMode == AdapterArgumentMatchMode.Any,
            AdapterArgumentMatchMode.Exact => IsExactSubset(subset.ArgumentTokens, superset),
            AdapterArgumentMatchMode.Prefix => IsPrefixSubset(subset.ArgumentTokens, superset),
            AdapterArgumentMatchMode.ContainsAll =>
                IsContainsAllSubset(subset.ArgumentTokens, superset),
            _ => throw new InvalidOperationException("Unknown adapter argument match mode."),
        };
    }

    private static bool IsExactSubset(
        IReadOnlyList<string> exactTokens,
        AdapterEntrypointDescriptor superset)
    {
        return superset.ArgumentMatchMode switch
        {
            AdapterArgumentMatchMode.Any => true,
            AdapterArgumentMatchMode.Exact => SequenceEqual(exactTokens, superset.ArgumentTokens),
            AdapterArgumentMatchMode.Prefix => StartsWith(exactTokens, superset.ArgumentTokens),
            AdapterArgumentMatchMode.ContainsAll =>
                ContainsAllTokens(exactTokens, superset.ArgumentTokens),
            _ => throw new InvalidOperationException("Unknown adapter argument match mode."),
        };
    }

    private static bool IsPrefixSubset(
        IReadOnlyList<string> prefixTokens,
        AdapterEntrypointDescriptor superset)
    {
        return superset.ArgumentMatchMode switch
        {
            AdapterArgumentMatchMode.Any => true,
            AdapterArgumentMatchMode.Prefix => StartsWith(prefixTokens, superset.ArgumentTokens),
            AdapterArgumentMatchMode.ContainsAll =>
                ContainsAllTokens(prefixTokens, superset.ArgumentTokens),
            AdapterArgumentMatchMode.Exact => false,
            _ => throw new InvalidOperationException("Unknown adapter argument match mode."),
        };
    }

    private static bool IsContainsAllSubset(
        IReadOnlyList<string> containsAllTokens,
        AdapterEntrypointDescriptor superset)
    {
        return superset.ArgumentMatchMode switch
        {
            AdapterArgumentMatchMode.Any => true,
            AdapterArgumentMatchMode.ContainsAll => ContainsAllTokens(
                containsAllTokens,
                superset.ArgumentTokens),
            AdapterArgumentMatchMode.Prefix => false,
            AdapterArgumentMatchMode.Exact => false,
            _ => throw new InvalidOperationException("Unknown adapter argument match mode."),
        };
    }

    private static bool ProtocolCoversHumanArgumentBoundary(
        AdapterEntrypointDescriptor subset,
        AdapterEntrypointDescriptor protocolCoverage)
    {
        return subset.ArgumentMatchMode switch
        {
            AdapterArgumentMatchMode.Any =>
                protocolCoverage.ArgumentMatchMode == AdapterArgumentMatchMode.Any,
            AdapterArgumentMatchMode.Exact => DoesProtocolCoverExactArgumentBoundary(
                subset.ArgumentTokens,
                protocolCoverage),
            AdapterArgumentMatchMode.Prefix => DoesProtocolCoverPrefixArgumentBoundary(
                subset.ArgumentTokens,
                protocolCoverage),
            AdapterArgumentMatchMode.ContainsAll => DoesProtocolCoverContainsAllArgumentBoundary(
                subset.ArgumentTokens,
                protocolCoverage),
            _ => throw new InvalidOperationException("Unknown adapter argument match mode."),
        };
    }

    private static bool DoesProtocolCoverExactArgumentBoundary(
        IReadOnlyList<string> exactTokens,
        AdapterEntrypointDescriptor protocolCoverage)
    {
        return protocolCoverage.ArgumentMatchMode == AdapterArgumentMatchMode.Any ||
               HasProtocolMatchOrConfusableArgumentBoundary(protocolCoverage, exactTokens);
    }

    private static bool DoesProtocolCoverPrefixArgumentBoundary(
        IReadOnlyList<string> prefixTokens,
        AdapterEntrypointDescriptor protocolCoverage)
    {
        return protocolCoverage.ArgumentMatchMode switch
        {
            AdapterArgumentMatchMode.Any => true,
            AdapterArgumentMatchMode.Prefix => StartsWith(
                prefixTokens,
                protocolCoverage.ArgumentTokens),
            AdapterArgumentMatchMode.Exact => StartsWith(
                prefixTokens,
                protocolCoverage.ArgumentTokens),
            AdapterArgumentMatchMode.ContainsAll => TokensOverlap(
                prefixTokens,
                protocolCoverage.ArgumentTokens),
            _ => throw new InvalidOperationException("Unknown adapter argument match mode."),
        };
    }

    private static bool DoesProtocolCoverContainsAllArgumentBoundary(
        IReadOnlyList<string> containsAllTokens,
        AdapterEntrypointDescriptor protocolCoverage)
    {
        return protocolCoverage.ArgumentMatchMode switch
        {
            AdapterArgumentMatchMode.Any => true,
            AdapterArgumentMatchMode.ContainsAll => TokensOverlap(
                containsAllTokens,
                protocolCoverage.ArgumentTokens),
            AdapterArgumentMatchMode.Prefix => false,
            AdapterArgumentMatchMode.Exact => false,
            _ => throw new InvalidOperationException("Unknown adapter argument match mode."),
        };
    }

    private static bool SequenceEqual(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWith(
        IReadOnlyList<string> values,
        IReadOnlyList<string> prefix)
    {
        if (values.Count < prefix.Count)
        {
            return false;
        }

        for (var index = 0; index < prefix.Count; index++)
        {
            if (!string.Equals(values[index], prefix[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TokensOverlap(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        var leftTokens = new HashSet<string>(left, StringComparer.Ordinal);
        return right.Any(leftTokens.Contains);
    }

    private static bool ContainsAllTokens(
        IReadOnlyList<string> availableTokens,
        IReadOnlyList<string> requiredTokens)
    {
        if (requiredTokens.Count == 0)
        {
            return true;
        }

        if (availableTokens.Count < requiredTokens.Count)
        {
            return false;
        }

        Dictionary<string, int> availableTokenCounts = CountTokens(availableTokens);
        foreach (KeyValuePair<string, int> requiredTokenCount in CountTokens(requiredTokens))
        {
            if (!availableTokenCounts.TryGetValue(requiredTokenCount.Key, out int availableCount) ||
                availableCount < requiredTokenCount.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, int> CountTokens(IReadOnlyList<string> tokens)
    {
        var tokenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string token in tokens)
        {
            tokenCounts[token] = tokenCounts.TryGetValue(token, out int count)
                ? count + 1
                : 1;
        }

        return tokenCounts;
    }
}

internal enum ConstraintRelation
{
    Equal = 0,
    Subset = 1,
    Superset = 2,
    Incomparable = 3,
}
