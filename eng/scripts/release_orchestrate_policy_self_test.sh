#!/usr/bin/env bash
set -Eeuo pipefail

# This script must be run from the repository root (the directory containing eng/).
# In CI it is invoked as: bash "${GITHUB_WORKSPACE}/eng/scripts/release_orchestrate_policy_self_test.sh"
# with cwd set to GITHUB_WORKSPACE by the runner.
# For local testing: cd /path/to/repo/root && bash eng/scripts/release_orchestrate_policy_self_test.sh

# The policy logic (assert_equals calls for official/buddy channel flags) lives in
# the policy validation script, not in the orchestration workflow YAML. This variable
# was previously pointing to the YAML file, which caused the self-test to always fail
# silently. Fixed to point to the correct source file.
POLICY_SCRIPT="eng/scripts/release_orchestrate_policy_validate_inputs.sh"
FAIL=0

# ==== Phase 0: Pre-case guard behavioral smoke tests ====
# These tests run the policy script directly to verify that the CHANNEL validation
# guards added in Step 2 actually reject invalid inputs. Unlike Phase 2 (which is
# structural/presence-only), these tests catch polarity inversions or logic errors
# that a grep-based check cannot detect.
check_guard_rejects() {
  local description="$1"
  local channel_value="$2"
  local expected_fragment="$3"
  local output
  # BLK-1: Capture stdout+stderr AND exit code inline. Appending 'echo "EXIT:$?"' after
  # the command means the outer 'set -Eeuo pipefail' shell stays clean while we can
  # still inspect whether the inner command actually exited non-zero.
  output=$(env SOURCE=manual PROJECT=dummy VERSION=1.0.0 CHANNEL_ALLOWLIST="" \
    ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
    PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=false PUBLISH_NODE_NPMJS=false \
    PUBLISH_RUBY_GPR=false PUBLISH_RUBY_RUBYGEMS=false \
    ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=false \
    GITHUB_STEP_SUMMARY=/dev/null GITHUB_ACTOR=test GITHUB_REF_NAME=test \
    CHANNEL="${channel_value}" bash "${POLICY_SCRIPT}" 2>&1; echo "EXIT:$?")
  # Verify non-zero exit: a guard that prints the error but omits 'exit 1' must fail here.
  if echo "${output}" | tail -1 | grep -q '^EXIT:0$'; then
    echo "ERROR: pre-case guard did not reject — '${description}' (channel='${channel_value}'): expected non-zero exit" >&2
    echo "  Got: ${output}" >&2
    FAIL=1
  fi
  if ! echo "${output}" | grep -qF "${expected_fragment}"; then
    echo "ERROR: pre-case guard missing expected message — '${description}' (channel='${channel_value}')" >&2
    echo "  Expected message containing: '${expected_fragment}'" >&2
    echo "  Got: ${output}" >&2
    FAIL=1
  fi
}

check_guard_rejects "empty channel"        ""           "Channel must not be empty."
check_guard_rejects "whitespace channel"   "my channel" "contains whitespace"
check_guard_rejects "uppercase channel"    "Official"   "contains uppercase"
check_guard_rejects "x-official reserved" "x-official" "reserved as an internal remapping slug"
check_guard_rejects "x-buddy reserved"    "x-buddy"    "reserved as an internal remapping slug"
# Format checks: these channels pass all pre-case guards but fail the regex in the *) arm.
# These tests guard against a polarity inversion or regex error in the format check that
# would cause invalid-format channels to fall through to the allowlist lookup instead.
check_guard_rejects "consecutive-hyphen channel (invalid format)"    "a--b"       "invalid format and cannot be used"
check_guard_rejects "leading-separator channel (invalid format)"      "-beta"      "invalid format and cannot be used"
check_guard_rejects "trailing-hyphen channel (invalid format)"        "alpha-"     "invalid format and cannot be used"
check_guard_rejects "trailing-underscore channel (invalid format)"    "alpha_"     "invalid format and cannot be used"
check_guard_rejects "consecutive-underscore channel (invalid format)" "my__channel" "invalid format and cannot be used"
check_guard_rejects "mixed-separator channel (invalid format)"        "a_-b"       "invalid format and cannot be used"

# NB-6: Whitespace guard — also covers leading, trailing, and tab characters.
# 'my channel' (internal space) is already tested above; these add the variants that
# are common transcription errors in GitHub Actions 'with:' blocks.
check_guard_rejects "leading-space channel"  " staging"    "contains whitespace"
check_guard_rejects "trailing-space channel" "staging "    "contains whitespace"
check_guard_rejects "tab channel"            $'stag\ting'  "contains whitespace"
check_guard_rejects "newline channel"        $'stag\ning'  "contains whitespace"

# NB-5: Uppercase + reserved slug — triggers a distinct inner guard with its own message.
# 'Official' (plain uppercase) is tested above; 'X-OFFICIAL' hits an inner if-branch
# that emits a different message ('its lowercase form ... is reserved'). Without this test,
# that inner branch is dead code from the test suite's perspective.
check_guard_rejects "uppercase reserved slug X-OFFICIAL"      "X-OFFICIAL" "its lowercase form"
check_guard_rejects "mixed-case reserved slug x-Official"     "x-Official" "its lowercase form"

