//! Finite native UV operations used by Python planning extraction.

use std::str::FromStr;

use uv_auth::CredentialsCache;
use uv_cache::Cache;
use uv_configuration::{DependencyGroups, DependencyGroupsWithDefaults, DevMode, NoSources};
use uv_distribution::BuildRequires;
use uv_distribution_types::{IndexLocations, Requirement, RequirementSource};
use uv_normalize::PackageName;
use uv_pep508::{Requirement as ParsedRequirement, VersionOrUrl};
use uv_pypi_types::VerbatimParsedUrl;
use uv_settings::{GlobalOptions, Options, PythonInstallMirrors, ResolverInstallerSchema};
use uv_workspace::pyproject::{Source, Sources, WorkspaceReference};
use uv_workspace::{ProjectWorkspace, VirtualProject, WorkspaceCache};

/// Attribute native failures without exposing source-valued diagnostics.
#[derive(Debug, PartialEq, Eq)]
pub enum PlanningError {
    RequirementParse,
    UnsupportedSource,
    IdentityMismatch,
    NativeLowering,
    UnknownLocalProducer,
    GroupSelection,
    UnsupportedConfiguration,
}

/// Admit this default request before native conversion can discard settings.
#[allow(deprecated)]
pub fn admit_configuration(options: &Options) -> Result<(), PlanningError> {
    let GlobalOptions {
        required_version,
        system_certs,
        native_tls,
        offline,
        no_cache,
        cache_dir,
        preview,
        python_preference,
        python_downloads,
        concurrent_downloads,
        concurrent_builds,
        concurrent_installs,
        http_proxy,
        https_proxy,
        no_proxy,
        allow_insecure_host,
    } = &options.globals;
    if [
        required_version.is_some(),
        system_certs.is_some(),
        native_tls.is_some(),
        offline.is_some(),
        no_cache.is_some(),
        cache_dir.is_some(),
        preview.is_some(),
        python_preference.is_some(),
        python_downloads.is_some(),
        concurrent_downloads.is_some(),
        concurrent_builds.is_some(),
        concurrent_installs.is_some(),
        http_proxy.is_some(),
        https_proxy.is_some(),
        no_proxy.is_some(),
        allow_insecure_host.is_some(),
    ]
    .contains(&true)
        || options.top_level != ResolverInstallerSchema::default()
        || options.install_mirrors != PythonInstallMirrors::default()
        || options.cache_keys.is_some()
        || options.override_dependencies.is_some()
        || options.exclude_dependencies.is_some()
        || options.constraint_dependencies.is_some()
        || options.build_constraint_dependencies.is_some()
        || options.environments.is_some()
        || options.required_environments.is_some()
        || options.minimum_libc_version.is_some()
    {
        return Err(PlanningError::UnsupportedConfiguration);
    }
    // Publish, add, pip and audit sections belong to other native commands.
    Ok(())
}

/// Preserve passive producer strings until UV parses them.
pub fn parse_build_requirements(
    strings: &[String],
) -> Result<Vec<ParsedRequirement<VerbatimParsedUrl>>, PlanningError> {
    strings
        .iter()
        .map(|value| {
            let requirement =
                ParsedRequirement::from_str(value).map_err(|_| PlanningError::RequirementParse)?;
            if matches!(requirement.version_or_url, Some(VersionOrUrl::Url(_))) {
                return Err(PlanningError::UnsupportedSource);
            }
            Ok(requirement)
        })
        .collect()
}

fn admit_sources(sources: Option<&Sources>) -> Result<(), PlanningError> {
    if let Some(sources) = sources {
        for source in sources.iter() {
            if !matches!(
                source,
                Source::Workspace {
                    workspace: WorkspaceReference::Bool(true),
                    ..
                }
            ) {
                return Err(PlanningError::UnsupportedSource);
            }
        }
    }
    Ok(())
}

/// Lower a guarded named Hatchling context using the native workspace and settings.
///
/// The caller completes physical configuration guards and native workspace discovery
/// before this operation. This function does not discover foreign sources, fetch
/// packages, select installed versions or run build hooks.
pub async fn lower_build_requirements(
    project: &ProjectWorkspace,
    strings: &[String],
    locations: &IndexLocations,
    sources: &NoSources,
    cache: &Cache,
    workspace_cache: &WorkspaceCache,
    credentials_cache: &CredentialsCache,
) -> Result<Vec<Requirement>, PlanningError> {
    if !matches!(sources, NoSources::None) {
        return Err(PlanningError::UnsupportedSource);
    }
    let directory = project.project_root();
    let requirements = parse_build_requirements(strings)?;
    let project_sources = project
        .current_project()
        .pyproject_toml()
        .tool
        .as_ref()
        .and_then(|tool| tool.uv.as_ref())
        .and_then(|uv| uv.sources.as_ref())
        .map(|sources| sources.inner());
    for requirement in &requirements {
        admit_sources(project_sources.and_then(|sources| sources.get(&requirement.name)))?;
        admit_sources(project.workspace().sources().get(&requirement.name))?;
    }
    let result = BuildRequires::from_project_maybe_workspace(
        uv_pypi_types::BuildRequires {
            name: Some(project.project_name().clone()),
            requires_dist: requirements,
        },
        directory,
        locations,
        sources,
        true,
        None,
        cache,
        workspace_cache,
        credentials_cache,
    )
    .await
    .map_err(|_| PlanningError::NativeLowering)?;
    if result.name.as_ref() != Some(project.project_name()) {
        return Err(PlanningError::IdentityMismatch);
    }
    for requirement in &result.requires_dist {
        match &requirement.source {
            RequirementSource::Registry { .. } => {}
            RequirementSource::Directory { install_path, .. } => {
                if !project
                    .workspace()
                    .packages()
                    .values()
                    .any(|member| member.root() == install_path.as_ref())
                {
                    return Err(PlanningError::UnknownLocalProducer);
                }
            }
            _ => return Err(PlanningError::UnsupportedSource),
        }
    }
    Ok(result.requires_dist)
}

