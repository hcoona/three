//! Read one Python planning request and return native UV supplement facts.

use std::collections::BTreeSet;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::ExitCode;
use std::str::FromStr;

use serde::{Deserialize, Serialize};
use uv_auth::CredentialsCache;
use uv_cache::Cache;
use uv_configuration::{DependencyGroupsWithDefaults, NoSources};
use uv_distribution_types::{IndexLocations, Requirement};
use uv_normalize::{DEV_DEPENDENCIES, ExtraName, GroupName, PackageName};
use uv_pep440::VersionSpecifiers;
use uv_pep508::MarkerTree;
use uv_python::Interpreter;
use uv_settings::{FilesystemOptions, Options, ResolverOptions};
use uv_workspace::dependency_groups::FlatDependencyGroups;
use uv_workspace::pyproject::PyProjectToml;
use uv_workspace::{DiscoveryOptions, ProjectWorkspace, VirtualProject, WorkspaceCache};
use workflow_python_native_facts::{
    PlanningError, admit_configuration, lower_build_requirements, select_groups,
};

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct Request {
    workspace_root: PathBuf,
    interpreter: PathBuf,
    cache: PathBuf,
    projects: Vec<ProjectInput>,
    group_operations: Vec<GroupOperation>,
    markers: Vec<MarkerInput>,
    python_constraints: Vec<PythonConstraint>,
}

/// These strings come directly from the passive extractor, not another declaration.
#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct ProjectInput {
    directory: PathBuf,
    build_requirements: Option<Vec<String>>,
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct GroupOperation {
    id: String,
    directory: PathBuf,
    packages: Vec<PackageName>,
    no_dev: bool,
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct MarkerInput {
    id: String,
    expression: String,
    extras: Vec<ExtraName>,
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct PythonConstraint {
    id: String,
    specifier: String,
}

#[derive(Serialize)]
struct Response {
    workspace_root: PathBuf,
    interpreter: PathBuf,
    interpreter_markers: serde_json::Value,
    members: Vec<Member>,
    build_requirements: Vec<BuildRequirements>,
    group_operations: Vec<GroupSelection>,
    markers: Vec<Activity>,
    python_constraints: Vec<Activity>,
}

#[derive(Serialize)]
struct Member {
    name: PackageName,
    directory: PathBuf,
}

#[derive(Serialize)]
struct BuildRequirements {
    directory: PathBuf,
    requirements: Vec<Requirement>,
}

#[derive(Serialize)]
struct GroupSelection {
    id: String,
    groups: Vec<GroupName>,
}

#[derive(Serialize)]
struct Activity {
    id: String,
    active: bool,
}

#[derive(Debug)]
enum HelperError {
    Stage(&'static str),
    Planning(PlanningError),
}

impl From<PlanningError> for HelperError {
    fn from(value: PlanningError) -> Self {
        Self::Planning(value)
    }
}

impl std::fmt::Display for HelperError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Stage(stage) => write!(formatter, "Python native extraction failed ({stage})."),
            Self::Planning(error) => {
                write!(formatter, "Python native extraction failed ({error:?}).")
            }
        }
    }
}

/// Validate with UV before its settings discovery can warn and continue.
fn validate_manifest(directory: &Path) -> Result<(), HelperError> {
    let file = directory.join("pyproject.toml");
    let source = fs::read_to_string(&file).map_err(|_| HelperError::Stage("ManifestRead"))?;
    PyProjectToml::from_string(source, file).map_err(|_| HelperError::Stage("NativeManifest"))?;
    Ok(())
}

