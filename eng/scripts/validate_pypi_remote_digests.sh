#!/usr/bin/env bash

# Validate PyPI remote digests for local release artifacts, to make publishing rerun-safe.
#
# Required env vars:
#   PROJECT   PyPI project name
#   VERSION   Release version (PEP 440)
#
# Optional env vars:
#   OUT_DIR   Directory containing local artifacts (default: "$GITHUB_WORKSPACE/out")
#
# Behavior:
# - If the project does not exist on PyPI (HTTP 404), treat as not yet published.
# - For each local file:
#   - If PyPI already has the filename for this version, require sha256 match.
#   - If PyPI does not have it, report it will be uploaded.

set -Eeuo pipefail

: "${PROJECT:?PROJECT is required}"
: "${VERSION:?VERSION is required}"

OUT_DIR="${OUT_DIR:-${GITHUB_WORKSPACE}/out}"

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required but not found on PATH." >&2
  exit 1
fi

api_url="https://pypi.org/pypi/${PROJECT}/json"
resp_json="${RUNNER_TEMP:-/tmp}/pypi.project.json"

set +e
status=$(curl -sS -L -o "${resp_json}" -w '%{http_code}' "${api_url}")
curl_rc=$?
set -e

if [[ "${curl_rc}" -ne 0 ]]; then
  echo "Failed to query PyPI project JSON (curl exit ${curl_rc})." >&2
  exit 1
fi

if [[ "${status}" == "404" ]]; then
  echo "PyPI project '${PROJECT}' not found (404). Treating as not yet published."
  exit 0
fi

if [[ "${status}" != "200" ]]; then
  echo "Unexpected PyPI response status: HTTP ${status}." >&2
  cat "${resp_json}" >&2 || true
  exit 1
fi

declare -A remote
while IFS=$'\t' read -r filename sha; do
  if [[ -n "${filename}" && -n "${sha}" ]]; then
    remote["${filename}"]="${sha}"
  fi
done < <(jq -r --arg v "${VERSION}" '.releases[$v][]? | "\(.filename)\t\(.digests.sha256)"' "${resp_json}")

shopt -s nullglob
files=("${OUT_DIR}"/*)
if [[ "${#files[@]}" -eq 0 ]]; then
  echo "No local artifacts found under '${OUT_DIR}'." >&2
  exit 1
fi

for f in "${files[@]}"; do
  base=$(basename "${f}")
  local_sha=$(sha256sum "${f}" | awk '{print $1}')

  if [[ -n "${remote[${base}]:-}" ]]; then
    if [[ "${remote[${base}]}" != "${local_sha}" ]]; then
      echo "PyPI already has file '${base}' for ${PROJECT} ${VERSION}, but sha256 differs." >&2
      echo "  remote: ${remote[${base}]}" >&2
      echo "  local:  ${local_sha}" >&2
      exit 1
    fi
    echo "PyPI already has '${base}' (sha256 match)."
  else
    echo "PyPI is missing '${base}' for ${PROJECT} ${VERSION}; it will be uploaded."
  fi
done
