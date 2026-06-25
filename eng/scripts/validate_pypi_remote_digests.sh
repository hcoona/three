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
# - If the version does not exist on PyPI, treat as not yet published.
# - If the version exists, require the remote file set to exactly match the
#   local planned artifact filenames and sha256 digests before publishing.

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

if ! release_state=$(jq -r --arg v "${VERSION}" '
  if (.releases | type) != "object" then
    "malformed"
  elif (.releases | has($v)) then
    "present"
  else
    "absent"
  end
' "${resp_json}"); then
  echo "PyPI JSON API payload for '${PROJECT}' is malformed." >&2
  exit 1
fi

case "${release_state}" in
absent)
  echo "PyPI version '${VERSION}' for project '${PROJECT}' not found. Treating as not yet published."
  exit 0
  ;;
present) ;;
*)
  echo "PyPI JSON API payload for '${PROJECT}' is missing a valid releases object." >&2
  exit 1
  ;;
esac

if ! release_type=$(jq -r --arg v "${VERSION}" '.releases[$v] | type' "${resp_json}"); then
  echo "PyPI JSON API payload for '${PROJECT}' is malformed." >&2
  exit 1
fi
if [[ "${release_type}" != "array" ]]; then
  echo "PyPI JSON API payload for '${PROJECT}' version '${VERSION}' has malformed release files." >&2
  exit 1
fi

if ! remote_rows=$(jq -r --arg v "${VERSION}" '
  .releases[$v][]
  | if type != "object" then
      error("malformed release file")
    elif (.filename | type) != "string" or .filename == "" then
      error("malformed release file filename")
    elif (.digests | type) != "object" then
      error("malformed release file digests")
    elif (.digests.sha256 | type) != "string" then
      error("malformed release file sha256")
    elif (.digests.sha256 | test("^[0-9a-f]{64}$") | not) then
      error("malformed release file sha256")
    else
      [.filename, .digests.sha256] | @tsv
    end
' "${resp_json}"); then
  echo "PyPI JSON API payload for '${PROJECT}' version '${VERSION}' has malformed release files." >&2
  exit 1
fi

declare -A remote
if [[ -n "${remote_rows}" ]]; then
  while IFS=$'\t' read -r filename sha; do
    if [[ -z "${filename}" || -z "${sha}" || "${sha}" == "null" ]]; then
      echo "PyPI JSON API payload for '${PROJECT}' version '${VERSION}' contains a release file without filename or sha256." >&2
      exit 1
    fi
    if [[ -n "${remote[${filename}]+set}" ]]; then
      echo "PyPI has duplicate file '${filename}' for ${PROJECT} ${VERSION}; remote file set is ambiguous." >&2
      exit 1
    fi
    remote["${filename}"]="${sha}"
  done <<<"${remote_rows}"
fi

shopt -s nullglob
files=("${OUT_DIR}"/*)
if [[ "${#files[@]}" -eq 0 ]]; then
  echo "No local artifacts found under '${OUT_DIR}'." >&2
  exit 1
fi

declare -A local_sha_by_filename
for f in "${files[@]}"; do
  if [[ ! -f "${f}" ]]; then
    echo "Local artifact path '${f}' is not a regular file." >&2
    exit 1
  fi
  base=$(basename "${f}")
  local_sha_by_filename["${base}"]=$(sha256sum "${f}" | awk '{print $1}')
done

for remote_filename in "${!remote[@]}"; do
  if [[ -z "${local_sha_by_filename[${remote_filename}]+set}" ]]; then
    echo "PyPI already has unexpected file '${remote_filename}' for ${PROJECT} ${VERSION}; remote file set is not exact." >&2
    exit 1
  fi
done

for f in "${files[@]}"; do
  base=$(basename "${f}")
  local_sha="${local_sha_by_filename[${base}]}"
  if [[ -n "${remote[${base}]:-}" ]]; then
    if [[ "${remote[${base}]}" != "${local_sha}" ]]; then
      echo "PyPI already has file '${base}' for ${PROJECT} ${VERSION}, but sha256 differs." >&2
      echo "  remote: ${remote[${base}]}" >&2
      echo "  local:  ${local_sha}" >&2
      exit 1
    fi
    echo "PyPI already has '${base}' (sha256 match)."
  else
    echo "PyPI is missing planned file '${base}' for ${PROJECT} ${VERSION}; remote file set is not exact." >&2
    exit 1
  fi
done
