using System.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace WorkflowDelivery.Repository;

internal sealed record ControlBuildValue(string Identity,
    IReadOnlyDictionary<string, string> Metadata);
internal sealed record ControlBuildParameter(TaskParameterMessageKind Kind, string Name,
    string? ItemType, string? PropertyName, ControlBuildValue[] Values);
internal sealed record ControlBuildTask(string Project, string Name, string Definition,
    string Implementation, ControlBuildParameter[] Parameters);
internal readonly record struct ControlBuildEvaluation(int Submission, int Node, int Evaluation);
internal sealed record ControlBuildImport(string Project, string Path)
{
    internal ControlBuildEvaluation? Evaluation { get; init; }
    internal string? Importer { get; init; }
    internal string? UnexpandedProject { get; init; }
}
internal sealed record ControlBuildLog(string Project, ControlBuildTask[] Tasks,
    ControlBuildImport[] Imports, IReadOnlyDictionary<string, string> Properties,
    IReadOnlyDictionary<string, string> GlobalProperties);

// Replay native events for one complete control build. Input semantics belong to its adapter.
internal static class ControlBuildLogReader
{
    internal static ControlBuildLog Read(string binlog, string project, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var collector = new Collector(project, token);
        var replay = new BinaryLogReplayEventSource { AllowForwardCompatibility = false };
        replay.AnyEventRaised += collector.Accept;
        replay.Replay(binlog, token);
        return collector.Complete();
    }

    internal static ControlBuildLog ReadEvents(IEnumerable<BuildEventArgs> events, string project,
        CancellationToken token)
    {
        var collector = new Collector(project, token);
        foreach (BuildEventArgs entry in events)
            collector.Accept(null, entry);
        return collector.Complete();
    }

    private readonly record struct ProjectContext(int Submission, int Node, int Project,
        int Instance)
    {
        internal static ProjectContext From(BuildEventContext value) =>
            new(value.SubmissionId, value.NodeId, value.ProjectContextId, value.ProjectInstanceId);
    }

    private readonly record struct TaskContext(ProjectContext Project, int Target, int Task)
    {
        internal static TaskContext From(BuildEventContext value) =>
            new(ProjectContext.From(value), value.TargetId, value.TaskId);
    }

    private readonly record struct EvaluationContext(int Submission, int Node, int Evaluation)
    {
        internal static EvaluationContext From(BuildEventContext value) =>
            new(value.SubmissionId, value.NodeId, value.EvaluationId);
    }

    private sealed class TaskState(TaskStartedEventArgs start)
    {
        internal TaskStartedEventArgs Start { get; } = start;
        internal List<ControlBuildParameter> Parameters { get; } = [];
        internal bool Finished { get; set; }
    }

    private sealed class Collector(string project, CancellationToken token)
    {
        private readonly string project = Path.GetFullPath(project);
        private readonly Dictionary<ProjectContext, string> projects = [];
        private readonly Dictionary<ProjectContext, ProjectStartedEventArgs> projectStarts = [];
        private readonly HashSet<ProjectContext> finishedProjects = [];
        private readonly Dictionary<EvaluationContext, string> evaluations = [];
        private readonly Dictionary<EvaluationContext,
            IReadOnlyDictionary<string, string>> evaluationProperties = [];
        private readonly HashSet<EvaluationContext> finishedEvaluations = [];
        private readonly Dictionary<TaskContext, TaskState> tasks = [];
        private readonly List<ControlBuildImport> imports = [];
        private bool started;
        private bool finished;

