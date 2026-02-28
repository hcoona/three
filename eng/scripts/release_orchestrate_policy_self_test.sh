#!/usr/bin/env bash
set -Eeuo pipefail

# The policy logic (assert_equals calls for official/buddy channel flags) lives in
# the policy validation script, not in the orchestration workflow YAML. This variable
# was previously pointing to the YAML file, which caused the self-test to always fail
# silently. Fixed to point to the correct source file.
POLICY_SCRIPT="eng/scripts/release_orchestrate_policy_validate_inputs.sh"
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
  official_count=$(awk '/^[[:space:]]+official\)/{f=1} f && /^[[:space:]]*;;/{exit} f' "${POLICY_SCRIPT}" \
    | grep -c "assert_equals \"${flag}\"" || true)
  buddy_count=$(awk '/^[[:space:]]+buddy\)/{f=1} f && /^[[:space:]]*;;/{exit} f' "${POLICY_SCRIPT}" \
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

# ==== Step-2 pre-case guard presence checks ====
# These checks verify that the new CHANNEL validation guards introduced in Step 2
# are still present in release_orchestrate_policy_validate_inputs.sh, catching
# regressions where a guard is accidentally removed or misplaced.
VALIDATE_INPUTS_SH="eng/scripts/release_orchestrate_policy_validate_inputs.sh"
FAIL=0

check_pattern_present() {
  local description="${1}"
  local pattern="${2}"
  local file="${3}"
  if ! grep -qE -- "${pattern}" "${file}"; then
    echo "ERROR: missing guard in ${file}: ${description}" >&2
    FAIL=1
  fi
}

# Empty channel guard
check_pattern_present "empty CHANNEL guard" \
  '-z "\$\{CHANNEL\}"' "${VALIDATE_INPUTS_SH}"

# Whitespace guard
check_pattern_present "whitespace-in-CHANNEL guard" \
  '\$\{CHANNEL\}.*\[\[:space:\]\]' "${VALIDATE_INPUTS_SH}"

# Uppercase guard
check_pattern_present "uppercase-CHANNEL guard" \
  '\$\{CHANNEL\}.*\$\{CHANNEL,,\}' "${VALIDATE_INPUTS_SH}"

# Reserved escape-slug guard for direct CHANNEL input
check_pattern_present "x-official/x-buddy direct CHANNEL guard" \
  '\$\{CHANNEL\}.*x-official' "${VALIDATE_INPUTS_SH}"

# Built-in channel guard inside is_channel_allowlisted (official/buddy cannot be allowlisted)
check_pattern_present "official/buddy allowlist entry builtin guard" \
  '\$\{entry\}.*==.*"official"' "${VALIDATE_INPUTS_SH}"

# Reserved escape-slug guard inside is_channel_allowlisted
check_pattern_present "x-official/x-buddy allowlist entry guard" \
  '\$\{entry\}.*x-official' "${VALIDATE_INPUTS_SH}"

# Format check in *) arm (prevents misleading "add to allowlist" guidance for invalid formats)
check_pattern_present "format check in *) case arm" \
  "invalid format and cannot be used" "${VALIDATE_INPUTS_SH}"

if [[ "${FAIL}" -ne 0 ]]; then
  echo "Step-2 guard presence check failed. A CHANNEL validation guard is missing" >&2
  echo "from ${VALIDATE_INPUTS_SH}. Restore the guard or update this self-test." >&2
  exit 1
fi
echo "Step-2 guard presence self-test passed."
