#!/usr/bin/env bash
set -Eeuo pipefail

# SYNC[add-new-language] — when adding a new language registry target (e.g., NuGet for C#),
# add the corresponding PUBLISH_<LANG>_<REGISTRY> variable check in this file.
# This file validates cross-kind contamination and emits GPR permission warnings.

if [[ -z "${PROJECT_KIND:-}" ]]; then
  echo "PROJECT_KIND is empty; upstream resolve job output contract is violated. Cannot validate publish-target policy." >&2
  exit 1
fi

# Contract: IS_WXT must be non-empty and exactly 'true' or 'false' for node projects.
if [[ "${PROJECT_KIND}" == "node" && -z "${IS_WXT:-}" ]]; then
  echo "IS_WXT is empty for PROJECT_KIND=node. The upstream release-resolve job output contract requires exactly 'true' or 'false'." >&2
  exit 1
fi
# Non-canonical values ('yes', '1', 'TRUE', etc.) are not empty but fail the
# == 'true' routing branch below, silently treating WXT projects as node-npm.
if [[ "${PROJECT_KIND}" == "node" && "${IS_WXT:-}" != "true" && "${IS_WXT:-}" != "false" ]]; then
  echo "IS_WXT has non-canonical value '${IS_WXT:-}' for PROJECT_KIND=node. The upstream release-resolve job output contract requires exactly 'true' or 'false'." >&2
  exit 1
fi

# NOTE: If publish_node_gpr or publish_ruby_gpr is true, the caller workflow
# MUST grant packages: write to the orchestrate job (workflow_call does not inherit
# permissions automatically). A missing permission causes a silent API failure at
# runtime, not a policy error. Both official.yml and buddy.yml
# are expected to include packages: write. Verify any new caller workflow does
# the same.
if [[ "${PUBLISH_NODE_GPR}" == "true" || "${PUBLISH_RUBY_GPR}" == "true" ]]; then
  echo "ℹ️ GPR publishing is enabled (publish_node_gpr=${PUBLISH_NODE_GPR}, publish_ruby_gpr=${PUBLISH_RUBY_GPR}). Ensure the caller workflow grants \`packages: write\`." >>"${GITHUB_STEP_SUMMARY:-/dev/null}" || true
fi

# assert_disabled: Checks that a publish flag is false for a project kind that
# does not use it (cross-kind contamination check). This is only enforced for
# custom allowlisted channels. For official/buddy channels the policy job already
# validates all flags, so this function is intentionally a no-op for them.
assert_disabled() {
  local flag_name="$1"
  local flag_value="$2"
  # No-op for official/buddy: the policy job enforces correct flag values
  # for these channels; cross-kind contamination cannot occur in practice.
  if [[ "${CHANNEL}" == "official" || "${CHANNEL}" == "buddy" ]]; then
    return 0
  fi
  if [[ "${flag_value}" == "true" ]]; then
    echo "Cross-kind contamination: '${flag_name}' is 'true' for a '${PROJECT_KIND}' project '${PROJECT}'; this flag has no effect and indicates a misconfiguration." >&2
    exit 1
  fi
}

require_registry_target_or_release_only_path() {
  local language_name="$1"
  local registry_label="$2"
  local gpr_flag_name="$3"
  local gpr_flag_value="$4"
  local public_registry_flag_name="$5"
  local public_registry_flag_value="$6"

  if [[ "${gpr_flag_value}" == "true" || "${public_registry_flag_value}" == "true" ]]; then
    return 0
  fi

  if [[ "${CHANNEL}" == "official" || "${CHANNEL}" == "buddy" ]]; then
    echo "${language_name} project '${PROJECT}' requires at least one ${registry_label} publish target for built-in channel '${CHANNEL}' (${gpr_flag_name}/${public_registry_flag_name}). Got: ${gpr_flag_name}=${gpr_flag_value}, ${public_registry_flag_name}=${public_registry_flag_value}." >&2
    exit 1
  fi

  local release_only_message
  release_only_message="${language_name} project '${PROJECT}' has no ${registry_label} registry publish targets enabled for custom channel '${CHANNEL}'. This is allowed only for descriptor-valid GitHub Release delivery; descriptor authoring and prepare-release-plan remain the fail-closed gates for zero-target or package-only/no-GitHub-Release profiles."
  echo "${release_only_message}"
  {
    echo ""
    echo "> [!NOTE]"
    echo "> ${release_only_message}"
  } >>"${GITHUB_STEP_SUMMARY:-/dev/null}" || true
}

if [[ "${PROJECT_KIND}" == "node" && "${IS_WXT:-}" != "true" ]]; then
  require_registry_target_or_release_only_path "Node" "Node" "publish_node_gpr" "${PUBLISH_NODE_GPR}" "publish_node_npmjs" "${PUBLISH_NODE_NPMJS}"
  assert_disabled "publish_python_pypi" "${PUBLISH_PYTHON_PYPI}"
  assert_disabled "publish_ruby_gpr" "${PUBLISH_RUBY_GPR}"
  assert_disabled "publish_ruby_rubygems" "${PUBLISH_RUBY_RUBYGEMS}"
  echo "Node publish-target policy passed for '${PROJECT}'."
  exit 0
