namespace WorkflowDelivery.CI;

internal static class ImpactPlanner
{
    internal static CiPlan Plan(PlanRequest request)
        => PlanSelected(request, SelectProjects(request));

    internal static CiPlan PlanSelected(PlanRequest request,
        IReadOnlyDictionary<string, HashSet<SelectionReason>> reasons)
    {
        // Callers can partition native selections, but hydrated facts must remain complete.
        SelectProjects(request with { ChangedPaths = [], Full = false });
        Dictionary<string, ProjectFacts> candidate = request.Candidate.Projects
            .ToDictionary(project => project.Id, StringComparer.Ordinal);

        var available = new Dictionary<CheckKey, CheckSpec>();
        foreach (ProjectFacts project in candidate.Values)
            foreach (CheckSpec check in project.Checks)
                AddCheck(available, check);
        ValidatePrerequisites(available);

        var selected = new Dictionary<CheckKey, PlannedCheck>();
        foreach ((string id, HashSet<SelectionReason> why) in reasons)
        {
            if (!candidate.TryGetValue(id, out ProjectFacts? project))
                continue;
            bool resolvedOrigin = project.Origin switch
            {
                CheckOrigin.Preset => !string.IsNullOrWhiteSpace(project.QualityPreset),
                CheckOrigin.NativeRetained => project.QualityPreset is null,
                _ => false,
            };
            if (!resolvedOrigin || project.Checks.Length == 0)
                throw new InvalidDataException($"Unresolved quality contract for {id}.");
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
                .ToArray()
        );
    }

    internal static Dictionary<string, HashSet<SelectionReason>> SelectProjects(PlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Dictionary<string, ProjectFacts> basis = ValidateFacts(request.Basis);
        Dictionary<string, ProjectFacts> candidate = ValidateFacts(request.Candidate);
        if (request.Basis.Scope != request.Candidate.Scope)
            throw new InvalidDataException("Comparison and candidate coverage scopes differ.");
        ArgumentNullException.ThrowIfNull(request.ChangedPaths);

        var reasons = new Dictionary<string, HashSet<SelectionReason>>(StringComparer.Ordinal);
        var consumers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        AddRelations(basis, consumers);
        AddRelations(candidate, consumers);

        foreach (string path in request.ChangedPaths)
        {
            ValidatePath(path);
            bool known = SelectOwners(request.Basis, path, reasons);
            known |= SelectOwners(request.Candidate, path, reasons);
            if (!known)
                throw new InvalidDataException($"Unresolved changed path: {path}");
        }
        if (request.Full)
            foreach (string project in candidate.Keys)
                AddReason(reasons, project, new("<full>", request.Candidate.Revision, project));

        // Traverse the union: deleting a reference must not hide its former consumers.
        var pending = new Queue<string>(reasons.Keys);
        while (pending.TryDequeue(out string? project))
        {
            if (!consumers.TryGetValue(project, out HashSet<string>? dependents))
                continue;
            foreach (string dependent in dependents)
            {
                bool changed = false;
                foreach (SelectionReason reason in reasons[project].ToArray())
                    changed |= AddReason(reasons, dependent, reason);
                if (changed)
                    pending.Enqueue(dependent);
            }
        }

        return reasons;
    }

