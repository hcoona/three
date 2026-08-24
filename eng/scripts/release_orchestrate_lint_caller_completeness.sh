#!/usr/bin/env bash
set -Eeuo pipefail

ORCHESTRATE_FILE=".github/workflows/release-orchestrate.yml"
OFFICIAL_FILE=".github/workflows/official.yml"
FAIL=0

# Extract all publish_* input names declared in the orchestrate workflow inputs: block.
# Each one must be explicitly passed in every caller workflow so that a new input
# added to the orchestrate file (Step A) is not silently omitted by callers (Steps F, G).
# SYNC[add-new-language] — this step auto-detects new publish_* inputs; no manual update needed.
mapfile -t publish_inputs < <(
  grep -P '^      publish_\w+:$' "${ORCHESTRATE_FILE}" | grep -oP 'publish_\w+' | sort -u
)

for flag in "${publish_inputs[@]}"; do
  if ! grep -qP "^\s+${flag}:" "${OFFICIAL_FILE}"; then
    echo "ERROR: '${flag}' is declared in release-orchestrate.yml inputs but not explicitly passed in '${OFFICIAL_FILE}'." >&2
    FAIL=1
  fi
done

if [[ "${FAIL}" -ne 0 ]]; then
  echo "Caller completeness check failed." >&2
  echo "When adding a new publish_* input to release-orchestrate.yml (Step A)," >&2
  echo "you MUST also pass it explicitly in official.yml (Step F)." >&2
  exit 1
fi
echo "Caller completeness check passed: all publish_* inputs are explicitly passed in official.yml."
