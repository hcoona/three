#!/usr/bin/env bash
set -Eeuo pipefail

WORKFLOW_FILE=".github/workflows/release-orchestrate.yml"
FAIL=0

# check_version validates hardcoded 'key: value' pairs in the workflow file.
# It handles both lowercase with: keys (e.g. python_version: '3.14') and
# ALL_CAPS env: block keys (e.g. PYTHON_VERSION: '3.14'). The unquoted character
# class is broadened automatically for ALL_CAPS keys since their values may start
# with letters or dots (e.g. PNPM_VERSION: latest), while with: keys only expect digits.
check_version() {
  local key="$1"
  local expected="$2"
  # Escape '.' so it is treated as a literal character in -P regex, not a metacharacter.
  local expected_re
  expected_re=$(printf '%s' "${expected}" | sed 's/\./\\./g')
  local mismatches
  # Match lines with pinned version values in any of these forms:
  #   key: '3.14'   (single-quoted)
  #   key: "3.14"   (double-quoted)
  #   key: 3.14     (unquoted scalar — valid YAML for simple version strings)
  # Skip bare declaration lines (e.g. `python_version:` with no value) by
  # requiring at least one non-space character after the colon separator.
  mismatches=$(grep -n "^[[:space:]]*${key}:[[:space:]]*['\"]" "${WORKFLOW_FILE}" \
    | grep -vF "'${expected}'" \
    | grep -vF "\"${expected}\"" || true)
  # Also check for unquoted scalar values (e.g. `node_version: 24` or `NODE_VERSION: 24`).
  # For ALL_CAPS env var keys, use a broader character class since values may start with
  # letters or dots; lowercase with: keys only expect digit-prefixed version strings.
  local unquoted_start
  if [[ "${key}" =~ ^[A-Z_]+$ ]]; then
    unquoted_start="[0-9A-Za-z.]"
  else
    unquoted_start="[0-9]"
  fi
  local unquoted
  unquoted=$(grep -n "^[[:space:]]*${key}:[[:space:]]*${unquoted_start}" "${WORKFLOW_FILE}" \
    | grep -vP ":\s+${expected_re}([^0-9A-Za-z._]|$)" \
    | grep -vP ":${expected_re}([^0-9A-Za-z._]|$)" || true)
  if [[ -n "${unquoted}" ]]; then
    mismatches="${mismatches}"$'\n'"${unquoted}"
  fi
  mismatches="${mismatches#$'\n'}"
  if [[ -n "${mismatches}" ]]; then
    echo "ERROR: '${key}:' value(s) not matching expected '${expected}':" >&2
    echo "${mismatches}" >&2
    FAIL=1
  fi
  # Assert at least one occurrence of the key exists. A count of zero means
  # the key was renamed or removed and the mismatch check would silently pass.
  total=$(grep -cP "^\s*${key}:\s" "${WORKFLOW_FILE}" || true)
  if [[ "${total}" -eq 0 ]]; then
    echo "ERROR: Key '${key}:' not found in '${WORKFLOW_FILE}'." \
      "Was it renamed or deleted? Update both the key name and the EXPECTED_* variable." >&2
    FAIL=1
  fi
}

check_version "python_version" "${EXPECTED_PYTHON_VERSION}"
check_version "node_version"   "${EXPECTED_NODE_VERSION}"
check_version "pnpm_version"   "${EXPECTED_PNPM_VERSION}"
check_version "ruby_version"   "${EXPECTED_RUBY_VERSION}"
# SYNC: add-new-language — add check_version "<lang>_version" "${EXPECTED_<LANG>_VERSION}" call here

# Also validate the top-level ALL_CAPS env: block so that runtime
# tool versions stay in sync with the hardcoded with: values above.
check_version "PYTHON_VERSION" "${EXPECTED_PYTHON_VERSION}"
check_version "NODE_VERSION"   "${EXPECTED_NODE_VERSION}"
check_version "PNPM_VERSION"   "${EXPECTED_PNPM_VERSION}"
check_version "RUBY_VERSION"   "${EXPECTED_RUBY_VERSION}"
# SYNC: add-new-language — add check_version "<LANG>_VERSION" "${EXPECTED_<LANG>_VERSION}" call here

if [[ "${FAIL}" -ne 0 ]]; then
  echo "Version consistency check failed." \
    "Update hardcoded values in 'with:' blocks to match" \
    "the 'env:' declarations at the top of the workflow." >&2
  exit 1
fi
echo "All hardcoded tool versions are consistent."
