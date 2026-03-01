#!/usr/bin/env bash
set -Eeuo pipefail

WORKFLOW_FILE=".github/workflows/release-orchestrate.yml"
FAIL=0

# Extract all top-level release-* job keys (except release-completed itself).
# Top-level jobs are indented with exactly 2 spaces followed by a colon.
mapfile -t release_jobs < <(
  grep -P '^  (release-[a-z0-9-]+):$' "${WORKFLOW_FILE}" \
    | grep -oP 'release-[a-z0-9-]+' \
    | grep -v '^release-completed$' \
    | sort -u
)

# Extract the needs: list of release-completed using awk.
mapfile -t needs_entries < <(
  awk '
    /^  release-completed:/ { in_job=1; next }
    in_job && /^    needs:/ { in_needs=1; next }
    in_job && in_needs && /^      - / { sub(/^      - /, ""); print; next }
    in_job && in_needs && !/^      / { in_needs=0 }
    in_job && /^  [a-z]/ { in_job=0 }
  ' "${WORKFLOW_FILE}" | sort -u
)

for job in "${release_jobs[@]}"; do
  found=0
  for entry in "${needs_entries[@]}"; do
    [[ "${entry}" == "${job}" ]] && found=1 && break
  done
  if [[ "${found}" -eq 0 ]]; then
    echo "ERROR: release-* job '${job}' is not listed in release-completed's needs:." >&2
    echo "Add '${job}' to the needs: list of release-completed (SYNC[add-new-language] step E)." >&2
    FAIL=1
  fi
done

# Also verify that every attest-* gate job (not *-enabled) is listed in release-completed's needs:.
# These are not auto-detected by the release-* check above; this closes the CI-enforcement gap
# for the attest-<lang> entries documented in the SYNC[add-new-language] checklist.
mapfile -t attest_gate_jobs < <(
  grep -P '^  (attest-[a-z0-9-]+):$' "${WORKFLOW_FILE}" \
    | grep -oP 'attest-[a-z0-9-]+' \
    | grep -v '\-enabled$' \
    | sort -u
)

for job in "${attest_gate_jobs[@]}"; do
  found=0
  for entry in "${needs_entries[@]}"; do
    [[ "${entry}" == "${job}" ]] && found=1 && break
  done
  if [[ "${found}" -eq 0 ]]; then
    echo "ERROR: attest-* gate job '${job}' is not listed in release-completed's needs:." >&2
    echo "Add '${job}' to the needs: list of release-completed (SYNC[add-new-language] step E)." >&2
    FAIL=1
  fi
done

if [[ "${FAIL}" -ne 0 ]]; then
  exit 1
fi
echo "release-completed needs: coverage check passed (release-* and attest-* gates)."