# NB-7: Valid-format channel with empty allowlist → "Unknown channel" path.
# This verifies the *) arm's allowlist lookup fires correctly when CHANNEL is a valid-format
# channel that simply isn't listed. Without this test, removing the allowlist lookup
# entirely would be invisible to all format-check guard tests above.
check_guard_rejects "valid-format channel not in allowlist (unknown-channel path)" \
  "unlisted" "Unknown channel"

# Behavioral smoke tests for is_channel_allowlisted reserved-entry guards.
# These use a valid CHANNEL (staging) with a CHANNEL_ALLOWLIST entry that should be
# rejected, verifying that the allowlist loop guard fires and exits non-zero.
# Phase 2 has structural checks for these guards, but a polarity inversion
# (e.g., == changed to !=) would pass Phase 2 silently.
check_allowlist_rejects() {
  local description="$1"
  local allowlist_value="$2"
  local expected_fragment="$3"
  local output
  # 'staging' is the stable anchor CHANNEL: it passes all pre-case guards so the
  # allowlist validation loop is always reached. The test exercises the invalid/reserved
  # CHANNEL_ALLOWLIST entry, not the channel value itself.
  # BLK-1: Capture exit code inline (same pattern as check_guard_rejects).
  output=$(env SOURCE=manual PROJECT=dummy VERSION=1.0.0 \
    CHANNEL_ALLOWLIST="${allowlist_value}" \
    ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
    PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=false PUBLISH_NODE_NPMJS=false \
    PUBLISH_RUBY_GPR=false PUBLISH_RUBY_RUBYGEMS=false \
    ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=false \
    GITHUB_STEP_SUMMARY=/dev/null GITHUB_ACTOR=test GITHUB_REF_NAME=test \
    CHANNEL=staging bash "${POLICY_SCRIPT}" 2>&1; echo "EXIT:$?")
  if echo "${output}" | tail -1 | grep -q '^EXIT:0$'; then
    echo "ERROR: allowlist entry guard did not reject — '${description}' (allowlist='${allowlist_value}'): expected non-zero exit" >&2
    echo "  Got: ${output}" >&2
    FAIL=1
  fi
  if ! echo "${output}" | grep -qF "${expected_fragment}"; then
    echo "ERROR: allowlist entry guard missing expected message — '${description}' (allowlist='${allowlist_value}')" >&2
    echo "  Expected message containing: '${expected_fragment}'" >&2
    echo "  Got: ${output}" >&2
    FAIL=1
  fi
}

check_allowlist_rejects "official in allowlist (built-in channel)" \
  "official" "cannot appear in channel_allowlist"
check_allowlist_rejects "buddy in allowlist (built-in channel)" \
  "buddy" "cannot appear in channel_allowlist"
# NB-8: Use a unique substring from the actual reserved-slug error message rather than
# 'x-official'/'x-buddy' themselves -- those strings appear in the input being tested
# and a polarity inversion that printed them regardless would produce a false pass.
check_allowlist_rejects "x-official in allowlist (reserved slug)" \
  "x-official" "reserved (hyphen form only"
check_allowlist_rejects "x-buddy in allowlist (reserved slug)" \
  "x-buddy" "reserved (hyphen form only"

# BLK-4: Behavioral tests for is_channel_allowlisted FORMAT validation.
# These exercise the regex guard INSIDE is_channel_allowlisted — a completely separate
# code path from the *) arm's direct CHANNEL format check. The anchor CHANNEL=staging
# is always valid; the invalid value under test is the CHANNEL_ALLOWLIST entry itself.
check_allowlist_rejects "invalid allowlist entry: consecutive hyphen" \
  "a--b" "Invalid channel name 'a--b' in channel_allowlist"
check_allowlist_rejects "invalid allowlist entry: leading separator" \
  "-beta" "Invalid channel name '-beta' in channel_allowlist"
check_allowlist_rejects "invalid allowlist entry: trailing hyphen" \
  "alpha-" "Invalid channel name 'alpha-' in channel_allowlist"
check_allowlist_rejects "invalid allowlist entry: trailing underscore" \
  "alpha_" "Invalid channel name 'alpha_' in channel_allowlist"
check_allowlist_rejects "invalid allowlist entry: consecutive underscore" \
  "my__channel" "Invalid channel name 'my__channel' in channel_allowlist"
check_allowlist_rejects "invalid allowlist entry: mixed separator" \
  "a_-b" "Invalid channel name 'a_-b' in channel_allowlist"