fn request_directories(request: &Request) -> Result<BTreeSet<PathBuf>, HelperError> {
    if !request.workspace_root.is_absolute()
        || !request.interpreter.is_absolute()
        || !request.cache.is_absolute()
    {
        return Err(HelperError::Stage("AbsoluteRequestPaths"));
    }
    let mut project_directories = BTreeSet::new();
    for project in &request.projects {
        if !project.directory.is_absolute()
            || !project.directory.starts_with(&request.workspace_root)
            || !project_directories.insert(project.directory.clone())
        {
            return Err(HelperError::Stage("ProjectIdentity"));
        }
    }
    if !project_directories.contains(&request.workspace_root) {
        return Err(HelperError::Stage("MissingRootContext"));
    }
    let mut operations = BTreeSet::new();
    for operation in &request.group_operations {
        if !project_directories.contains(&operation.directory)
            || operation.id.trim().is_empty()
            || !operations.insert(&operation.id)
        {
            return Err(HelperError::Stage("GroupOperationIdentity"));
        }
    }
    for (stage, ids) in [
        (
            "MarkerIdentity",
            request
                .markers
                .iter()
                .map(|input| &input.id)
                .collect::<Vec<_>>(),
        ),
        (
            "PythonConstraintIdentity",
            request
                .python_constraints
                .iter()
                .map(|input| &input.id)
                .collect(),
        ),
    ] {
        let mut unique = BTreeSet::new();
        for id in ids {
            if id.trim().is_empty() || !unique.insert(id) {
                return Err(HelperError::Stage(stage));
            }
        }
    }
    Ok(project_directories)
}

/// Match the native default-selection owner within the admitted zero/singleton scope.
fn group_owner_manifest<'a>(
    current: &'a PyProjectToml,
    packages: &[PackageName],
    members: impl Iterator<Item = (&'a PackageName, &'a PyProjectToml)>,
) -> Result<&'a PyProjectToml, HelperError> {
    match packages {
        [] => Ok(current),
        [name] => {
            let mut matches = members.filter(|(member, _)| *member == name);
            let (_, manifest) = matches
                .next()
                .ok_or(HelperError::Stage("GroupOwnerIdentity"))?;
            if matches.next().is_some() {
                return Err(HelperError::Stage("GroupOwnerIdentity"));
            }
            Ok(manifest)
        }
        _ => Err(HelperError::Stage("GroupOwnerIdentity")),
    }
}

/// Transfer selected owner groups with nonempty native declarations.
fn owner_selected_groups(
    manifest: &PyProjectToml,
    directory: &Path,
    selected: &DependencyGroupsWithDefaults,
) -> Result<Vec<GroupName>, HelperError> {
    let native_groups = FlatDependencyGroups::from_pyproject_toml(directory, manifest)
        .map_err(|_| HelperError::Stage("NativeOwnerGroups"))?;
    let candidates = manifest
        .dependency_groups
        .iter()
        .flat_map(|groups| groups.keys())
        .chain(std::iter::once(&*DEV_DEPENDENCIES));
    let mut contributions = BTreeSet::new();
    for group in candidates.filter(|group| selected.contains(group)) {
        let Some(native_group) = native_groups.get(group) else {
            continue;
        };
        if native_group.requirements.is_empty() {
            // A constraint-only group has no metadata node. Do not discard its
            // native constraint until that necessary shape has a transfer seam.
            if native_group.requires_python.is_some() {
                return Err(HelperError::Stage("EmptyGroupPythonConstraint"));
            }
            continue;
        }
        contributions.insert(group.clone());
    }
    Ok(contributions.into_iter().collect())
}