fi

if [[ "${PROJECT_KIND}" == "ruby" ]]; then
  require_registry_target_or_release_only_path "Ruby" "Ruby" "publish_ruby_gpr" "${PUBLISH_RUBY_GPR}" "publish_ruby_rubygems" "${PUBLISH_RUBY_RUBYGEMS}"
  assert_disabled "publish_python_pypi" "${PUBLISH_PYTHON_PYPI}"
  assert_disabled "publish_node_gpr" "${PUBLISH_NODE_GPR}"
  assert_disabled "publish_node_npmjs" "${PUBLISH_NODE_NPMJS}"
  echo "Ruby publish-target policy passed for '${PROJECT}'."
  exit 0
fi

if [[ "${PROJECT_KIND}" == "python" ]]; then
  if [[ "${PUBLISH_PYTHON_PYPI}" != "true" ]]; then
    echo "⚠️ Python project '${PROJECT}' will not publish to PyPI (publish_python_pypi=false). Only a GitHub Release will be created." >>"${GITHUB_STEP_SUMMARY:-/dev/null}" || true
  fi
  assert_disabled "publish_node_gpr" "${PUBLISH_NODE_GPR}"
  assert_disabled "publish_node_npmjs" "${PUBLISH_NODE_NPMJS}"
  assert_disabled "publish_ruby_gpr" "${PUBLISH_RUBY_GPR}"
  assert_disabled "publish_ruby_rubygems" "${PUBLISH_RUBY_RUBYGEMS}"
  echo "Python publish-target policy passed for '${PROJECT}'."
  exit 0
fi

if [[ "${PROJECT_KIND}" == "dotnet" ]]; then
  assert_disabled "publish_python_pypi" "${PUBLISH_PYTHON_PYPI}"
  assert_disabled "publish_node_gpr" "${PUBLISH_NODE_GPR}"
  assert_disabled "publish_node_npmjs" "${PUBLISH_NODE_NPMJS}"
  assert_disabled "publish_ruby_gpr" "${PUBLISH_RUBY_GPR}"
  assert_disabled "publish_ruby_rubygems" "${PUBLISH_RUBY_RUBYGEMS}"
  echo ".NET project '${PROJECT}' uses GitHub Release artifacts only; NuGet registry publishing is deferred."
  exit 0
fi

# WXT projects are distributed as browser extension archives, not npm packages;
# all publish_node_* flags are intentionally ignored for WXT projects.
if [[ "${PROJECT_KIND}" == "node" && "${IS_WXT:-}" == "true" ]]; then
  # assert_disabled is a no-op for official/buddy channels (policy job already
  # validated all flags). For custom allowlisted channels it enforces that no
  # publish_node_* flags are erroneously set to true.
  assert_disabled "publish_python_pypi" "${PUBLISH_PYTHON_PYPI}"
  assert_disabled "publish_node_gpr" "${PUBLISH_NODE_GPR}"
  assert_disabled "publish_node_npmjs" "${PUBLISH_NODE_NPMJS}"
  assert_disabled "publish_ruby_gpr" "${PUBLISH_RUBY_GPR}"
  assert_disabled "publish_ruby_rubygems" "${PUBLISH_RUBY_RUBYGEMS}"
  echo "WXT project '${PROJECT}' skips registry publish-target policy (browser extension distribution)."
  echo "ℹ️ WXT project \`${PROJECT}\`: publish_node_gpr=${PUBLISH_NODE_GPR}, publish_node_npmjs=${PUBLISH_NODE_NPMJS} are ignored (browser extension; not published to npm registries)." >>"${GITHUB_STEP_SUMMARY:-/dev/null}" || true
  # NOTE: For official channel runs, both publish_node_gpr=true and publish_node_npmjs=true
  # are required by channel policy (the policy job mandates them before project_kind is known).
  # For WXT projects both flags have no runtime effect — WXT artifacts are distributed as
  # browser extension archives, not npm packages, and no Node pipeline jobs execute for WXT builds.
  if [[ "${CHANNEL}" == "official" && ("${PUBLISH_NODE_NPMJS}" == "true" || "${PUBLISH_NODE_GPR}" == "true") ]]; then
    echo "ℹ️ Official channel note: \`publish_node_gpr=true\` and \`publish_node_npmjs=true\` are required by channel policy but have no effect for WXT projects (browser extension; npm publishing is skipped)." >>"${GITHUB_STEP_SUMMARY:-/dev/null}" || true
  fi
  exit 0
fi

echo "Unexpected project kind '${PROJECT_KIND}' while validating publish-target policy." >&2
exit 1