    private static Dictionary<string, ProjectFacts> ValidateFacts(RepositoryFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        RequireText(facts.Revision, "revision");
        RequireText(facts.Scope, "coverage scope");
        ArgumentNullException.ThrowIfNull(facts.Errors);
        if (facts.Errors.Length != 0)
            throw new InvalidDataException(
                $"Incomplete {facts.Revision} facts: " + string.Join("; ", facts.Errors)
            );
        ArgumentNullException.ThrowIfNull(facts.Projects);
        var projects = new Dictionary<string, ProjectFacts>(StringComparer.Ordinal);
        foreach (ProjectFacts project in facts.Projects)
        {
            ArgumentNullException.ThrowIfNull(project);
            RequireText(project.Id, "project id");
            if (project.Directory != ".")
                ValidatePath(project.Directory);
            if (!Enum.IsDefined(project.Origin))
                throw new InvalidDataException($"Unknown check origin for {project.Id}.");
            if (project.OwnedPaths is { } owned)
            {
                foreach (string path in owned)
                    ValidatePath(path);
                if (owned.Distinct(StringComparer.Ordinal).Count() != owned.Length)
                    throw new InvalidDataException($"Duplicate owned path for {project.Id}.");
            }
            if (project.ReleaseUnit is not null)
                RequireText(project.ReleaseUnit, "release unit");
            if (!projects.TryAdd(project.Id, project))
                throw new InvalidDataException($"Duplicate project: {project.Id}");
            ArgumentNullException.ThrowIfNull(project.Checks);
            foreach (CheckSpec check in project.Checks)
                ValidateCheck(check);
        }
        foreach (ProjectFacts project in facts.Projects)
        {
            ArgumentNullException.ThrowIfNull(project.Dependencies);
            ArgumentNullException.ThrowIfNull(project.QualityConsumers);
            foreach (string id in project.Dependencies.Concat(project.QualityConsumers))
                if (id is null || !projects.ContainsKey(id))
                    throw new InvalidDataException(
                        $"Unresolved relation from {project.Id} to {id}.");
        }
        ArgumentNullException.ThrowIfNull(facts.SharedInputs);
        foreach (SharedInput input in facts.SharedInputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            ValidatePath(input.Path);
            ArgumentNullException.ThrowIfNull(input.Consumers);
            if (input.Consumers.Length == 0 || input.Consumers.Any(id => id is null ||
                !projects.ContainsKey(id)))
                throw new InvalidDataException($"Unresolved shared input: {input.Path}");
        }
        ArgumentNullException.ThrowIfNull(facts.UnaffectedPaths);
        foreach (string path in facts.UnaffectedPaths)
            ValidatePath(path);
        return projects;
    }

    internal static void ValidateCheck(CheckSpec check)
    {
        ArgumentNullException.ThrowIfNull(check);
        ValidateKey(check.Key);
        RequireText(check.Runner, "runner");
        ArgumentNullException.ThrowIfNull(check.Dimensions);
        foreach ((string name, string value) in check.Dimensions)
        {
            RequireText(name, "dimension name");
            RequireText(value, "dimension value");
        }
        ArgumentNullException.ThrowIfNull(check.Prerequisites);
        foreach (CheckKey key in check.Prerequisites)
            ValidateKey(key);
        if (check.Package is { } package)
        {
            RequireText(package.Unit, "package unit");
            RequireText(package.Build, "package build");
            RequireText(package.Definition, "package definition");
            RequireText(package.ExpectedVersion, "native package version");
            ValidatePath(package.Declaration);
            ValidatePath(package.Directory);
            ValidatePath(package.EntryPoint);
            if (package.PublishDirectory is not null)
                ValidatePath(package.PublishDirectory);
            ArgumentNullException.ThrowIfNull(package.Outputs);
            foreach (PackageOutput output in package.Outputs)
            {
                ArgumentNullException.ThrowIfNull(output);
                RequireText(output.Id, "package output");
                RequireText(output.Role, "package output role");
                RequireText(output.Kind, "package output kind");
            }
            if (package.Outputs.Length == 0 ||
                package.Outputs.Select(output => output.Id).Distinct(StringComparer.Ordinal)
                    .Count() != package.Outputs.Length)
                throw new InvalidDataException("Unresolved complete package outputs.");
        }
    }

    internal static void ValidateKey(CheckKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        RequireText(key.Target, "target");
        RequireText(key.Check, "check");
        RequireText(key.Variant, "variant");
    }

