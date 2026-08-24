#!/usr/bin/env bash

# Publish a Node package tarball to GitHub Packages (npm.pkg.github.com) with rerun-safe digest validation.
#
# Required env vars:
#   OWNER      GitHub repository owner (e.g. hcoona)
#   PROJECT    Unscoped project name
#   VERSION    Package version
#   DIST_TAG   npm dist-tag to publish under
#
# Optional env vars:
#   TARBALL    Path to the tarball (default: "$GITHUB_WORKSPACE/out/gpr.tgz")
#   REGISTRY   Registry URL (default: "https://npm.pkg.github.com")

set -Eeuo pipefail

: "${OWNER:?OWNER is required}"
: "${PROJECT:?PROJECT is required}"
: "${VERSION:?VERSION is required}"
: "${DIST_TAG:?DIST_TAG is required}"

TARBALL="${TARBALL:-${GITHUB_WORKSPACE}/out/gpr.tgz}"
REGISTRY="${REGISTRY:-https://npm.pkg.github.com}"

pkg_name="@${OWNER,,}/${PROJECT}"

if [[ ! -f "${TARBALL}" ]]; then
  echo "Missing tarball: ${TARBALL}" >&2
  exit 1
fi

local_sri=$(
  node - "${TARBALL}" <<'NODE'
const fs = require('fs');
const crypto = require('crypto');
const p = process.argv[2];
const buf = fs.readFileSync(p);
const hash = crypto.createHash('sha512').update(buf).digest('base64');
console.log(`sha512-${hash}`);
NODE
)

err_file="${RUNNER_TEMP:-/tmp}/npm-view-gpr.err"
: >"${err_file}"

set +e
remote_integrity=$(npm view "${pkg_name}@${VERSION}" dist.integrity --registry "${REGISTRY}" 2>"${err_file}")
view_rc=$?
set -e

if [[ "${view_rc}" -eq 0 ]]; then
  remote_integrity=$(printf '%s' "${remote_integrity}" | tr -d '\r\n')
  if [[ -z "${remote_integrity}" ]]; then
    echo "npm view returned empty dist.integrity for ${pkg_name}@${VERSION} on ${REGISTRY}." >&2
    exit 1
  fi
  if [[ "${remote_integrity}" == "${local_sri}" ]]; then
    echo "${pkg_name}@${VERSION} already exists on GitHub Packages (integrity match)."
    exit 0
  fi
  echo "${pkg_name}@${VERSION} already exists on GitHub Packages but integrity differs." >&2
  echo "  remote: ${remote_integrity}" >&2
  echo "  local:  ${local_sri}" >&2
  exit 1
fi

if grep -Eqi '(E404|\b404\b)' "${err_file}"; then
  echo "${pkg_name}@${VERSION} not found on GitHub Packages; will publish."
elif grep -Eqi '(E401|E403|\b401\b|\b403\b)' "${err_file}"; then
  echo "Auth/permission error while querying GitHub Packages registry." >&2
  cat "${err_file}" >&2 || true
  exit 1
else
  echo "Failed to query GitHub Packages registry (npm view exit ${view_rc})." >&2
  cat "${err_file}" >&2 || true
  exit 1
fi

publish_err="${RUNNER_TEMP:-/tmp}/npm-publish-gpr.err"
: >"${publish_err}"

set +e
npm publish "${TARBALL}" --registry "${REGISTRY}" --tag "${DIST_TAG}" 2>"${publish_err}"
publish_rc=$?
set -e

if [[ "${publish_rc}" -eq 0 ]]; then
  echo "Published ${pkg_name}@${VERSION} to GitHub Packages."
  exit 0
fi

if grep -Eqi '(previously published|cannot publish over|forbidden|already exists|you cannot publish over)' "${publish_err}"; then
  echo "Publish reported existing version; re-checking integrity."
  remote_integrity=$(npm view "${pkg_name}@${VERSION}" dist.integrity --registry "${REGISTRY}")
  remote_integrity=$(printf '%s' "${remote_integrity}" | tr -d '\r\n')
  if [[ "${remote_integrity}" == "${local_sri}" ]]; then
    echo "${pkg_name}@${VERSION} already exists on GitHub Packages (integrity match)."
    exit 0
  fi
  echo "${pkg_name}@${VERSION} exists on GitHub Packages but integrity differs." >&2
  exit 1
fi

echo "Failed to publish to GitHub Packages." >&2
cat "${publish_err}" >&2 || true
exit 1
