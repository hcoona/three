#!/usr/bin/env bash

# Publish a Node package tarball to npmjs.org with rerun-safe digest validation.
#
# Required env vars:
#   PROJECT    Unscoped npm package name
#   VERSION    Package version
#   DIST_TAG   npm dist-tag to publish under
#
# Optional env vars:
#   TARBALL    Path to the tarball (default: "$GITHUB_WORKSPACE/out/npmjs.tgz")
#   REGISTRY   Registry URL (default: "https://registry.npmjs.org")

set -Eeuo pipefail

: "${PROJECT:?PROJECT is required}"
: "${VERSION:?VERSION is required}"
: "${DIST_TAG:?DIST_TAG is required}"

TARBALL="${TARBALL:-${GITHUB_WORKSPACE}/out/npmjs.tgz}"
REGISTRY="${REGISTRY:-https://registry.npmjs.org}"

npm_name="${PROJECT}"

tmp_dir="${RUNNER_TEMP:-/tmp}"

if [[ ! -f "${TARBALL}" ]]; then
  echo "Missing tarball: ${TARBALL}" >&2
  exit 1
fi

local_sri=$(node - <<'NODE'
const fs = require('fs');
const crypto = require('crypto');
const p = process.argv[1];
const buf = fs.readFileSync(p);
const hash = crypto.createHash('sha512').update(buf).digest('base64');
console.log(`sha512-${hash}`);
NODE
"${TARBALL}")

err_file="${tmp_dir}/npm-view-npmjs.err"
: >"${err_file}"

set +e
remote_integrity=$(npm view "${npm_name}@${VERSION}" dist.integrity --registry "${REGISTRY}" 2>"${err_file}")
view_rc=$?
set -e

if [[ "${view_rc}" -eq 0 ]]; then
  remote_integrity=$(printf '%s' "${remote_integrity}" | tr -d '\r\n')
  if [[ -z "${remote_integrity}" ]]; then
    echo "npm view returned empty dist.integrity for ${npm_name}@${VERSION} on ${REGISTRY}." >&2
    exit 1
  fi
  if [[ "${remote_integrity}" == "${local_sri}" ]]; then
    echo "${npm_name}@${VERSION} already exists on npmjs.org (integrity match)."
    exit 0
  fi
  echo "${npm_name}@${VERSION} already exists on npmjs.org but integrity differs." >&2
  echo "  remote: ${remote_integrity}" >&2
  echo "  local:  ${local_sri}" >&2
  exit 1
fi

if grep -Eqi '(E404|404)' "${err_file}"; then
  echo "${npm_name}@${VERSION} not found on npmjs.org; will publish."
elif grep -Eqi '(E401|E403|401|403)' "${err_file}"; then
  echo "Auth/permission error while querying npmjs.org registry." >&2
  cat "${err_file}" >&2 || true
  exit 1
else
  echo "Failed to query npmjs.org registry (npm view exit ${view_rc})." >&2
  cat "${err_file}" >&2 || true
  exit 1
fi

publish_err="${tmp_dir}/npm-publish-npmjs.err"
: >"${publish_err}"

set +e
npm publish "${TARBALL}" --registry "${REGISTRY}" --tag "${DIST_TAG}" --access public 2>"${publish_err}"
publish_rc=$?
set -e

if [[ "${publish_rc}" -eq 0 ]]; then
  echo "Published ${npm_name}@${VERSION} to npmjs.org."
  exit 0
fi

if grep -Eqi '(previously published|cannot publish over|already exists|you cannot publish over)' "${publish_err}"; then
  echo "Publish reported existing version; re-checking integrity."
  remote_integrity=$(npm view "${npm_name}@${VERSION}" dist.integrity --registry "${REGISTRY}")
  remote_integrity=$(printf '%s' "${remote_integrity}" | tr -d '\r\n')
  if [[ "${remote_integrity}" == "${local_sri}" ]]; then
    echo "${npm_name}@${VERSION} already exists on npmjs.org (integrity match)."
    exit 0
  fi
  echo "${npm_name}@${VERSION} exists on npmjs.org but integrity differs." >&2
  exit 1
fi

echo "Failed to publish to npmjs.org." >&2
cat "${publish_err}" >&2 || true
exit 1
