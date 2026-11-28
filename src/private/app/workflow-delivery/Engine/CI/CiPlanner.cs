namespace WorkflowDelivery.CI;

internal static class CiPlanner
{
    internal static CiPlan Plan(PlanRequest request,
        IReadOnlyCollection<SelectionReason>? pairedReasons = null) =>
        PlanSelected(request, ImpactPlanner.SelectProjects(request, pairedReasons));

    internal static CiPlan PlanSelected(PlanRequest request,
        IReadOnlyDictionary<string, HashSet<SelectionReason>> reasons)
    {
        // Callers can partition native selections, but hydrated facts must remain complete.
        ImpactPlanner.SelectProjects(request with { ChangedPaths = [], Full = false });
        Dictionary<string, ProjectFacts> candidate = request.Candidate.Projects
            .ToDictionary(project => project.Id, StringComparer.Ordinal);

        var available = new Dictionary<CheckKey, CheckSpec>();
        foreach (ProjectFacts project in candidate.Values)
            foreach (CheckSpec check in project.Checks)
                AddCheck(available, check);
        ImpactPlanner.ValidatePrerequisites(available);

        var selected = new Dictionary<CheckKey, PlannedCheck>();
        foreach ((string id, HashSet<SelectionReason> why) in reasons)
        {
            if (!candidate.TryGetValue(id, out ProjectFacts? project))
                continue;
            ImpactPlanner.ValidateQualityOwner(project);
            string[] presets = project.Origin == CheckOrigin.Preset
                ? [project.QualityPreset!] : [];
            foreach (CheckSpec check in project.Checks)
                SelectCheck(selected, check, presets, why, [project.Origin]);
        }

        var work = new Queue<CheckKey>(selected.Keys);
        while (work.TryDequeue(out CheckKey? key))
        {
            PlannedCheck owner = selected[key];
            foreach (CheckKey prerequisite in owner.Work.Prerequisites)
            {
                CheckSpec check = available[prerequisite] with { Required = owner.Work.Required };
                if (SelectCheck(selected, check, owner.QualityPresets, owner.Reasons,
                    owner.Origins))
                    work.Enqueue(prerequisite);
            }
        }

        return new(
            request.Basis.Revision,
            request.Candidate.Revision,
            request.Candidate.Scope,
            selected.Values
                .OrderBy(x => x.Work.Key.Target, StringComparer.Ordinal)
                .ThenBy(x => x.Work.Key.Check, StringComparer.Ordinal)
                .ThenBy(x => x.Work.Key.Variant, StringComparer.Ordinal)
                .Select(x => x with
                {
                    QualityPresets = x.QualityPresets.Order(StringComparer.Ordinal).ToArray(),
                    Origins = x.Origins.Order().ToArray(),
                    Reasons = x.Reasons
                        .OrderBy(r => r.Path, StringComparer.Ordinal)
                        .ThenBy(r => r.Revision, StringComparer.Ordinal)
                        .ThenBy(r => r.Project, StringComparer.Ordinal)
                        .ToArray(),
                })
                .ToArray(),
            request.Full
        );
    }

    private static void AddCheck(Dictionary<CheckKey, CheckSpec> checks, CheckSpec check)
    {
        if (!checks.TryGetValue(check.Key, out CheckSpec? previous))
        {
            checks.Add(check.Key, check);
            return;
        }
        if (!previous.SameTarget(check) ||
            !previous.Prerequisites.ToHashSet().SetEquals(check.Prerequisites))
            throw new InvalidDataException($"Conflicting check definition: {check.Key}");
        checks[check.Key] = previous with { Required = previous.Required || check.Required };
    }

    private static bool SelectCheck(
        Dictionary<CheckKey, PlannedCheck> selected,
        CheckSpec check,
        IEnumerable<string> presets,
        IEnumerable<SelectionReason> reasons,
        IEnumerable<CheckOrigin> origins
    )
    {
        if (!selected.TryGetValue(check.Key, out PlannedCheck? old))
        {
            selected.Add(check.Key, new(check, presets.Distinct().ToArray(),
                reasons.Distinct().ToArray(), origins.Distinct().ToArray()));
            return true;
        }
        string[] mergedPresets = old.QualityPresets.Union(presets).ToArray();
        SelectionReason[] mergedReasons = old.Reasons.Union(reasons).ToArray();
        CheckOrigin[] mergedOrigins = old.Origins.Union(origins).ToArray();
        bool required = old.Work.Required || check.Required;
        bool changed = required != old.Work.Required
            || mergedPresets.Length != old.QualityPresets.Length
            || mergedOrigins.Length != old.Origins.Length
            || mergedReasons.Length != old.Reasons.Length;
        selected[check.Key] = new(old.Work with { Required = required }, mergedPresets,
            mergedReasons, mergedOrigins);
        return changed;
    }
}