check_guard_accepts() {
  # Verify that a valid channel value is NOT rejected by the pre-case guards.
  # Uses a minimal allowlist that allowlists the test channel so the script can
  # proceed past the policy case block and exit 0.
  local description="$1"
  local channel_value="$2"
  local output
  output=$(env SOURCE=manual PROJECT=dummy VERSION=1.0.0 \
    CHANNEL_ALLOWLIST="${channel_value}" \
    ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
    PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=false PUBLISH_NODE_NPMJS=false \
    PUBLISH_RUBY_GPR=false PUBLISH_RUBY_RUBYGEMS=false \
    ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=false \
    GITHUB_STEP_SUMMARY=/dev/null GITHUB_ACTOR=test GITHUB_REF_NAME=test \
    CHANNEL="${channel_value}" bash "${POLICY_SCRIPT}" 2>&1; echo "EXIT:$?")
  if ! echo "${output}" | tail -1 | grep -q '^EXIT:0$'; then
    echo "ERROR: guard incorrectly rejected valid channel — '${description}' (channel='${channel_value}')" >&2
    echo "  Got: ${output}" >&2
    FAIL=1
  fi
}

check_guard_accepts "simple lowercase channel"   "staging"
check_guard_accepts "hyphenated channel"         "my-channel"
check_guard_accepts "channel with digit suffix"  "canary2"
check_guard_accepts "channel with underscore"    "my_channel"
# NB-4: The underscore variants x_official and x_buddy are explicitly documented as NOT
# blocked (see validate_inputs.sh comment). Without acceptance tests, a developer could
# silently tighten the guard and remove this deliberate allowance with no test failure.
check_guard_accepts "x_official is allowed (underscore form, not reserved)" "x_official"
check_guard_accepts "x_buddy is allowed (underscore form, not reserved)"    "x_buddy"

# BLK-5: Multi-entry allowlist — verify is_channel_allowlisted iterates ALL entries and
# correctly accepts a channel that appears as the non-first (second) entry.
# Single-entry tests above confirm the basic happy path; this guards against an
# off-by-one regression where only the first allowlist entry is ever compared.
{
  _me_output=$(env SOURCE=manual PROJECT=dummy VERSION=1.0.0 \
    CHANNEL_ALLOWLIST="other,staging" \
    ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
    PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=false PUBLISH_NODE_NPMJS=false \
    PUBLISH_RUBY_GPR=false PUBLISH_RUBY_RUBYGEMS=false \
    ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=false \
    GITHUB_STEP_SUMMARY=/dev/null GITHUB_ACTOR=test GITHUB_REF_NAME=test \
    CHANNEL=staging bash "${POLICY_SCRIPT}" 2>&1; echo "EXIT:$?")
  if ! echo "${_me_output}" | tail -1 | grep -q '^EXIT:0$'; then
    echo "ERROR: multi-entry allowlist: 'staging' should be accepted in 'other,staging' allowlist" >&2
    echo "  Got: ${_me_output}" >&2
    FAIL=1
  fi
}

# Verify that the built-in channels pass the pre-case guards AND satisfy policy when
# the correct flag profile is provided. This guards against accidental changes to the
# guard conditions that would block official/buddy from ever passing (e.g., adding a
# guard that rejects channels without a hyphen, or a case-insensitive reserved-name
# check that matches "official").
check_builtin_channel_accepted() {
  local description="$1"
  shift
  local output
  output=$(env SOURCE=manual PROJECT=dummy VERSION=1.0.0 CHANNEL_ALLOWLIST="" \
    GITHUB_STEP_SUMMARY=/dev/null GITHUB_ACTOR=test GITHUB_REF_NAME=test \
    "$@" bash "${POLICY_SCRIPT}" 2>&1; echo "EXIT:$?")
  if ! echo "${output}" | tail -1 | grep -q '^EXIT:0$'; then
    echo "ERROR: built-in channel incorrectly rejected — '${description}'" >&2
    echo "  Got: ${output}" >&2
    FAIL=1
  fi
}

check_builtin_channel_accepted "official channel with correct profile" \
  CHANNEL=official \
  ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
  PUBLISH_PYTHON_PYPI=true PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=true \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=true \
  ENABLE_ATTESTATION=true GITHUB_RELEASE_PRERELEASE=false

check_builtin_channel_accepted "buddy channel with correct profile" \
  CHANNEL=buddy \
  ENFORCE_PRERELEASE_ONLY=true ENFORCE_NON_CLOBBER=true \
  PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=false \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=false \
  ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=true

