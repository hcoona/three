#!/usr/bin/env bash
set -Eeuo pipefail

api="repos/${GITHUB_REPOSITORY}/releases/tags/${TAG_NAME}"
resp_file="${RUNNER_TEMP}/gh-api.response"
rm -f "${resp_file}"

set +e
gh api "${api}" --include >"${resp_file}" 2>&1
rc=$?
set -e

status=$(grep -m1 -E '^HTTP/' "${resp_file}" | awk '{print $2}' || true)

if [[ "${rc}" -eq 0 ]]; then
  # Extract response body: skip HTTP headers up to and including the blank separator line.
  body=$(sed '1,/^[[:space:]]*$/d' "${resp_file}")
  if [[ -z "${body}" ]]; then
    echo "Error: gh api output did not contain a JSON body." >&2
    cat "${resp_file}" >&2 || true
    exit 1
  fi
  prerelease=$(printf '%s' "${body}" | jq -r '.prerelease')
  if [[ "${prerelease}" != "true" && "${prerelease}" != "false" ]]; then
    echo "Error: release JSON body missing 'prerelease'." >&2
    exit 1
  fi
  if [[ "${prerelease}" == "false" ]]; then
    echo "A non-prerelease GitHub Release already exists for tag '${TAG_NAME}'." >&2
    echo "Buddy releases must not modify official (prerelease=false) releases. Aborting." >&2
    echo "Note: force_update_tag=true controls whether the git tag can be moved, but does not bypass this prerelease-type protection." >&2
    exit 1
  fi
  echo "Existing release is prerelease=true for tag '${TAG_NAME}'. Proceeding."
  echo "### Guard: Non-clobber :white_check_mark: passed" >>"${GITHUB_STEP_SUMMARY:-/dev/null}" || true
  echo "Tag \`${TAG_NAME}\` — existing release is prerelease=true, safe to proceed." >>"${GITHUB_STEP_SUMMARY:-/dev/null}" || true
  exit 0
fi

if [[ "${status}" == "404" ]]; then
  echo "No existing GitHub Release found for tag '${TAG_NAME}'."
  # Secondary check: verify whether the git tag itself already exists.
  # A tag without a release can appear when a prior run created the tag but
  # the release step failed. Log a warning so it is visible in the run log.
  tag_resp="${RUNNER_TEMP}/gh-tag-api.response"
  set +e
  gh api "repos/${GITHUB_REPOSITORY}/git/refs/tags/${TAG_NAME}" --include >"${tag_resp}" 2>&1
  set -e
  tag_http=$(grep -m1 -E '^HTTP/' "${tag_resp}" | awk '{print $2}' || true)
  if [[ "${tag_http}" == "200" ]]; then
    tag_sha=$(sed '1,/^[[:space:]]*$/d' "${tag_resp}" | jq -r '.object.sha')
    echo "Warning: git tag '${TAG_NAME}' already exists (sha=${tag_sha}) but has no associated GitHub Release." >&2
    echo "This is expected if a previous run created the tag but failed before creating the release." >&2
  fi
  echo "### Guard: Non-clobber :white_check_mark: passed" >>"${GITHUB_STEP_SUMMARY:-/dev/null}" || true
  echo "Tag \`${TAG_NAME}\` — no existing release found, safe to proceed." >>"${GITHUB_STEP_SUMMARY:-/dev/null}" || true
  exit 0
fi

echo "Failed to query release metadata for tag '${TAG_NAME}' (HTTP ${status:-unknown})." >&2
cat "${resp_file}" >&2 || true
exit 1