/// Apply UV's package defaults and no-dev semantics without a group interpreter.
pub fn select_groups(
    project: &VirtualProject,
    packages: &[PackageName],
    no_dev: bool,
) -> Result<DependencyGroupsWithDefaults, PlanningError> {
    let defaults = project
        .default_groups_for_packages(packages)
        .map_err(|_| PlanningError::GroupSelection)?;
    Ok(DependencyGroups::from_args(
        no_dev.then_some(DevMode::Exclude),
        vec![],
        vec![],
        false,
        vec![],
        false,
    )
    .with_defaults(defaults))
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn native_configuration_admission_preserves_defaults_and_rejects_relevant_settings() {
        assert_eq!(admit_configuration(&Options::default()), Ok(()));
        let irrelevant = serde_json::from_value::<Options>(json!({
            "publish-url": "https://example.invalid/upload",
            "pip": { "index-url": "https://example.invalid/simple" }
        }))
        .expect("native command-specific options");
        assert_eq!(admit_configuration(&irrelevant), Ok(()));
        for settings in [
            json!({ "offline": true }),
            json!({ "required-version": "==0.12.23" }),
            json!({ "cache-dir": "/unowned/cache" }),
            json!({ "keyring-provider": "subprocess" }),
            json!({ "no-sources": false }),
            json!({ "index-strategy": "unsafe-best-match" }),
            json!({ "config-settings": { "private-key": "private-value" } }),
            json!({ "constraint-dependencies": ["hatchling>=1.28"] }),
            json!({ "environments": ["sys_platform == 'linux'"] }),
            json!({ "cache-keys": [{ "file": "extra-input" }] }),
        ] {
            let options = serde_json::from_value::<Options>(settings)
                .expect("well-formed native options outside this request");
            assert_eq!(
                admit_configuration(&options),
                Err(PlanningError::UnsupportedConfiguration)
            );
        }
    }

    #[test]
    fn valid_build_batch_retains_native_fields() {
        let result = parse_build_requirements(&[
            "hatchling>=1.28".into(),
            "nbgv-python[build] ; python_version >= '3.14'".into(),
        ])
        .expect("supported native build strings");
        assert_eq!(result.len(), 2);
        assert_eq!(result[0].name.as_ref(), "hatchling");
        assert_eq!(result[1].name.as_ref(), "nbgv-python");
        assert_eq!(result[1].extras.len(), 1);
        assert_eq!(result[1].extras[0].as_ref(), "build");
        assert_eq!(
            result[0].version_or_url,
            Some(VersionOrUrl::VersionSpecifier(
                ">=1.28".parse().expect("native version specifier")
            ))
        );
        assert_eq!(
            result[1].marker,
            "python_version >= '3.14'".parse().expect("native marker")
        );
    }

    #[test]
    fn malformed_build_batch_is_terminal() {
        for invalid in [
            "",
            "nbgv-python[",
            "hatchling=>1",
            "private-token-sentinel @",
        ] {
            let result = parse_build_requirements(&[
                "hatchling>=1.28".into(),
                invalid.into(),
                "nbgv-python".into(),
            ]);
            assert_eq!(result.unwrap_err(), PlanningError::RequirementParse);
        }
    }

    #[test]
    fn direct_url_build_requirements_are_unsupported() {
        for source in [
            "nbgv-python @ https://example.invalid/plugin.whl",
            "nbgv-python @ git+https://example.invalid/plugin.git",
            "nbgv-python @ file:///unowned/plugin",
        ] {
            assert_eq!(
                parse_build_requirements(&[source.into()]).unwrap_err(),
                PlanningError::UnsupportedSource
            );
        }
    }

    #[test]
    fn workspace_sources_are_admitted_and_other_sources_rejected() {
        assert_eq!(admit_sources(None), Ok(()));
        let accepted = serde_json::from_value::<Sources>(json!({ "workspace": true }))
            .expect("native workspace source");
        assert_eq!(admit_sources(Some(&accepted)), Ok(()));
        for mut source in [
            json!({ "workspace": false }),
            json!({ "workspace": "../foreign" }),
            json!({ "git": "https://example.invalid/plugin.git" }),
            json!({ "url": "https://example.invalid/plugin.whl" }),
            json!({ "path": "../unowned" }),
            json!({ "index": "private" }),
        ] {
            let unsupported = serde_json::from_value::<Sources>(source.clone())
                .expect("native unsupported source");
            assert_eq!(
                admit_sources(Some(&unsupported)),
                Err(PlanningError::UnsupportedSource)
            );
            source
                .as_object_mut()
                .expect("source fixture object")
                .insert("marker".into(), json!("python_version < '3.14'"));
            let mixed = serde_json::from_value::<Sources>(json!([
                { "workspace": true, "marker": "python_version >= '3.14'" },
                source
            ]))
            .expect("native alternative sources");
            assert_eq!(
                admit_sources(Some(&mixed)),
                Err(PlanningError::UnsupportedSource)
            );
        }
    }
}