# BLK-2: Verify built-in channels REJECT an incorrect flag profile.
# check_builtin_channel_accepted only tests the happy path; a polarity inversion in
# assert_equals ('!=' accidentally changed to '==') would be invisible to it.
# Phase 1 catches assertion *removal* but not operator inversion — only a behavioral
# reject test covers that gap.
check_builtin_channel_rejects() {
  local description="$1"
  shift
  local output
  output=$(env SOURCE=manual PROJECT=dummy VERSION=1.0.0 CHANNEL_ALLOWLIST="" \
    GITHUB_STEP_SUMMARY=/dev/null GITHUB_ACTOR=test GITHUB_REF_NAME=test \
    "$@" bash "${POLICY_SCRIPT}" 2>&1; echo "EXIT:$?")
  if echo "${output}" | tail -1 | grep -q '^EXIT:0$'; then
    echo "ERROR: built-in channel incorrectly accepted wrong profile — '${description}'" >&2
    echo "  Got: ${output}" >&2
    FAIL=1
    return  # skip assert_equals check; script accepted, so no rejection message to inspect
  fi
  # Verify the rejection came from an assert_equals mismatch, not an unrelated pre-case
  # failure. Without this check, a regression that breaks env-var handling before the
  # case block would make all 18 tests appear to pass (they exit non-zero) while never
  # actually exercising any assertion.
  if ! echo "${output}" | grep -qF "policy mismatch for"; then
    echo "ERROR: built-in channel rejected but not via assert_equals — '${description}'" >&2
    echo "  Got: ${output}" >&2
    FAIL=1
  fi
}

# SYNC[add-new-language] — add a check_builtin_channel_rejects call below for each new
# publish_<lang>_<registry> flag, flipping it to its wrong value relative to the expected
# official/buddy profile. Without this, removing or inverting the new assert_equals in
# one of the channel arms would produce no self-test failure.
# Each call flips exactly one flag relative to the correct profile to verify that every
# assert_equals is present and uses the correct operator. Covering all 9 flags (7 of which
# are asymmetric, 2 symmetric: PUBLISH_NODE_GPR and PUBLISH_RUBY_GPR) prevents a
# polarity-inversion regression from going undetected.
check_builtin_channel_rejects "official rejects PUBLISH_PYTHON_PYPI=false" \
  CHANNEL=official \
  ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
  PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=true \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=true \
  ENABLE_ATTESTATION=true GITHUB_RELEASE_PRERELEASE=false
check_builtin_channel_rejects "official rejects ENFORCE_PRERELEASE_ONLY=true" \
  CHANNEL=official \
  ENFORCE_PRERELEASE_ONLY=true ENFORCE_NON_CLOBBER=false \
  PUBLISH_PYTHON_PYPI=true PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=true \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=true \
  ENABLE_ATTESTATION=true GITHUB_RELEASE_PRERELEASE=false
check_builtin_channel_rejects "official rejects ENFORCE_NON_CLOBBER=true" \
  CHANNEL=official \
  ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=true \
  PUBLISH_PYTHON_PYPI=true PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=true \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=true \
  ENABLE_ATTESTATION=true GITHUB_RELEASE_PRERELEASE=false
check_builtin_channel_rejects "official rejects PUBLISH_NODE_GPR=false" \
  CHANNEL=official \
  ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
  PUBLISH_PYTHON_PYPI=true PUBLISH_NODE_GPR=false PUBLISH_NODE_NPMJS=true \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=true \
  ENABLE_ATTESTATION=true GITHUB_RELEASE_PRERELEASE=false
check_builtin_channel_rejects "official rejects PUBLISH_NODE_NPMJS=false" \
  CHANNEL=official \
  ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
  PUBLISH_PYTHON_PYPI=true PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=false \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=true \
  ENABLE_ATTESTATION=true GITHUB_RELEASE_PRERELEASE=false
check_builtin_channel_rejects "official rejects PUBLISH_RUBY_GPR=false" \
  CHANNEL=official \
  ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
  PUBLISH_PYTHON_PYPI=true PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=true \
  PUBLISH_RUBY_GPR=false PUBLISH_RUBY_RUBYGEMS=true \
  ENABLE_ATTESTATION=true GITHUB_RELEASE_PRERELEASE=false
check_builtin_channel_rejects "official rejects ENABLE_ATTESTATION=false" \
  CHANNEL=official \
  ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
  PUBLISH_PYTHON_PYPI=true PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=true \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=true \
  ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=false
check_builtin_channel_rejects "official rejects PUBLISH_RUBY_RUBYGEMS=false" \
  CHANNEL=official \
  ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
  PUBLISH_PYTHON_PYPI=true PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=true \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=false \
  ENABLE_ATTESTATION=true GITHUB_RELEASE_PRERELEASE=false
check_builtin_channel_rejects "official rejects GITHUB_RELEASE_PRERELEASE=true" \
  CHANNEL=official \
  ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
  PUBLISH_PYTHON_PYPI=true PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=true \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=true \
  ENABLE_ATTESTATION=true GITHUB_RELEASE_PRERELEASE=true

check_builtin_channel_rejects "buddy rejects PUBLISH_PYTHON_PYPI=true" \
  CHANNEL=buddy \
  ENFORCE_PRERELEASE_ONLY=true ENFORCE_NON_CLOBBER=true \
  PUBLISH_PYTHON_PYPI=true PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=false \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=false \
  ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=true
check_builtin_channel_rejects "buddy rejects ENFORCE_PRERELEASE_ONLY=false" \
  CHANNEL=buddy \
  ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=true \
  PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=false \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=false \
  ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=true
