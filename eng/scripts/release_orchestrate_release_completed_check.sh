#!/usr/bin/env bash
set -Eeuo pipefail

# Dynamically process all jobs in needs: using toJSON(needs).
# This prevents the 3-way sync problem where needs:, env:, and for-loop
# all had to be manually updated when adding new language pipelines.
echo "Release pipeline job outcomes:"
failed=()
active_publish_failures=()
success_count=0

{
  echo "## Release Pipeline Job Outcomes"
  echo "| Job | Result |"
  echo "| --- | ------ |"
} >>"${GITHUB_STEP_SUMMARY}"

while IFS= read -r line; do
  job="${line%%:*}"
  status="${line##*:}"
  echo "  ${job}: ${status}"
  case "${status}" in
  # This generic scan counts only release-* delivery jobs. Active registry
  # publish jobs/gates are counted below only when the finalized plan says the
  # corresponding publish node is active, so disabled no-op gates cannot mask a
  # genuine "no release delivered" condition.
  success)
    icon=":white_check_mark:"
    if [[ "${job}" == release-* ]]; then
      success_count=$((success_count + 1))
    fi
    ;;
  skipped) icon=":white_circle:" ;;
  failure)
    icon=":x:"
    failed+=("${job}(${status})")
    ;;
  cancelled)
    icon=":no_entry:"
    failed+=("${job}(${status})")
    ;;
  *)
    icon=":question:"
    failed+=("${job}(${status}?)")
    ;;
  esac
  echo "| \`${job}\` | ${icon} ${status} |" >>"${GITHUB_STEP_SUMMARY}"
done < <(echo "${NEEDS_JSON}" | jq -r 'to_entries[] | "\(.key):\(.value.result)"')

plan_output() {
  local name="$1"
  echo "${NEEDS_JSON}" | jq -r --arg name "${name}" '."prepare-release-plan".outputs[$name] // "false"'
}

need_result() {
  local job="$1"
  echo "${NEEDS_JSON}" | jq -r --arg job "${job}" '.[$job].result // ""'
}

count_active_publish_delivery() {
  local active_output="$1"
  local job="$2"
  local label="$3"
  local publish_flag="$4"
  local result

  if [[ "${publish_flag}" != "true" ]]; then
    return 0
  fi

  if [[ "$(plan_output "${active_output}")" != "true" ]]; then
    return 0
  fi

  result="$(need_result "${job}")"
  case "${result}" in
  success)
    echo "  active publish satisfied: ${label} (${job})"
    success_count=$((success_count + 1))
    ;;
  failure | cancelled)
    # The generic needs scan above already records this as a job failure.
    ;;
  skipped | "")
    active_publish_failures+=("${job}(${result:-missing})")
    ;;
  *)
    active_publish_failures+=("${job}(${result}?)")
    ;;
  esac
}

has_active_package_registry_publish() {
  local active_output publish_flag
  while IFS=: read -r active_output publish_flag; do
    if [[ "${publish_flag}" == "true" && "$(plan_output "${active_output}")" == "true" ]]; then
      return 0
    fi
  done <<EOF
has_active_python_pypi:${PUBLISH_PYTHON_PYPI:-true}
has_active_node_gpr:${PUBLISH_NODE_GPR:-true}
has_active_node_npmjs:${PUBLISH_NODE_NPMJS:-true}
has_active_ruby_gpr:${PUBLISH_RUBY_GPR:-true}
has_active_ruby_rubygems:${PUBLISH_RUBY_RUBYGEMS:-true}
EOF
  return 1
}

count_active_publish_delivery "has_active_python_pypi" "publish-python" "Python PyPI" "${PUBLISH_PYTHON_PYPI:-true}"
count_active_publish_delivery "has_active_node_gpr" "publish-node-gpr" "Node GitHub Packages" "${PUBLISH_NODE_GPR:-true}"
count_active_publish_delivery "has_active_node_npmjs" "publish-node-npmjs" "Node npmjs" "${PUBLISH_NODE_NPMJS:-true}"
count_active_publish_delivery "has_active_ruby_gpr" "publish-ruby-gpr" "Ruby GitHub Packages" "${PUBLISH_RUBY_GPR:-true}"
count_active_publish_delivery "has_active_ruby_rubygems" "publish-ruby-rubygems" "RubyGems.org" "${PUBLISH_RUBY_RUBYGEMS:-true}"
has_skip_results="$(plan_output "has_skip_results")"

if [[ "${#failed[@]}" -gt 0 ]]; then
  echo "One or more release pipeline jobs failed: ${failed[*]}" >&2
  {
    echo ""
    echo "> [!CAUTION]"
    echo "> One or more release pipeline jobs failed: ${failed[*]}"
  } >>"${GITHUB_STEP_SUMMARY}"
  exit 1
fi

if [[ "${#active_publish_failures[@]}" -gt 0 ]]; then
  echo "One or more active registry publish jobs did not complete successfully: ${active_publish_failures[*]}" >&2
  {
    echo ""
    echo "> [!CAUTION]"
    echo "> One or more active registry publish jobs did not complete successfully: ${active_publish_failures[*]}"
  } >>"${GITHUB_STEP_SUMMARY}"
  exit 1
fi

