#!/usr/bin/env bash

# Publish a Ruby gem artifact to RubyGems.org with rerun-safe digest validation.
#
# This script intentionally does NOT configure credentials. For RubyGems Trusted Publishing
# (OIDC), callers should run rubygems/configure-rubygems-credentials with trusted-publisher: true.
#
# Required env vars:
#   PROJECT    Gem name
#   VERSION    Gem version
#
# Optional env vars:
#   OUT_DIR    Directory containing the built gem (default: "$GITHUB_WORKSPACE/out")

set -Eeuo pipefail

: "${PROJECT:?PROJECT is required}"
: "${VERSION:?VERSION is required}"

OUT_DIR="${OUT_DIR:-${GITHUB_WORKSPACE}/out}"

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required but not found on PATH." >&2
  exit 1
fi

gem_path="${OUT_DIR}/${PROJECT}-${VERSION}.gem"
if [[ ! -f "${gem_path}" ]]; then
  echo "Missing gem artifact: ${gem_path}" >&2
  ls -la "${OUT_DIR}" >&2 || true
  exit 1
fi

local_sha=$(sha256sum "${gem_path}" | awk '{print $1}')

api_url="https://rubygems.org/api/v2/rubygems/${PROJECT}/versions/${VERSION}.json?platform=ruby"
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
    echo "Gem ${PROJECT} ${VERSION} is already published on RubyGems.org (digest match)."
    exit 0
  fi
  echo "Gem ${PROJECT} ${VERSION} already exists on RubyGems.org but digest differs." >&2
  exit 1
fi

if [[ "${status}" == "404" ]]; then
  echo "Gem ${PROJECT} ${VERSION} not found on RubyGems.org yet; will attempt to push."
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
    gem await "${PROJECT}:${VERSION}:ruby"

    status=$(curl -sS -L -o "${resp_json}" -w '%{http_code}' "${api_url}")
    if [[ "${status}" != "200" ]]; then
      echo "After push conflict, RubyGems.org API still not returning 200 (HTTP ${status})." >&2
      cat "${push_err}" >&2 || true
      exit 1
    fi

    remote_sha=$(jq -r '.sha // empty' "${resp_json}")
    if [[ "${remote_sha}" == "${local_sha}" ]]; then
      echo "Gem ${PROJECT} ${VERSION} is already published on RubyGems.org (digest match)."
      exit 0
    fi

    echo "Gem ${PROJECT} ${VERSION} exists on RubyGems.org but digest differs." >&2
    exit 1
  fi

  echo "Failed to publish gem to RubyGems.org." >&2
  cat "${push_err}" >&2 || true
  exit 1
fi

gem await "${PROJECT}:${VERSION}:ruby"
echo "Published gem ${PROJECT} ${VERSION} to RubyGems.org."
