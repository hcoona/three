#!/usr/bin/env bash

# Publish a Ruby gem to GitHub Packages (RubyGems registry) with rerun-safe digest validation.
#
# This script uses an authenticated source URL for fetch verification and the
# RubyGems credentials file for push authentication.
#
# Required env vars:
#   PACKAGE_NAME     Planner-frozen Ruby package name
#   PACKAGE_VERSION  Planner-frozen Ruby package version
#   OWNER    GitHub repository owner (e.g. hcoona)
#   RUBY_GPR_USER     GitHub username for authenticated package fetches
#   RUBY_GPR_TOKEN    GitHub token (GITHUB_TOKEN)
#
# Optional env vars:
#   OUT_DIR  Directory containing the built gem (default: "$GITHUB_WORKSPACE/out")
#   EXPECTED_GEM_FILENAME  Planner-frozen gem basename (default: "$PACKAGE_NAME-$PACKAGE_VERSION.gem")
#   EXPECTED_GEM_SHA256    Compatibility-only lowercase SHA-256 digest (normally unset)

set -Eeuo pipefail

: "${PACKAGE_NAME:?PACKAGE_NAME is required}"
: "${PACKAGE_VERSION:?PACKAGE_VERSION is required}"
: "${OWNER:?OWNER is required}"
: "${RUBY_GPR_USER:?RUBY_GPR_USER is required}"
: "${RUBY_GPR_TOKEN:?RUBY_GPR_TOKEN is required}"

OUT_DIR="${OUT_DIR:-${GITHUB_WORKSPACE}/out}"

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

cred_path=$(gem env credentials)
mkdir -p "$(dirname "${cred_path}")"
{
  echo ":github: Bearer ${RUBY_GPR_TOKEN}"
} >"${cred_path}"
chmod 600 "${cred_path}"

authenticated_source=$(
  RUBY_GPR_USER="${RUBY_GPR_USER}" \
    RUBY_GPR_TOKEN="${RUBY_GPR_TOKEN}" \
    OWNER="${OWNER}" \
    ruby -ruri -e 'puts "https://#{URI.encode_www_form_component(ENV.fetch("RUBY_GPR_USER"))}:#{URI.encode_www_form_component(ENV.fetch("RUBY_GPR_TOKEN"))}@rubygems.pkg.github.com/#{ENV.fetch("OWNER")}/"'
)

redact_gpr_token() {
  RUBY_GPR_AUTHENTICATED_SOURCE="${authenticated_source}" ruby -ruri -e '
    token = ENV.fetch("RUBY_GPR_TOKEN", "")
    source = ENV.fetch("RUBY_GPR_AUTHENTICATED_SOURCE", "")
    encoded_token = token.empty? ? "" : URI.encode_www_form_component(token)
    text = STDIN.read
    [source, token, encoded_token].each do |secret|
      text = text.gsub(secret, "<redacted>") unless secret.empty?
    end
    print text
  '
}

tmpdir="$(mktemp -d)"
trap 'rm -rf "${tmpdir}"' EXIT

expected_remote_file="${expected_gem_filename}"
remote_file="${tmpdir}/${expected_remote_file}"
fetch_err="${tmpdir}/fetch.err"

set +e
(cd "${tmpdir}" && gem fetch "${PACKAGE_NAME}" -v "${PACKAGE_VERSION}" --norc --silent --clear-sources --source "${authenticated_source}") 2>"${fetch_err}"
fetch_rc=$?
set -e

if [[ "${fetch_rc}" -eq 0 && -f "${remote_file}" ]]; then
  remote_sha=$(sha256sum "${remote_file}" | awk '{print $1}')
  if [[ "${local_sha}" == "${remote_sha}" ]]; then
    echo "Gem ${PACKAGE_NAME} ${PACKAGE_VERSION} is already published on GitHub Packages (digest match)."
    exit 0
  fi
  echo "Gem ${PACKAGE_NAME} ${PACKAGE_VERSION} already exists on GitHub Packages but digest differs." >&2
  exit 1
fi

if [[ "${fetch_rc}" -eq 0 && ! -f "${remote_file}" ]]; then
  echo "gem fetch succeeded but did not produce expected file '${expected_remote_file}' in the temp directory." >&2
  echo "Refusing to fall through to 'gem push' on an ambiguous fetch outcome." >&2
  ls -la "${tmpdir}" >&2 || true
  exit 1
fi

if [[ "${fetch_rc}" -ne 0 ]]; then
  if grep -Eqi '(401|403|unauthorized|forbidden)' "${fetch_err}"; then
    echo "Authentication/permission error while checking GitHub Packages RubyGems registry." >&2
    redact_gpr_token <"${fetch_err}" >&2 || true
    echo "Common fix: ensure the gem is linked to this repository and Actions has access to publish packages." >&2
    exit 1
  fi
  if grep -Eqi '(could not find|\b404\b|not found)' "${fetch_err}"; then
    echo "Gem ${PACKAGE_NAME} ${PACKAGE_VERSION} not found on GitHub Packages yet; will attempt to push."
  else
    echo "Failed to check existing gem on GitHub Packages (gem fetch exit ${fetch_rc})." >&2
    redact_gpr_token <"${fetch_err}" >&2 || true
    exit 1
  fi
fi

push_err="${tmpdir}/push.err"
set +e
gem push --key github --host "https://rubygems.pkg.github.com/${OWNER}" "${gem_path}" 2>"${push_err}"
push_rc=$?
set -e

if [[ "${push_rc}" -eq 0 ]]; then
  echo "Published gem ${PACKAGE_NAME} ${PACKAGE_VERSION} to GitHub Packages."
  exit 0
fi

if grep -Eqi '(already (exists|been pushed)|repushing|already pushed)' "${push_err}"; then
  echo "Push reported existing version; retrying fetch to verify digest (eventual consistency)."
  for i in 1 2 3 4 5; do
    sleep $((i * 3))
    : >"${fetch_err}"

    set +e
    (cd "${tmpdir}" && rm -f "${expected_remote_file}" && gem fetch "${PACKAGE_NAME}" -v "${PACKAGE_VERSION}" --norc --silent --clear-sources --source "${authenticated_source}") 2>"${fetch_err}"
    fetch_rc=$?
    set -e

    if [[ "${fetch_rc}" -eq 0 && -f "${remote_file}" ]]; then
      remote_sha=$(sha256sum "${remote_file}" | awk '{print $1}')
      if [[ "${local_sha}" == "${remote_sha}" ]]; then
        echo "Gem ${PACKAGE_NAME} ${PACKAGE_VERSION} is already published on GitHub Packages (digest match)."
        exit 0
      fi
      echo "Gem ${PACKAGE_NAME} ${PACKAGE_VERSION} exists on GitHub Packages but digest differs." >&2
      exit 1
    fi

    if [[ "${fetch_rc}" -eq 0 && ! -f "${remote_file}" ]]; then
      echo "gem fetch succeeded but did not produce expected file '${expected_remote_file}' during verification retry." >&2
      echo "Refusing to continue on an ambiguous fetch outcome." >&2
      ls -la "${tmpdir}" >&2 || true
      exit 1
    fi
  done

  echo "Unable to verify already-published gem after push conflict." >&2
  cat "${push_err}" >&2 || true
  exit 1
fi

echo "Failed to publish gem to GitHub Packages." >&2
cat "${push_err}" >&2 || true
exit 1