if [[ "$(plan_output "has_active_github_release")" == "true" ]] || has_active_package_registry_publish || [[ "${has_skip_results}" == "true" ]]; then
  if [[ -z "${ARTIFACTS_ROOT:-}" || -z "${RUN_ID:-}" || -z "${RUN_ATTEMPT:-}" ]]; then
    echo "Release completion receipt validation requires ARTIFACTS_ROOT, RUN_ID, and RUN_ATTEMPT." >&2
    {
      echo ""
      echo "> [!CAUTION]"
      echo "> Release completion receipt validation requires ARTIFACTS_ROOT, RUN_ID, and RUN_ATTEMPT."
    } >>"${GITHUB_STEP_SUMMARY}"
    exit 1
  fi
  disabled_package_target_keys=()
  [[ "${PUBLISH_PYTHON_PYPI:-true}" == "true" ]] || disabled_package_target_keys+=("python-pypi")
  [[ "${PUBLISH_NODE_GPR:-true}" == "true" ]] || disabled_package_target_keys+=("node-gpr")
  [[ "${PUBLISH_NODE_NPMJS:-true}" == "true" ]] || disabled_package_target_keys+=("node-npmjs")
  [[ "${PUBLISH_RUBY_GPR:-true}" == "true" ]] || disabled_package_target_keys+=("ruby-gpr")
  [[ "${PUBLISH_RUBY_RUBYGEMS:-true}" == "true" ]] || disabled_package_target_keys+=("ruby-rubygems")
  disabled_package_target_keys_csv="$(
    IFS=,
    printf '%s' "${disabled_package_target_keys[*]}"
  )"
  if ! uv run python eng/scripts/workflow_release_control.py release-completed-receipts \
    --artifacts-root "${ARTIFACTS_ROOT}" \
    --run-id "${RUN_ID}" \
    --attempt "${RUN_ATTEMPT}" \
    --plan-artifact-name "$(plan_output "plan_artifact_name")" \
    --execution-sets-artifact-name "$(plan_output "execution_sets_artifact_name")" \
    --entry-publish-handoff-artifact-name "$(plan_output "entry_publish_handoff_artifact_name")" \
    --disabled-package-target-keys "${disabled_package_target_keys_csv}"; then
    echo "Release completion requires valid receipts for every active or skip-satisfied publish node." >&2
    {
      echo ""
      echo "> [!CAUTION]"
      echo "> Release completion requires valid receipts for every active or skip-satisfied publish node."
    } >>"${GITHUB_STEP_SUMMARY}"
    exit 1
  fi
  echo "  release completion satisfied: completion receipts"
  if [[ "$(plan_output "has_active_github_release")" == "true" ]]; then
    success_count=$((success_count + 1))
  fi
fi

if [[ "${success_count}" -eq 0 ]]; then
  plan_result=$(echo "${NEEDS_JSON}" | jq -r '."prepare-release-plan".result // ""')
  has_live_side_effects=$(echo "${NEEDS_JSON}" | jq -r '."prepare-release-plan".outputs.has_live_side_effects // "true"')
  if [[ "${plan_result}" == "success" && "${has_live_side_effects}" == "false" ]]; then
    if [[ "${has_skip_results}" == "true" ]]; then
      terminal_reason="the finalized execution sets contain only skip-satisfied publish nodes"
      terminal_summary="The finalized plan was already satisfied; skip-satisfied publish receipts are the terminal release outcome for this idempotent rerun."
    else
      terminal_reason="the finalized execution sets contain no live publish side effects"
      terminal_summary="The finalized plan selected no active publish nodes, so no release job is required."
    fi
    echo "No release job succeeded because ${terminal_reason}."
    {
      echo ""
      echo "> [!NOTE]"
      echo "> No release job ran because ${terminal_reason}. ${terminal_summary}"
    } >>"${GITHUB_STEP_SUMMARY}"
    exit 0
  fi

  # NOTE: This path fires when a guard, policy, publish, or build job fails and
  # cascade-skips its downstream release-* job. Cascade-skipped jobs appear as
  # "skipped" in toJSON(needs), not "failure", so the failure is invisible here.
  # Use the GitHub Actions job visualizer to identify the root cause. Common
  # upstream jobs to inspect: guard-prerelease-only, guard-non-clobber,
  # policy-publish-targets, publish-node-gpr, publish-node-npmjs, publish-ruby-gpr.
  echo "No release job succeeded. This may indicate: (1) an upstream gate failure (policy, guards, or publish), (2) a build step failure, or (3) all projects of the detected kind were skipped. Inspect the GitHub Actions job visualizer for upstream failures." >&2
  {
    echo ""
    echo "> [!CAUTION]"
    echo "> No release job succeeded. This may indicate:"
    echo "> 1. An upstream gate failure — inspect **guard-prerelease-only**, **guard-non-clobber**, **policy-publish-targets**, **publish-node-gpr**, **publish-node-npmjs**, or **publish-ruby-gpr**."
    echo "> 2. A build or publish step failure."
    echo "> 3. All projects of the detected kind were skipped."
  } >>"${GITHUB_STEP_SUMMARY}"
  exit 1
fi

echo "All release or active registry publish jobs completed successfully with required receipts (skipped jobs are expected for non-matching project kinds and skip-satisfied GitHub Releases)."