    internal static void RequireText(string value, string subject)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Missing {subject}.");
    }

    internal static void ValidatePath(string path)
    {
        RequireText(path, "repository path");
        if (path.Contains('\\') || path.Contains(':') || path.Split('/').Any(p => p is "" or
            "." or ".."))
            throw new InvalidDataException(
                $"Expected a normalized repository-relative path: {path}");
    }

    private static bool SelectOwners(
        RepositoryFacts facts,
        string path,
        Dictionary<string, HashSet<SelectionReason>> reasons
    )
    {
        bool known = facts.UnaffectedPaths.Contains(path, StringComparer.Ordinal);
        foreach (string owner in DirectConsumers(facts, path))
        {
            known = true;
            AddReason(reasons, owner, new(path, facts.Revision, owner));
        }
        return known;
    }

    internal static string[] DirectConsumers(RepositoryFacts facts, string path)
    {
        // The nearest project owns a nested path; broader consumers are explicit inputs.
        ProjectFacts[] owners = facts.Projects
            .Where(p => path == p.Directory || path.StartsWith(p.Directory + "/",
                StringComparison.Ordinal))
            .ToArray();
        int longest = owners.Length == 0 ? 0 : owners.Max(p => p.Directory.Length);
        return owners.Where(p => p.OwnedPaths is null && p.Directory.Length == longest)
            .Select(p => p.Id)
            .Concat(facts.Projects.Where(p => p.OwnedPaths?.Contains(path,
                StringComparer.Ordinal) == true).Select(p => p.Id))
            .Concat(facts.SharedInputs.Where(i => i.Path == path).SelectMany(i => i.Consumers))
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool AddReason(
        Dictionary<string, HashSet<SelectionReason>> reasons,
        string id,
        SelectionReason reason
    )
    {
        if (!reasons.TryGetValue(id, out HashSet<SelectionReason>? items))
            reasons.Add(id, items = []);
        return items.Add(reason);
    }

    private static void AddRelations(
        Dictionary<string, ProjectFacts> projects,
        Dictionary<string, HashSet<string>> consumers
    )
    {
        void Add(string input, string consumer)
        {
            if (!consumers.TryGetValue(input, out HashSet<string>? values))
                consumers.Add(input, values = new(StringComparer.Ordinal));
            values.Add(consumer);
        }
        foreach (ProjectFacts project in projects.Values)
        {
            foreach (string dependency in project.Dependencies)
                Add(dependency, project.Id);
            foreach (string qualityConsumer in project.QualityConsumers)
                Add(project.Id, qualityConsumer);
            if (project.ReleaseUnit is not null)
                foreach (ProjectFacts member in projects.Values.Where(p => p.ReleaseUnit ==
                    project.ReleaseUnit))
                    Add(project.Id, member.Id);
        }
    }

    private static void AddCheck(Dictionary<CheckKey, CheckSpec> checks, CheckSpec check)
    {
        if (!checks.TryGetValue(check.Key, out CheckSpec? previous))
        {
            checks.Add(check.Key, check);
            return;
        }
        if (previous.Runner != check.Runner
            || previous.Dimensions.Count != check.Dimensions.Count
            || previous.Dimensions.Any(p => !check.Dimensions.TryGetValue(p.Key, out string?
                value) || p.Value != value)
            || !SamePackage(previous.Package, check.Package)
            || !previous.Prerequisites.ToHashSet().SetEquals(check.Prerequisites))
            throw new InvalidDataException($"Conflicting check definition: {check.Key}");
        checks[check.Key] = previous with { Required = previous.Required || check.Required };
    }

    internal static bool SamePackage(PackageTarget? first, PackageTarget? second) =>
        first is null ? second is null : second is not null &&
        (first with { Outputs = second.Outputs }) == second &&
        first.Outputs.SequenceEqual(second.Outputs);

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
            selected.Add(check.Key, new(check, presets.Distinct().ToArray(), reasons.Distinct(
                ).ToArray(), origins.Distinct().ToArray()));
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

    internal static void ValidatePrerequisites(Dictionary<CheckKey, CheckSpec> checks)
    {
        var active = new HashSet<CheckKey>();
        var visited = new HashSet<CheckKey>();
        void Visit(CheckKey key)
        {
            if (visited.Contains(key))
                return;
            if (!checks.TryGetValue(key, out CheckSpec? check))
                throw new InvalidDataException($"Missing prerequisite: {key}");
            if (!active.Add(key))
                throw new InvalidDataException($"Cyclic check prerequisites: {key}");
            foreach (CheckKey prerequisite in check.Prerequisites)
                Visit(prerequisite);
            active.Remove(key);
            visited.Add(key);
        }
        foreach (CheckKey key in checks.Keys)
            Visit(key);
    }
}