async fn extract(request: Request) -> Result<Response, HelperError> {
    let project_directories = request_directories(&request)?;
    for project in &request.projects {
        validate_manifest(&project.directory)?;
    }
    for ancestor in request.workspace_root.ancestors().skip(1) {
        if ancestor
            .join("pyproject.toml")
            .try_exists()
            .map_err(|_| HelperError::Stage("AncestorConfigurationRead"))?
        {
            validate_manifest(ancestor)?;
        }
    }
    // This first request has owned empty ambient configuration roots. Preserve
    // the native result/error semantics instead of reproducing configuration lookup.
    if FilesystemOptions::user()
        .map_err(|_| HelperError::Stage("NativeUserConfiguration"))?
        .is_some()
        || FilesystemOptions::system()
            .map_err(|_| HelperError::Stage("NativeSystemConfiguration"))?
            .is_some()
    {
        return Err(HelperError::Stage("UnsupportedAmbientConfiguration"));
    }
    let options = FilesystemOptions::find(&request.workspace_root)
        .map_err(|_| HelperError::Stage("NativeProjectConfiguration"))?
        .map_or_else(Options::default, FilesystemOptions::into_options);
    admit_configuration(&options)?;
    let resolver = ResolverOptions::from(options.top_level);
    let locations = IndexLocations::from(resolver.indexes);
    let sources = NoSources::from_args(
        resolver.no_sources,
        resolver.no_sources_package.unwrap_or_default(),
    );
    if !matches!(sources, NoSources::None) {
        return Err(HelperError::Stage("UnsupportedSourceConfiguration"));
    }
    let cache = Cache::from_path(&request.cache)
        .init()
        .await
        .map_err(|_| HelperError::Stage("NativeCache"))?;
    let workspace_cache = WorkspaceCache::default();
    let credentials_cache = CredentialsCache::new();
    let discovery = DiscoveryOptions::default();
    let workspace = VirtualProject::discover(
        &request.workspace_root,
        &discovery,
        &cache,
        &workspace_cache,
    )
    .await
    .map_err(|_| HelperError::Stage("NativeWorkspace"))?;
    if workspace.workspace().install_path().as_path() != request.workspace_root.as_path()
        || !workspace
            .workspace()
            .conflicts()
            .map_err(|_| HelperError::Stage("NativeConflicts"))?
            .is_empty()
    {
        return Err(HelperError::Stage("WorkspaceContext"));
    }
    let members: Vec<_> = workspace
        .workspace()
        .packages()
        .iter()
        .map(|(name, member)| Member {
            name: name.clone(),
            directory: member.root().to_path_buf(),
        })
        .collect();
    let mut native_directories: BTreeSet<_> = members
        .iter()
        .map(|member| member.directory.clone())
        .collect();
    native_directories.insert(request.workspace_root.clone());
    if native_directories != project_directories {
        return Err(HelperError::Stage("IncompletePassiveContexts"));
    }
    let mut build_requirements = Vec::new();
    for project in &request.projects {
        let Some(strings) = &project.build_requirements else {
            continue;
        };
        let native = ProjectWorkspace::from_maybe_project_root(
            &project.directory,
            &discovery,
            &cache,
            &workspace_cache,
        )
        .await
        .map_err(|_| HelperError::Stage("NativeBuildContext"))?
        .ok_or(HelperError::Stage("MissingNamedBuildContext"))?;
        if native.project_root() != project.directory {
            return Err(HelperError::Stage("BuildContextIdentity"));
        }
        build_requirements.push(BuildRequirements {
            directory: project.directory.clone(),
            requirements: lower_build_requirements(
                &native,
                strings,
                &locations,
                &sources,
                &cache,
                &workspace_cache,
                &credentials_cache,
            )
            .await?,
        });
    }
    let mut group_operations = Vec::new();
    for operation in request.group_operations {
        if !native_directories.contains(&operation.directory) {
            return Err(HelperError::Stage("GroupContextIdentity"));
        }
        let native =
            VirtualProject::discover(&operation.directory, &discovery, &cache, &workspace_cache)
                .await
                .map_err(|_| HelperError::Stage("NativeGroupContext"))?;
        let selected = select_groups(&native, &operation.packages, operation.no_dev)?;
        let manifest = group_owner_manifest(
            native.pyproject_toml(),
            &operation.packages,
            native
                .workspace()
                .packages()
                .iter()
                .map(|(name, member)| (name, member.pyproject_toml())),
        )?;
        group_operations.push(GroupSelection {
            id: operation.id,
            groups: owner_selected_groups(manifest, &operation.directory, &selected)?,
        });
    }
    let interpreter = Interpreter::query(&request.interpreter, &cache)
        .map_err(|_| HelperError::Stage("NativeInterpreter"))?;
    let markers = request
        .markers
        .into_iter()
        .map(|input| {
            let marker = MarkerTree::from_str(&input.expression)
                .map_err(|_| HelperError::Stage("NativeMarker"))?;
            Ok(Activity {
                id: input.id,
                active: marker.evaluate(interpreter.markers(), &input.extras),
            })
        })
        .collect::<Result<_, HelperError>>()?;
    let python_constraints = request
        .python_constraints
        .into_iter()
        .map(|input| {
            let specifier = VersionSpecifiers::from_str(&input.specifier)
                .map_err(|_| HelperError::Stage("NativePythonConstraint"))?;
            Ok(Activity {
                id: input.id,
                active: specifier.contains(interpreter.python_version()),
            })
        })
        .collect::<Result<_, HelperError>>()?;
    Ok(Response {
        workspace_root: request.workspace_root,
        interpreter: request.interpreter,
        interpreter_markers: serde_json::to_value(interpreter.markers())
            .map_err(|_| HelperError::Stage("NativeMarkerTransport"))?,
        members,
        build_requirements,
        group_operations,
        markers,
        python_constraints,
    })
}