check_builtin_channel_rejects "buddy rejects ENFORCE_NON_CLOBBER=false" \
  CHANNEL=buddy \
  ENFORCE_PRERELEASE_ONLY=true ENFORCE_NON_CLOBBER=false \
  PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=false \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=false \
  ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=true
check_builtin_channel_rejects "buddy rejects PUBLISH_NODE_GPR=false" \
  CHANNEL=buddy \
  ENFORCE_PRERELEASE_ONLY=true ENFORCE_NON_CLOBBER=true \
  PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=false PUBLISH_NODE_NPMJS=false \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=false \
  ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=true
check_builtin_channel_rejects "buddy rejects PUBLISH_RUBY_RUBYGEMS=true" \
  CHANNEL=buddy \
  ENFORCE_PRERELEASE_ONLY=true ENFORCE_NON_CLOBBER=true \
  PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=false \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=true \
  ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=true
check_builtin_channel_rejects "buddy rejects PUBLISH_RUBY_GPR=false" \
  CHANNEL=buddy \
  ENFORCE_PRERELEASE_ONLY=true ENFORCE_NON_CLOBBER=true \
  PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=false \
  PUBLISH_RUBY_GPR=false PUBLISH_RUBY_RUBYGEMS=false \
  ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=true
check_builtin_channel_rejects "buddy rejects ENABLE_ATTESTATION=true" \
  CHANNEL=buddy \
  ENFORCE_PRERELEASE_ONLY=true ENFORCE_NON_CLOBBER=true \
  PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=false \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=false \
  ENABLE_ATTESTATION=true GITHUB_RELEASE_PRERELEASE=true
check_builtin_channel_rejects "buddy rejects PUBLISH_NODE_NPMJS=true" \
  CHANNEL=buddy \
  ENFORCE_PRERELEASE_ONLY=true ENFORCE_NON_CLOBBER=true \
  PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=true \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=false \
  ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=true
check_builtin_channel_rejects "buddy rejects GITHUB_RELEASE_PRERELEASE=false" \
  CHANNEL=buddy \
  ENFORCE_PRERELEASE_ONLY=true ENFORCE_NON_CLOBBER=true \
  PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=false \
  PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=false \
  ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=false

# BLK-3: Verify production registry flags are prohibited for allowlisted channels.
# These three guards in the *) arm prevent staging/canary channels from accidentally
# publishing to PyPI/npmjs/RubyGems. Removing any one guard would be invisible to all
# other Phase 0 and Phase 2 checks without these behavioral tests.
check_allowlist_registry_rejects() {
  # Usage: check_allowlist_registry_rejects <description> <expected_fragment> [KEY=VALUE ...]
  # Each KEY=VALUE is passed as a separate env override; 'env' treats each as one assignment.
  # Do NOT combine multiple overrides into one quoted string (env cannot split them).
  # Override flags are placed AFTER the defaults in the env command. GNU env resolves
  # duplicate variables by using the last value — this is the GNU env left-to-right
  # last-writer-wins behaviour (not guaranteed by POSIX, but reliable on Linux CI).
  local description="$1"
  local expected_fragment="$2"
  shift 2
  local output
  output=$(env SOURCE=manual PROJECT=dummy VERSION=1.0.0 \
    CHANNEL_ALLOWLIST="staging" \
    ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
    PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=false PUBLISH_NODE_NPMJS=false \
    PUBLISH_RUBY_GPR=false PUBLISH_RUBY_RUBYGEMS=false \
    ENABLE_ATTESTATION=false GITHUB_RELEASE_PRERELEASE=false \
    GITHUB_STEP_SUMMARY=/dev/null GITHUB_ACTOR=test GITHUB_REF_NAME=test \
    "$@" CHANNEL=staging bash "${POLICY_SCRIPT}" 2>&1; echo "EXIT:$?")
  if echo "${output}" | tail -1 | grep -q '^EXIT:0$'; then
    echo "ERROR: registry prohibition not enforced — '${description}': expected non-zero exit" >&2
    echo "  Got: ${output}" >&2
    FAIL=1
  fi
  if ! echo "${output}" | grep -qF "${expected_fragment}"; then
    echo "ERROR: registry prohibition missing expected message — '${description}'" >&2
    echo "  Expected message containing: '${expected_fragment}'" >&2
    echo "  Got: ${output}" >&2
    FAIL=1
  fi
}

check_allowlist_registry_rejects "allowlisted channel must not publish to PyPI" \
  "restricted to the official channel only" "PUBLISH_PYTHON_PYPI=true"
check_allowlist_registry_rejects "allowlisted channel must not publish to npmjs" \
  "restricted to the official channel only" "PUBLISH_NODE_NPMJS=true"
check_allowlist_registry_rejects "allowlisted channel must not publish to RubyGems" \
  "restricted to the official channel only" "PUBLISH_RUBY_RUBYGEMS=true"

