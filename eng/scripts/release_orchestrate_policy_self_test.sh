#!/usr/bin/env bash
set -Eeuo pipefail

WORKFLOW_FILE=".github/workflows/release-orchestrate.yml"
FAIL=0

# For each flag that must be validated per-channel, verify that assert_equals
# appears at least twice in the policy job (once for official, once in buddy).
# A count < 2 means the flag was added to one branch but not the other,
# which causes a silent policy bypass for the missing channel.
# SYNC: add-new-language — add the new publish_<lang>_<registry> flag name(s) here.
# Omitting this step means future regressions (removing assert_equals for the new flag
# from one branch) will not be caught by the self-test.
# NOTE: force_update_tag is intentionally excluded from this list.
# It is a per-run operational override, not a channel profile flag, and is not asserted
# by the policy job for either channel. Add channel profile flags only.
# NOTE: This list is manually maintained. Adding a new publish_<lang>_<registry> input
# requires updating BOTH this list AND the assert_equals calls in the case branches
# (see SYNC: add-new-language B2 above). The CI check only enforces flags listed here.
required_flags=(
  enforce_prerelease_only
  enforce_non_clobber
  publish_python_pypi
  publish_node_gpr
  publish_node_npmjs
  publish_ruby_gpr
  publish_ruby_rubygems
  enable_attestation
  github_release_prerelease
)

for flag in "${required_flags[@]}"; do
  # Scope the grep to each branch individually to catch the case where a flag
  # was added to 'official' but not 'buddy' (or vice versa). A global grep
  # count ≥ 2 would pass even if both assertions are in the same branch while
  # the other branch is missing them entirely.
  # Use ';; ' as the branch boundary instead of the sibling branch label.
  # This is more robust: a nested case/esac inside a branch would cause the
  # sibling-label approach to over-include lines; ;; always terminates a branch.
  official_count=$(awk '/^[[:space:]]+official\)/{f=1} f && /^[[:space:]]*;;/{exit} f' "${WORKFLOW_FILE}" \
    | grep -c "assert_equals \"${flag}\"" || true)
  buddy_count=$(awk '/^[[:space:]]+buddy\)/{f=1} f && /^[[:space:]]*;;/{exit} f' "${WORKFLOW_FILE}" \
    | grep -c "assert_equals \"${flag}\"" || true)
  if [[ "${official_count}" -lt 1 || "${buddy_count}" -lt 1 ]]; then
    echo "ERROR: flag '${flag}' missing assert_equals in one or more channel branches (official: ${official_count}, buddy: ${buddy_count}); each flag must be asserted in BOTH 'official' and 'buddy'." >&2
    FAIL=1
  fi
done

if [[ "${FAIL}" -ne 0 ]]; then
  echo "Policy self-test failed. When adding a new language, you MUST add assert_equals calls" >&2
  echo "for each new flag in BOTH the 'official' AND 'buddy' case branches of the policy job." >&2
  echo "See SYNC: add-new-language (B2) comment for details." >&2
  exit 1
fi
echo "Policy flag coverage self-test passed."