#[tokio::main]
async fn main() -> ExitCode {
    std::panic::set_hook(Box::new(|_| {
        eprintln!("Python native extraction failed (NativePanic).");
    }));
    let result = async {
        let mut arguments = std::env::args_os().skip(1);
        let path = arguments
            .next()
            .ok_or(HelperError::Stage("RequestArgument"))?;
        if arguments.next().is_some() {
            return Err(HelperError::Stage("RequestArgument"));
        }
        let input = fs::read(path).map_err(|_| HelperError::Stage("RequestRead"))?;
        let request =
            serde_json::from_slice(&input).map_err(|_| HelperError::Stage("RequestShape"))?;
        let response = extract(request).await?;
        serde_json::to_string(&response).map_err(|_| HelperError::Stage("ResponseShape"))
    }
    .await;
    match result {
        Ok(response) => {
            println!("{response}");
            ExitCode::SUCCESS
        }
        Err(error) => {
            eprintln!("{error}");
            ExitCode::FAILURE
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn request() -> Request {
        serde_json::from_value(json!({
            "workspace_root": "/endpoint",
            "interpreter": "/tools/python",
            "cache": "/owned/cache",
            "projects": [
                { "directory": "/endpoint", "build_requirements": null },
                { "directory": "/endpoint/plugin", "build_requirements": ["hatchling"] }
            ],
            "group_operations": [],
            "markers": [],
            "python_constraints": []
        }))
        .expect("complete internal request")
    }

    fn manifest(groups: &str) -> PyProjectToml {
        PyProjectToml::from_string(groups.into(), PathBuf::from("/controlled/pyproject.toml"))
            .expect("controlled native manifest")
    }

    fn selection(no_dev: bool) -> DependencyGroupsWithDefaults {
        use uv_configuration::{DependencyGroups, DevMode};
        use uv_normalize::DefaultGroups;
        DependencyGroups::from_args(
            no_dev.then_some(DevMode::Exclude),
            vec![],
            vec![],
            false,
            vec![],
            false,
        )
        .with_defaults(DefaultGroups::List(vec![
            "dev".parse().unwrap(),
            "quality".parse().unwrap(),
        ]))
    }

    #[test]
    fn owner_local_groups_do_not_inherit_other_manifests() {
        let root = manifest("[dependency-groups]\ndev = ['pytest']\n");
        let with_groups = manifest("[dependency-groups]\ndev = ['pytest']\nquality = ['ruff']\n");
        let without_groups = manifest("");
        let package: PackageName = "with-groups".parse().unwrap();
        let empty_package: PackageName = "without-groups".parse().unwrap();
        let members = [(&package, &with_groups), (&empty_package, &without_groups)];
        for (current, packages, expected) in [
            (&root, vec![], vec!["dev"]),
            (&with_groups, vec![], vec!["dev", "quality"]),
            (&without_groups, vec![], vec![]),
            (&root, vec![package.clone()], vec!["dev", "quality"]),
            (&root, vec![empty_package.clone()], vec![]),
            (&with_groups, vec![empty_package.clone()], vec![]),
        ] {
            let owner = group_owner_manifest(current, &packages, members.iter().copied())
                .expect("exact native owner");
            let result = owner_selected_groups(owner, Path::new("/controlled"), &selection(false))
                .expect("owner-local native groups");
            assert_eq!(
                result
                    .iter()
                    .map(|group| group.as_ref())
                    .collect::<Vec<_>>(),
                expected
            );
            let suppressed =
                owner_selected_groups(owner, Path::new("/controlled"), &selection(true))
                    .expect("native no-dev");
            assert_eq!(
                suppressed
                    .iter()
                    .map(|group| group.as_ref())
                    .collect::<Vec<_>>(),
                expected
                    .into_iter()
                    .filter(|name| *name != "dev")
                    .collect::<Vec<_>>()
            );
        }
    }

    #[test]
    fn native_group_presence_and_no_dev_preserve_owner_scope() {
        let legacy = manifest(
            "[tool.uv]\ndev-dependencies = ['pytest']\n[dependency-groups]\nquality = ['ruff']\n",
        );
        for (no_dev, expected) in [(false, vec!["dev", "quality"]), (true, vec!["quality"])] {
            let result =
                owner_selected_groups(&legacy, Path::new("/controlled"), &selection(no_dev))
                    .expect("native legacy presence and selection");
            assert_eq!(
                result
                    .iter()
                    .map(|group| group.as_ref())
                    .collect::<Vec<_>>(),
                expected
            );
        }
        let invalid = manifest("[dependency-groups]\nquality = [{include-group = 'missing'}]\n");
        assert_eq!(
            owner_selected_groups(&invalid, Path::new("/controlled"), &selection(false))
                .unwrap_err()
                .to_string(),
            "Python native extraction failed (NativeOwnerGroups)."
        );
    }

    #[test]
    fn empty_native_groups_do_not_require_graph_nodes() {
        for (source, ordinary, no_dev) in [
            ("[dependency-groups]\ndev = []\n", vec![], vec![]),
            ("[tool.uv]\ndev-dependencies = []\n", vec![], vec![]),
            (
                "[dependency-groups]\ndev = [{include-group = 'empty'}]\nempty = []\n",
                vec![],
                vec![],
            ),
            (
                "[dependency-groups]\ndev = []\nquality = ['ruff']\n",
                vec!["quality"],
                vec!["quality"],
            ),
            (
                "[dependency-groups]\ndev = [{include-group = 'tools'}]\ntools = ['pytest']\n",
                vec!["dev"],
                vec![],
            ),
        ] {
            let owner = manifest(source);
            for (excluded, expected) in [(false, ordinary), (true, no_dev)] {
                let answer =
                    owner_selected_groups(&owner, Path::new("/controlled"), &selection(excluded))
                        .expect("native graph contributions");
                assert_eq!(
                    answer
                        .iter()
                        .map(|group| group.as_ref())
                        .collect::<Vec<_>>(),
                    expected,
                    "source={source}, no_dev={excluded}"
                );
            }
        }
    }

    #[test]
    fn singleton_empty_group_does_not_inherit_root_contributions() {
        let root = manifest("[dependency-groups]\ndev = ['pytest']\n");
        let member = manifest("[dependency-groups]\ndev = []\n");
        let name: PackageName = "empty-member".parse().unwrap();
        let owner = group_owner_manifest(
            &root,
            std::slice::from_ref(&name),
            std::iter::once((&name, &member)),
        )
        .expect("singleton native owner");
        for no_dev in [false, true] {
            assert_eq!(
                owner_selected_groups(owner, Path::new("/controlled"), &selection(no_dev))
                    .expect("empty native contribution"),
                Vec::<GroupName>::new()
            );
        }
    }

    #[test]
    fn selected_empty_group_constraints_are_not_discarded() {
        for source in [
            "[dependency-groups]\ndev = []\n[tool.uv.dependency-groups]\ndev = {requires-python = '>=3.14'}\n",
            "[dependency-groups]\ndev = [{include-group = 'empty'}]\nempty = []\n[tool.uv.dependency-groups]\nempty = {requires-python = '>=3.14'}\n",
        ] {
            let owner = manifest(source);
            assert_eq!(
                owner_selected_groups(&owner, Path::new("/controlled"), &selection(false))
                    .unwrap_err()
                    .to_string(),
                "Python native extraction failed (EmptyGroupPythonConstraint)."
            );
            assert_eq!(
                owner_selected_groups(&owner, Path::new("/controlled"), &selection(true))
                    .expect("native dev exclusion"),
                Vec::<GroupName>::new()
            );
        }
    }

    #[test]
    fn unsupported_group_owner_requests_are_terminal() {
        let current = manifest("");
        let name: PackageName = "member".parse().unwrap();
        let missing: PackageName = "missing".parse().unwrap();
        let members = [(&name, &current)];
        for packages in [vec![missing], vec![name.clone(), name.clone()]] {
            assert_eq!(
                group_owner_manifest(&current, &packages, members.iter().copied())
                    .unwrap_err()
                    .to_string(),
                "Python native extraction failed (GroupOwnerIdentity)."
            );
        }
        let duplicate = [(&name, &current), (&name, &current)];
        assert_eq!(
            group_owner_manifest(
                &current,
                std::slice::from_ref(&name),
                duplicate.iter().copied()
            )
            .unwrap_err()
            .to_string(),
            "Python native extraction failed (GroupOwnerIdentity)."
        );
    }

    #[test]
    fn request_context_identity_is_checked_without_native_queries() {
        let valid = request();
        assert_eq!(
            request_directories(&valid).expect("valid request"),
            BTreeSet::from([
                PathBuf::from("/endpoint"),
                PathBuf::from("/endpoint/plugin")
            ])
        );
        for path in [PathBuf::from("relative"), PathBuf::from("/other/plugin")] {
            let mut invalid = request();
            invalid.projects[1].directory = path;
            assert_eq!(
                request_directories(&invalid).unwrap_err().to_string(),
                "Python native extraction failed (ProjectIdentity)."
            );
        }
        let mut duplicate = request();
        duplicate.projects[1].directory = duplicate.workspace_root.clone();
        assert_eq!(
            request_directories(&duplicate).unwrap_err().to_string(),
            "Python native extraction failed (ProjectIdentity)."
        );
        let mut missing_root = request();
        missing_root.projects.remove(0);
        assert_eq!(
            request_directories(&missing_root).unwrap_err().to_string(),
            "Python native extraction failed (MissingRootContext)."
        );
    }

    #[test]
    fn relative_operation_paths_fail_before_native_queries() {
        for field in ["workspace_root", "interpreter", "cache"] {
            let mut invalid = request();
            match field {
                "workspace_root" => invalid.workspace_root = PathBuf::from("relative"),
                "interpreter" => invalid.interpreter = PathBuf::from("relative"),
                "cache" => invalid.cache = PathBuf::from("relative"),
                _ => unreachable!(),
            }
            assert_eq!(
                request_directories(&invalid).unwrap_err().to_string(),
                "Python native extraction failed (AbsoluteRequestPaths)."
            );
        }
    }

    #[test]
    fn request_operation_identities_fail_before_native_queries() {
        let mut valid = request();
        valid.group_operations.push(GroupOperation {
            id: "quality".into(),
            directory: valid.workspace_root.clone(),
            packages: vec![],
            no_dev: false,
        });
        valid.markers.push(MarkerInput {
            id: "native-node-id".into(),
            expression: "python_version >= '3.14'".into(),
            extras: vec![],
        });
        valid.python_constraints.push(PythonConstraint {
            id: "native-node-id".into(),
            specifier: ">=3.14".into(),
        });
        assert!(request_directories(&valid).is_ok());
        valid.group_operations[0].directory = PathBuf::from("relative");
        assert_eq!(
            request_directories(&valid).unwrap_err().to_string(),
            "Python native extraction failed (GroupOperationIdentity)."
        );
        valid.group_operations[0].directory = valid.workspace_root.clone();
        valid.group_operations.push(GroupOperation {
            id: "quality".into(),
            directory: valid.workspace_root.clone(),
            packages: vec![],
            no_dev: false,
        });
        assert_eq!(
            request_directories(&valid).unwrap_err().to_string(),
            "Python native extraction failed (GroupOperationIdentity)."
        );
        valid.group_operations.pop();
        valid.markers[0].id = " ".into();
        assert_eq!(
            request_directories(&valid).unwrap_err().to_string(),
            "Python native extraction failed (MarkerIdentity)."
        );
        valid.markers[0].id = "native-node-id".into();
        valid.python_constraints.push(PythonConstraint {
            id: "native-node-id".into(),
            specifier: ">=3.14".into(),
        });
        assert_eq!(
            request_directories(&valid).unwrap_err().to_string(),
            "Python native extraction failed (PythonConstraintIdentity)."
        );
    }

    #[test]
    fn terminal_diagnostics_report_stage_or_native_error_kind_only() {
        assert_eq!(
            HelperError::Stage("NativeInterpreter").to_string(),
            "Python native extraction failed (NativeInterpreter)."
        );
        assert_eq!(
            HelperError::from(PlanningError::NativeLowering).to_string(),
            "Python native extraction failed (NativeLowering)."
        );
    }
}