# BLK-6: SOURCE=tag input validation behavioral tests.
# These verify that the source=tag path's REF_NAME/REF guards fire correctly. All other
# Phase 0 tests use SOURCE=manual — these add coverage for the source=tag code path that
# is otherwise completely untested. A guard removal in the tag path would be invisible
# to every other Phase 0 test.
check_source_tag_rejects() {
  local description="$1"
  local expected_fragment="$2"
  shift 2
  local output
  # Base env: fully valid SOURCE=tag with official channel profile.
  # Caller overrides specific vars via "$@" to trigger the guard under test.
  # REF_NAME and REF are included in the base so a single override to "" triggers
  # exactly the missing-field guard without unbound-variable errors.
  output=$(env SOURCE=tag REF_NAME="v1.0.0" REF="refs/tags/v1.0.0" \
    CHANNEL=official CHANNEL_ALLOWLIST="" \
    ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
    PUBLISH_PYTHON_PYPI=true PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=true \
    PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=true \
    ENABLE_ATTESTATION=true GITHUB_RELEASE_PRERELEASE=false \
    GITHUB_STEP_SUMMARY=/dev/null GITHUB_ACTOR=test GITHUB_REF_NAME=refs/tags/v1.0.0 \
    "$@" bash "${POLICY_SCRIPT}" 2>&1; echo "EXIT:$?")
  if echo "${output}" | tail -1 | grep -q '^EXIT:0$'; then
    echo "ERROR: source=tag guard did not reject — '${description}': expected non-zero exit" >&2
    echo "  Got: ${output}" >&2
    FAIL=1
  fi
  if ! echo "${output}" | grep -qF "${expected_fragment}"; then
    echo "ERROR: source=tag guard missing expected message — '${description}'" >&2
    echo "  Expected message containing: '${expected_fragment}'" >&2
    echo "  Got: ${output}" >&2
    FAIL=1
  fi
}

check_source_tag_accepts() {
  local description="$1"
  shift
  local output
  output=$(env SOURCE=tag REF_NAME="v1.0.0" REF="refs/tags/v1.0.0" \
    CHANNEL=official CHANNEL_ALLOWLIST="" \
    ENFORCE_PRERELEASE_ONLY=false ENFORCE_NON_CLOBBER=false \
    PUBLISH_PYTHON_PYPI=true PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=true \
    PUBLISH_RUBY_GPR=true PUBLISH_RUBY_RUBYGEMS=true \
    ENABLE_ATTESTATION=true GITHUB_RELEASE_PRERELEASE=false \
    GITHUB_STEP_SUMMARY=/dev/null GITHUB_ACTOR=test GITHUB_REF_NAME=refs/tags/v1.0.0 \
    "$@" bash "${POLICY_SCRIPT}" 2>&1; echo "EXIT:$?")
  if ! echo "${output}" | tail -1 | grep -q '^EXIT:0$'; then
    echo "ERROR: source=tag incorrectly rejected — '${description}'" >&2
    echo "  Got: ${output}" >&2
    FAIL=1
  fi
}

check_source_tag_rejects "source=tag missing REF_NAME" \
  "source=tag requires ref_name to be set." REF_NAME=""
check_source_tag_rejects "source=tag missing REF" \
  "source=tag requires ref to be set." REF=""
check_source_tag_accepts "source=tag with all required fields (official channel)"

if [[ "${FAIL}" -ne 0 ]]; then
  echo "Pre-case guard behavioral smoke tests failed. See errors above." >&2
  exit 1
fi
echo "Pre-case guard behavioral smoke tests passed."
# Invariant: FAIL==0 here (Phase 0 exits on any failure above).

# For each flag that must be validated per-channel, verify that assert_equals
# appears at least twice in the policy job (once for official, once in buddy).
# A count < 2 means the flag was added to one branch but not the other,
# which causes a silent policy bypass for the missing channel.
# SYNC[add-new-language] — add the new publish_<lang>_<registry> flag name(s) here.
# Omitting this step means future regressions (removing assert_equals for the new flag
# from one branch) will not be caught by the self-test.
# NOTE: force_update_tag is intentionally excluded from this list.
# It is a per-run operational override, not a channel profile flag, and is not asserted
# by the policy job for either channel. Add channel profile flags only.
# NOTE: This list is manually maintained. Adding a new publish_<lang>_<registry> input
# requires updating BOTH this list AND the assert_equals calls in the case branches
# (see SYNC[add-new-language] B2 above). The CI check only enforces flags listed here.
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
  # Use '^[[:space:]]*;;' as the branch boundary instead of the sibling branch label.
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
  echo "See SYNC[add-new-language] (B2) comment for details." >&2
  exit 1
fi
echo "Policy flag coverage self-test passed."

# ==== Step-2 pre-case guard presence checks ====
# These checks verify that the new CHANNEL validation guards introduced in Step 2
# are still present in release_orchestrate_policy_validate_inputs.sh, catching
# regressions where a guard is accidentally removed or misplaced.
# NOTE (NB-4): These are PRESENCE-only checks (grep/awk). They cannot verify that
# guards appear in the correct order relative to the case statement. Guard ordering
# correctness is guaranteed by Phase 0 behavioral tests above — those tests exercise
# the actual execution path and would fail if a guard were moved to the wrong position
# (e.g., inside the *) arm instead of before the case block).
VALIDATE_INPUTS_SH="${POLICY_SCRIPT}"  # same file; single canonical path declared above
# Use a separate variable for Phase 2 — Phase 1 exits on failure so FAIL is
# guaranteed 0 here, but an explicit name makes the phase boundary clear.
FAIL_PHASE2=0

