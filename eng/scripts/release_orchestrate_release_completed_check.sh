#!/usr/bin/env bash
set -Eeuo pipefail

# Dynamically process all jobs in needs: using toJSON(needs).
# This prevents the 3-way sync problem where needs:, env:, and for-loop
# all had to be manually updated when adding new language pipelines.
echo "Release job outcomes:"
failed=()
success_count=0

{
  echo "## Release Job Outcomes"
  echo "| Job | Result |"
  echo "| --- | ------ |"
} >>"${GITHUB_STEP_SUMMARY}"

while IFS= read -r line; do
  job="${line%%:*}"
  status="${line##*:}"
  echo "  ${job}: ${status}"
  case "${status}" in
  # Only release-* delivery jobs count toward success_count.
  # attest-* gate jobs exit 0 (success) when builds fail; including them
  # here would mask a genuine "no release delivered" condition.
  success)
    icon=":white_check_mark:"
    [[ "${job}" == release-* ]] && success_count=$((success_count + 1))
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

if [[ "${#failed[@]}" -gt 0 ]]; then
  echo "One or more release pipeline jobs failed: ${failed[*]}" >&2
  {
    echo ""
    echo "> [!CAUTION]"
    echo "> One or more release pipeline jobs failed: ${failed[*]}"
  } >>"${GITHUB_STEP_SUMMARY}"
  exit 1
fi

if [[ "${success_count}" -eq 0 ]]; then
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

echo "All release jobs completed successfully (skipped jobs are expected for non-matching project kinds)."