        internal void Accept(object? sender, BuildEventArgs entry)
        {
            token.ThrowIfCancellationRequested();
            switch (entry)
            {
                case BuildStartedEventArgs:
                    Require(!started && !finished, "Conflicting native build start.");
                    started = true;
                    break;
                case BuildFinishedEventArgs end:
                    Require(started && !finished && end.Succeeded,
                        "The native build did not complete successfully.");
                    finished = true;
                    break;
                case BuildErrorEventArgs:
                    throw new InvalidDataException("The native build contains an error.");
                case ProjectEvaluationStartedEventArgs evaluation:
                    Require(!finished, "Evaluation follows native build completion.");
                    Require(evaluations.TryAdd(EvaluationContext.From(Context(entry)),
                        FullPath(evaluation.ProjectFile)), "Conflicting native evaluation.");
                    break;
                case ProjectEvaluationFinishedEventArgs evaluation:
                    EvaluationContext evaluationKey = EvaluationContext.From(Context(entry));
                    Require(evaluations.TryGetValue(evaluationKey, out string? evaluatedProject) &&
                        evaluatedProject == FullPath(evaluation.ProjectFile) &&
                        finishedEvaluations.Add(evaluationKey), "Incomplete native evaluation.");
                    evaluationProperties.Add(evaluationKey, Properties(evaluation.Properties));
                    break;
                case ProjectImportedEventArgs import:
                    EvaluationContext importKey = EvaluationContext.From(Context(entry));
                    Require(evaluations.TryGetValue(importKey, out string? importingProject) &&
                        !finishedEvaluations.Contains(importKey), "Unbound native import.");
                    Require(!import.ImportIgnored,
                        "The native operation ignored an invalid import.");
                    // The SDK emits null for a false condition or a glob with no matches.
                    if (import.ImportedProjectFile is not null)
                        imports.Add(new(importingProject!, FullPath(import.ImportedProjectFile))
                        {
                            Evaluation = new(importKey.Submission, importKey.Node,
                                importKey.Evaluation),
                            Importer = string.IsNullOrEmpty(import.ProjectFile) ? null :
                                FullPath(import.ProjectFile),
                            UnexpandedProject = import.UnexpandedProject,
                        });
                    break;
                case ProjectStartedEventArgs start:
                    Require(started && !finished, "Project is outside the native build.");
                    Require(projects.TryAdd(ProjectContext.From(Context(entry)),
                        FullPath(start.ProjectFile)), "Conflicting native project start.");
                    projectStarts.Add(ProjectContext.From(Context(entry)), start);
                    break;
                case ProjectFinishedEventArgs end:
                    ProjectContext projectKey = ProjectContext.From(Context(entry));
                    Require(projects.TryGetValue(projectKey, out string? nativeProject) &&
                        nativeProject == FullPath(end.ProjectFile) && end.Succeeded &&
                        finishedProjects.Add(projectKey), "Incomplete native project.");
                    break;
                case TaskStartedEventArgs start:
                    TaskContext key = TaskContext.From(Context(entry));
                    Require(projects.TryGetValue(key.Project, out string? owner) &&
                        owner == FullPath(start.ProjectFile) && !finished &&
                        !finishedProjects.Contains(key.Project), "Unbound native task.");
                    Require(!string.IsNullOrWhiteSpace(start.TaskName) &&
                        !string.IsNullOrWhiteSpace(start.TaskFile) &&
                        !string.IsNullOrWhiteSpace(start.TaskAssemblyLocation),
                        "Native task implementation identity is unavailable.");
                    Require(tasks.TryAdd(key, new(start)), "Conflicting native task context.");
                    break;
                case TaskParameterEventArgs parameter when parameter.Kind is
                    TaskParameterMessageKind.TaskInput or TaskParameterMessageKind.TaskOutput:
                    TaskState task = ActiveTask(entry);
                    Require(!string.IsNullOrWhiteSpace(parameter.ParameterName),
                        "The SDK omitted a native parameter role.");
                    if (parameter.Kind == TaskParameterMessageKind.TaskInput)
                        Require(parameter.ItemType == parameter.ParameterName &&
                            !task.Parameters.Any(p =>
                                p.Kind == TaskParameterMessageKind.TaskInput &&
                                p.Name.Equals(parameter.ParameterName,
                                    StringComparison.OrdinalIgnoreCase)),
                            "Conflicting native task input.");
                    Require(parameter.Items is not null, "Native parameter values are missing.");
                    ControlBuildValue[] values = parameter.Items!.Cast<object>()
                        .Select(Value).ToArray();
                    task.Parameters.Add(new(parameter.Kind, parameter.ParameterName,
                        parameter.ItemType, parameter.PropertyName, values));
                    break;
                case TaskFinishedEventArgs end:
                    TaskState completed = ActiveTask(entry);
                    Require(end.Succeeded && completed.Start.TaskName == end.TaskName &&
                        FullPath(completed.Start.ProjectFile) == FullPath(end.ProjectFile),
                        "The native task did not complete successfully.");
                    completed.Finished = true;
                    break;
            }
        }