check_pattern_present() {
  local description="${1}"
  local pattern="${2}"
  local file="${3}"
  if ! grep -qE -- "${pattern}" "${file}"; then
    echo "ERROR: missing guard in ${file}: ${description}" >&2
    FAIL_PHASE2=1
  fi
}

# NOTE: patterns are prefixed with '^[^#]*' to exclude comment lines, preventing a
# removed guard whose comment residue remains from producing a false-positive match.

# Empty channel guard
check_pattern_present "empty CHANNEL guard" \
  '^[^#]*-z "\$\{CHANNEL\}"' "${VALIDATE_INPUTS_SH}"

# whitespace guard — pattern requires both the =~ operator and the [[:space:]] literal,
# making it specific enough to detect guard removal while tolerating quote styles.
check_pattern_present "whitespace-in-CHANNEL guard" \
  '^[^#]*\$\{CHANNEL\}.*=~.*\[\[:space:\]\]' "${VALIDATE_INPUTS_SH}"

# Uppercase guard
check_pattern_present "uppercase-CHANNEL guard" \
  '^[^#]*\$\{CHANNEL\}.*\$\{CHANNEL,,\}' "${VALIDATE_INPUTS_SH}"

# Reserved escape-slug guard for direct CHANNEL input
check_pattern_present "x-official/x-buddy direct CHANNEL guard" \
  '^[^#]*\$\{CHANNEL\}.*x-official' "${VALIDATE_INPUTS_SH}"

# Built-in channel guard inside is_channel_allowlisted (official/buddy cannot be allowlisted)
check_pattern_present "official/buddy allowlist entry builtin guard" \
  '^[^#]*\$\{entry\}.*==.*"official"' "${VALIDATE_INPUTS_SH}"

# Reserved escape-slug guard inside is_channel_allowlisted
check_pattern_present "x-official/x-buddy allowlist entry guard" \
  '^[^#]*\$\{entry\}.*x-official' "${VALIDATE_INPUTS_SH}"

# Format check in *) arm (prevents misleading "add to allowlist" guidance for invalid formats)
check_pattern_present "format check in *) case arm" \
  '^[^#]*invalid format and cannot be used' "${VALIDATE_INPUTS_SH}"

if [[ "${FAIL_PHASE2}" -ne 0 ]]; then
  echo "Step-2 guard presence check failed. A CHANNEL validation guard is missing" >&2
  echo "from ${VALIDATE_INPUTS_SH}. See each guard's inline comment for design intent." >&2
  echo "See REFACTOR_PLAN.md §2 (Step 2) for the full validation design." >&2
  exit 1
fi
echo "Step-2 guard presence self-test passed."

# ==== Phase 3: publish_targets.sh behavioral tests ====
# Covers the three new changes introduced in Step 2 for
# release_orchestrate_policy_publish_targets.sh:
#   PT-1: PROJECT_KIND empty guard (new behavior — exit 1 with clear message)
#   PT-2: IS_WXT canonical value contract (new behavior — reject 'yes', '1', etc.)
#   PT-3+: Acceptance paths (node-npm, node-wxt, python, ruby) confirming the
#          defensive IS_WXT:- and GITHUB_STEP_SUMMARY:-/dev/null fixes work under
#          the test runner's environment (where both vars are unset).
PUBLISH_TARGETS_SCRIPT="eng/scripts/release_orchestrate_policy_publish_targets.sh"
FAIL_PHASE3=0

check_publish_targets_rejects() {
  local description="$1"
  local expected_fragment="$2"
  shift 2
  local output
  output=$(env PROJECT=test-project CHANNEL=official \
    PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=false PUBLISH_NODE_NPMJS=false \
    PUBLISH_RUBY_GPR=false PUBLISH_RUBY_RUBYGEMS=false \
    GITHUB_STEP_SUMMARY=/dev/null \
    "$@" bash "${PUBLISH_TARGETS_SCRIPT}" 2>&1; echo "EXIT:$?")
  if echo "${output}" | tail -1 | grep -q '^EXIT:0$'; then
    echo "ERROR: publish_targets.sh incorrectly accepted — '${description}'" >&2
    echo "  Got: ${output}" >&2
    FAIL_PHASE3=1
    return
  fi
  if ! echo "${output}" | grep -qF "${expected_fragment}"; then
    echo "ERROR: publish_targets.sh rejected for wrong reason — '${description}'" >&2
    echo "  Expected to find: '${expected_fragment}'" >&2
    echo "  Got: ${output}" >&2
    FAIL_PHASE3=1
  fi
}

