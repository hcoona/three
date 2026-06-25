#!/usr/bin/env bash

# Publish a Ruby gem artifact to RubyGems.org with rerun-safe digest validation.
#
# This script intentionally does NOT configure credentials. For RubyGems Trusted Publishing
# (OIDC), callers should run rubygems/configure-rubygems-credentials with trusted-publisher: true.
#
# Required env vars:
#   PACKAGE_NAME     Planner-frozen Ruby package name
#   PACKAGE_VERSION  Planner-frozen Ruby package version
#
# Optional env vars:
#   OUT_DIR    Directory containing the built gem (default: "$GITHUB_WORKSPACE/out")
#   EXPECTED_GEM_FILENAME  Planner-frozen gem basename (default: "$PACKAGE_NAME-$PACKAGE_VERSION.gem")
#   EXPECTED_GEM_SHA256    Compatibility-only lowercase SHA-256 digest (normally unset)

set -Eeuo pipefail

: "${PACKAGE_NAME:?PACKAGE_NAME is required}"
: "${PACKAGE_VERSION:?PACKAGE_VERSION is required}"

OUT_DIR="${OUT_DIR:-${GITHUB_WORKSPACE}/out}"

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required but not found on PATH." >&2
  exit 1
fi

expected_gem_filename="${EXPECTED_GEM_FILENAME:-${PACKAGE_NAME}-${PACKAGE_VERSION}.gem}"
if [[ "${expected_gem_filename}" != *.gem || "${expected_gem_filename}" == */* || "${expected_gem_filename}" == *\\* ]]; then
  echo "Invalid expected gem filename: ${expected_gem_filename}" >&2
  exit 1
fi

gem_path="${OUT_DIR}/${expected_gem_filename}"
if [[ ! -f "${gem_path}" ]]; then
  echo "Missing gem artifact: ${gem_path}" >&2
  ls -la "${OUT_DIR}" >&2 || true
  exit 1
fi
actual_basename="$(basename "${gem_path}")"
if [[ "${actual_basename}" != "${expected_gem_filename}" ]]; then
  echo "Gem filename mismatch: expected ${expected_gem_filename}, got ${actual_basename}" >&2
  exit 1
fi

local_sha=$(sha256sum "${gem_path}" | awk '{print $1}')
if [[ -n "${EXPECTED_GEM_SHA256:-}" ]]; then
  if [[ ! "${EXPECTED_GEM_SHA256}" =~ ^[0-9a-f]{64}$ ]]; then
    echo "Invalid expected gem SHA-256: ${EXPECTED_GEM_SHA256}" >&2
    exit 1
  fi
  if [[ "${local_sha}" != "${EXPECTED_GEM_SHA256}" ]]; then
    echo "Gem SHA-256 mismatch for ${actual_basename}: expected ${EXPECTED_GEM_SHA256}, got ${local_sha}" >&2
    exit 1
  fi
fi

api_url="https://rubygems.org/api/v2/rubygems/${PACKAGE_NAME}/versions/${PACKAGE_VERSION}.json?platform=ruby"
resp_json="${RUNNER_TEMP:-/tmp}/rubygems.version.json"

set +e
status=$(curl -sS -L -o "${resp_json}" -w '%{http_code}' "${api_url}")
curl_rc=$?
set -e

if [[ "${curl_rc}" -ne 0 ]]; then
  echo "Failed to query RubyGems.org API (curl exit ${curl_rc})." >&2
  exit 1
fi

if [[ "${status}" == "200" ]]; then
  remote_sha=$(jq -r '.sha // empty' "${resp_json}")
  if [[ -z "${remote_sha}" ]]; then
    echo "RubyGems.org API response missing 'sha'." >&2
    cat "${resp_json}" >&2 || true
    exit 1
  fi
  if [[ "${remote_sha}" == "${local_sha}" ]]; then
    echo "Gem ${PACKAGE_NAME} ${PACKAGE_VERSION} is already published on RubyGems.org (digest match)."
    exit 0
  fi
  echo "Gem ${PACKAGE_NAME} ${PACKAGE_VERSION} already exists on RubyGems.org but digest differs." >&2
  exit 1
fi

if [[ "${status}" == "404" ]]; then
  echo "Gem ${PACKAGE_NAME} ${PACKAGE_VERSION} not found on RubyGems.org yet; will attempt to push."
elif [[ "${status}" == "429" || "${status}" =~ ^5[0-9][0-9]$ ]]; then
  echo "RubyGems.org API is rate-limited or unavailable (HTTP ${status})." >&2
  exit 1
else
  echo "Unexpected RubyGems.org API response status: HTTP ${status}." >&2
  cat "${resp_json}" >&2 || true
  exit 1
fi

gem install rubygems-await -v 0.5.4 --no-document

push_err="${RUNNER_TEMP:-/tmp}/rubygems.push.err"

set +e
gem push "${gem_path}" 2>"${push_err}"
push_rc=$?
set -e

if [[ "${push_rc}" -ne 0 ]]; then
  if grep -Eqi '(already (exists|been pushed)|repushing|already pushed)' "${push_err}"; then
    echo "Push reported existing version; waiting and re-checking digest."
    gem await "${PACKAGE_NAME}:${PACKAGE_VERSION}:ruby"

    status=$(curl -sS -L -o "${resp_json}" -w '%{http_code}' "${api_url}")
    if [[ "${status}" != "200" ]]; then
      echo "After push conflict, RubyGems.org API still not returning 200 (HTTP ${status})." >&2
      cat "${push_err}" >&2 || true
      exit 1
    fi

    remote_sha=$(jq -r '.sha // empty' "${resp_json}")
    if [[ "${remote_sha}" == "${local_sha}" ]]; then
      echo "Gem ${PACKAGE_NAME} ${PACKAGE_VERSION} is already published on RubyGems.org (digest match)."
      exit 0
    fi

    echo "Gem ${PACKAGE_NAME} ${PACKAGE_VERSION} exists on RubyGems.org but digest differs." >&2
    exit 1
  fi

  echo "Failed to publish gem to RubyGems.org." >&2
  cat "${push_err}" >&2 || true
  exit 1
fi

gem await "${PACKAGE_NAME}:${PACKAGE_VERSION}:ruby"
echo "Published gem ${PACKAGE_NAME} ${PACKAGE_VERSION} to RubyGems.org."