        internal ControlBuildLog Complete()
        {
            token.ThrowIfCancellationRequested();
            Require(started && finished && projects.ContainsValue(project) &&
                projects.Count == finishedProjects.Count &&
                evaluations.Count == finishedEvaluations.Count &&
                tasks.Values.All(task => task.Finished), "Incomplete native control build.");
            TaskState[] compilers = tasks.Values.Where(task =>
                FullPath(task.Start.ProjectFile) == project &&
                task.Start.TaskName == "Csc").ToArray();
            Require(compilers.Length == 1 && compilers[0].Parameters.Any(p =>
                p.Kind == TaskParameterMessageKind.TaskInput && p.Name == "Sources" &&
                p.Values.Length != 0), "The control build requires an actual compiler invocation.");
            ProjectStartedEventArgs root = projectStarts[ProjectContext.From(
                Context(compilers[0].Start))];
            IReadOnlyDictionary<string, string> properties = Properties(root.Properties);
            if (evaluations.TryGetValue(EvaluationContext.From(Context(root)),
                out string? evaluatedRoot))
            {
                Require(evaluatedRoot == project, "The compiler has a different evaluation.");
                IReadOnlyDictionary<string, string> evaluated = evaluationProperties[
                    EvaluationContext.From(Context(root))];
                if (evaluated.Count != 0)
                {
                    Require(properties.All(pair => evaluated.TryGetValue(pair.Key,
                        out string? value) && pair.Value == value),
                        "Conflicting native project and evaluation properties.");
                    properties = evaluated;
                }
            }
            return new(project, tasks.Values.Select(task => new ControlBuildTask(
                FullPath(task.Start.ProjectFile), task.Start.TaskName,
                FullPath(task.Start.TaskFile), FullPath(task.Start.TaskAssemblyLocation),
                task.Parameters.ToArray())).ToArray(), imports.Distinct().ToArray(), properties,
                new Dictionary<string, string>(root.GlobalProperties ??
                    new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase));
        }

        private static Dictionary<string, string> Properties(IEnumerable? values)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (values is null) return result;
            foreach (object value in values)
            {
                // The maintained replay emits evaluated properties as DictionaryEntry;
                // native global/property dictionaries may also enumerate typed pairs.
                KeyValuePair<string, string> pair = value switch
                {
                    DictionaryEntry { Key: string name, Value: string text } => new(name, text),
                    KeyValuePair<string, string> native => native,
                    _ => throw new InvalidDataException("Unsupported native property item."),
                };
                Require(!string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null &&
                    result.TryAdd(pair.Key, pair.Value), "Conflicting native property item.");
            }
            return result;
        }

        private TaskState ActiveTask(BuildEventArgs entry)
        {
            TaskContext key = TaskContext.From(Context(entry));
            Require(tasks.TryGetValue(key, out TaskState? task) && !task.Finished && !finished &&
                !finishedProjects.Contains(key.Project), "Unbound or completed native task.");
            return task!;
        }

        private static ControlBuildValue Value(object value)
        {
            Require(value is ITaskItem, "Unsupported native parameter item representation.");
            var item = (ITaskItem)value;
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (object name in item.MetadataNames)
            {
                Require(name is string text && !string.IsNullOrWhiteSpace(text),
                    "Malformed native item metadata.");
                string key = (string)name;
                Require(metadata.TryAdd(key, item.GetMetadata(key)),
                    "Conflicting native item metadata.");
            }
            return new(item.ItemSpec, metadata);
        }

        private static BuildEventContext Context(BuildEventArgs entry) =>
            entry.BuildEventContext ??
            throw new InvalidDataException("Native event context is missing.");

        private static string FullPath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
                throw new InvalidDataException("Native build context requires an absolute path.");
            return Path.GetFullPath(value);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