check_publish_targets_accepts() {
  local description="$1"
  shift
  local output
  output=$(env PROJECT=test-project CHANNEL=official \
    PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=false PUBLISH_NODE_NPMJS=false \
    PUBLISH_RUBY_GPR=false PUBLISH_RUBY_RUBYGEMS=false \
    GITHUB_STEP_SUMMARY=/dev/null \
    "$@" bash "${PUBLISH_TARGETS_SCRIPT}" 2>&1; echo "EXIT:$?")
  if ! echo "${output}" | tail -1 | grep -q '^EXIT:0$'; then
    echo "ERROR: publish_targets.sh incorrectly rejected — '${description}'" >&2
    echo "  Got: ${output}" >&2
    FAIL_PHASE3=1
  fi
}

# PT-1: PROJECT_KIND empty guard
check_publish_targets_rejects "empty PROJECT_KIND is rejected" \
  "PROJECT_KIND is empty" \
  PROJECT_KIND=""

# PT-2: IS_WXT empty and non-canonical value contract
# PT-2a: IS_WXT unset/empty must be rejected (Bug-1 fix: guard was previously skipped for
# empty IS_WXT via -n, silently routing WXT projects as node-npm).
check_publish_targets_rejects "IS_WXT empty is rejected for node project" \
  "IS_WXT is empty" \
  PROJECT_KIND=node PUBLISH_NODE_GPR=true
# PT-2b: Non-canonical non-empty values must also be rejected.
check_publish_targets_rejects "IS_WXT='yes' is rejected for node project" \
  "non-canonical value" \
  PROJECT_KIND=node IS_WXT=yes PUBLISH_NODE_GPR=true
check_publish_targets_rejects "IS_WXT='1' is rejected for node project" \
  "non-canonical value" \
  PROJECT_KIND=node IS_WXT=1 PUBLISH_NODE_GPR=true
check_publish_targets_rejects "IS_WXT='TRUE' is rejected for node project" \
  "non-canonical value" \
  PROJECT_KIND=node IS_WXT=TRUE PUBLISH_NODE_GPR=true

# PT-3: node-npm acceptance (IS_WXT=false, at least one Node publish target)
check_publish_targets_accepts "node-npm project with GPR publish target" \
  PROJECT_KIND=node IS_WXT=false PUBLISH_NODE_GPR=true
check_publish_targets_accepts "node-npm project with npmjs publish target" \
  PROJECT_KIND=node IS_WXT=false PUBLISH_NODE_NPMJS=true

# PT-4: node-wxt acceptance (IS_WXT=true; official channel permits node flags by policy)
check_publish_targets_accepts "WXT project on official channel accepts node flags" \
  PROJECT_KIND=node IS_WXT=true PUBLISH_NODE_GPR=true PUBLISH_NODE_NPMJS=true

# PT-5: python acceptance (standard path — GITHUB_STEP_SUMMARY=/dev/null via test helper).
check_publish_targets_accepts "python project accepts (GITHUB_STEP_SUMMARY=/dev/null)" \
  PROJECT_KIND=python

# PT-5b: validates GITHUB_STEP_SUMMARY:-/dev/null defensive fix: the :- default must be
# exercised when GITHUB_STEP_SUMMARY is unset, so the script must not abort via set -u.
if pt5b_out=$(env -u GITHUB_STEP_SUMMARY \
    PROJECT=test-project CHANNEL=official \
    PUBLISH_PYTHON_PYPI=false PUBLISH_NODE_GPR=false PUBLISH_NODE_NPMJS=false \
    PUBLISH_RUBY_GPR=false PUBLISH_RUBY_RUBYGEMS=false \
    PROJECT_KIND=python \
    bash "${PUBLISH_TARGETS_SCRIPT}" 2>&1); then
  :
else
  echo "ERROR: publish_targets.sh incorrectly rejected — 'python project accepts (GITHUB_STEP_SUMMARY unset)'" >&2
  echo "  Got: ${pt5b_out}" >&2
  FAIL_PHASE3=1
fi

# PT-6: ruby acceptance
check_publish_targets_accepts "ruby project with RubyGems publish target" \
  PROJECT_KIND=ruby PUBLISH_RUBY_RUBYGEMS=true

# PT-7: cross-kind contamination rejected for custom channel (assert_disabled behavioral test).
# All Phase 3 acceptance tests above use CHANNEL=official where assert_disabled is a no-op;
# this test uses a custom allowlisted channel to exercise the actual contamination check path.
check_publish_targets_rejects "cross-kind contamination rejected for custom channel" \
  "Cross-kind contamination" \
  PROJECT_KIND=python PUBLISH_NODE_GPR=true CHANNEL=staging \
  CHANNEL_ALLOWLIST=staging

if [[ "${FAIL_PHASE3}" -ne 0 ]]; then
  echo "publish_targets.sh behavioral tests failed. See errors above." >&2
  exit 1
fi
echo "publish_targets.sh behavioral tests passed."
