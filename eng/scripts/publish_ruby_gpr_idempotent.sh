#!/usr/bin/env bash

# Publish a Ruby gem to GitHub Packages (RubyGems registry) with rerun-safe digest validation.
#
# This script intentionally relies on the RubyGems credentials file for authentication.
#
# Required env vars:
#   PROJECT  Gem name
#   VERSION  Gem version
#   OWNER    GitHub repository owner (e.g. hcoona)
#   TOKEN    GitHub token (GITHUB_TOKEN)
#
# Optional env vars:
#   OUT_DIR  Directory containing the built gem (default: "$GITHUB_WORKSPACE/out")

set -Eeuo pipefail

: "${PROJECT:?PROJECT is required}"
: "${VERSION:?VERSION is required}"
: "${OWNER:?OWNER is required}"
: "${TOKEN:?TOKEN is required}"

OUT_DIR="${OUT_DIR:-${GITHUB_WORKSPACE}/out}"

gem_path="${OUT_DIR}/${PROJECT}-${VERSION}.gem"
if [[ ! -f "${gem_path}" ]]; then
  echo "Missing gem artifact: ${gem_path}" >&2
  ls -la "${OUT_DIR}" >&2 || true
  exit 1
fi

cred_path=$(gem env credentials)
mkdir -p "$(dirname "${cred_path}")"
{
  echo ":github: Bearer ${TOKEN}"
} >"${cred_path}"
chmod 600 "${cred_path}"

tmpdir="$(mktemp -d)"
trap 'rm -rf "${tmpdir}"' EXIT

expected_remote_file="${PROJECT}-${VERSION}.gem"
remote_file="${tmpdir}/${expected_remote_file}"
fetch_err="${tmpdir}/fetch.err"

set +e
(cd "${tmpdir}" && gem fetch "${PROJECT}" -v "${VERSION}" --norc --silent --clear-sources --source "https://rubygems.pkg.github.com/${OWNER}/") 2>"${fetch_err}"
fetch_rc=$?
set -e

if [[ "${fetch_rc}" -eq 0 && -f "${remote_file}" ]]; then
  local_sha=$(sha256sum "${gem_path}" | awk '{print $1}')
  remote_sha=$(sha256sum "${remote_file}" | awk '{print $1}')
  if [[ "${local_sha}" == "${remote_sha}" ]]; then
    echo "Gem ${PROJECT} ${VERSION} is already published on GitHub Packages (digest match)."
    exit 0
  fi
  echo "Gem ${PROJECT} ${VERSION} already exists on GitHub Packages but digest differs." >&2
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
    cat "${fetch_err}" >&2 || true
    echo "Common fix: ensure the gem is linked to this repository and Actions has access to publish packages." >&2
    exit 1
  fi
  if grep -Eqi '(could not find|\b404\b|not found)' "${fetch_err}"; then
    echo "Gem ${PROJECT} ${VERSION} not found on GitHub Packages yet; will attempt to push."
  else
    echo "Failed to check existing gem on GitHub Packages (gem fetch exit ${fetch_rc})." >&2
    cat "${fetch_err}" >&2 || true
    exit 1
  fi
fi

push_err="${tmpdir}/push.err"
set +e
gem push --key github --host "https://rubygems.pkg.github.com/${OWNER}" "${gem_path}" 2>"${push_err}"
push_rc=$?
set -e

if [[ "${push_rc}" -eq 0 ]]; then
  echo "Published gem ${PROJECT} ${VERSION} to GitHub Packages."
  exit 0
fi

if grep -Eqi '(already (exists|been pushed)|repushing|already pushed)' "${push_err}"; then
  echo "Push reported existing version; retrying fetch to verify digest (eventual consistency)."
  for i in 1 2 3 4 5; do
    sleep $((i * 3))
    : >"${fetch_err}"

    set +e
    (cd "${tmpdir}" && rm -f "${expected_remote_file}" && gem fetch "${PROJECT}" -v "${VERSION}" --norc --silent --clear-sources --source "https://rubygems.pkg.github.com/${OWNER}/") 2>"${fetch_err}"
    fetch_rc=$?
    set -e

    if [[ "${fetch_rc}" -eq 0 && -f "${remote_file}" ]]; then
      local_sha=$(sha256sum "${gem_path}" | awk '{print $1}')
      remote_sha=$(sha256sum "${remote_file}" | awk '{print $1}')
      if [[ "${local_sha}" == "${remote_sha}" ]]; then
        echo "Gem ${PROJECT} ${VERSION} is already published on GitHub Packages (digest match)."
        exit 0
      fi
      echo "Gem ${PROJECT} ${VERSION} exists on GitHub Packages but digest differs." >&2
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
