#!/usr/bin/env bash
set -Eeuo pipefail

assert_equals() {
  local key="$1"
  local actual="$2"
  local expected="$3"
  if [[ "${actual}" != "${expected}" ]]; then
    echo "Channel '${CHANNEL}' policy mismatch for '${key}': expected '${expected}', got '${actual}'." >&2
    exit 1
  fi
}

is_channel_allowlisted() {
  local candidate="$1"
  local entry
  IFS=',' read -ra allowlist <<< "${CHANNEL_ALLOWLIST}"
  for entry in "${allowlist[@]}"; do
    # Trim leading and trailing whitespace.
    entry="${entry#"${entry%%[![:space:]]*}"}"
    entry="${entry%"${entry##*[![:space:]]}"}"
    [[ -z "${entry}" ]] && continue
    # Reserved channels cannot be allowlisted.
    if [[ "${entry}" == "official" || "${entry}" == "buddy" ]]; then
      echo "Reserved channel '${entry}' cannot appear in channel_allowlist." >&2
      exit 1
    fi
    # Validate channel name charset: only lowercase letters, digits, hyphens, and underscores.
    if [[ ! "${entry}" =~ ^[a-z0-9_-]+$ ]]; then
      echo "Invalid channel name '${entry}' in channel_allowlist: only lowercase letters (a-z), digits (0-9), hyphens (-), and underscores (_) are allowed." >&2
      exit 1
    fi
    # Check if this entry matches the candidate.
    if [[ "${entry}" == "${candidate}" ]]; then
      return 0
    fi
  done
  return 1
}

if [[ "${SOURCE}" != "tag" && "${SOURCE}" != "manual" ]]; then
  echo "Invalid source '${SOURCE}'." >&2
  exit 1
fi

if [[ "${SOURCE}" == "tag" ]]; then
  if [[ -z "${REF_NAME}" ]]; then
    echo "source=tag requires ref_name to be set." >&2
    exit 1
  fi
  if [[ -z "${REF}" ]]; then
    echo "source=tag requires ref to be set." >&2
    exit 1
  fi
fi

if [[ "${SOURCE}" == "manual" ]]; then
  if [[ -z "${PROJECT}" ]]; then
    echo "source=manual requires project to be set." >&2
    exit 1
  fi
  if [[ -z "${VERSION}" ]]; then
    echo "source=manual requires version to be set." >&2
    exit 1
  fi
fi

case "${CHANNEL}" in
  official)
    assert_equals "enforce_prerelease_only" "${ENFORCE_PRERELEASE_ONLY}" "false"
    assert_equals "enforce_non_clobber" "${ENFORCE_NON_CLOBBER}" "false"
    # SYNC: add-new-language — add assert_equals for publish_<lang>_<registry> in official channel below
    assert_equals "publish_python_pypi" "${PUBLISH_PYTHON_PYPI}" "true"
    assert_equals "publish_node_gpr" "${PUBLISH_NODE_GPR}" "true"
    # Note: WXT projects must still pass publish_node_npmjs=true to satisfy policy,
    # but all Node publish jobs are gated on is_wxt != 'true', so this flag has
    # no runtime effect for WXT projects.
    echo "Note: WXT (browser extension) projects require publish_node_npmjs=true by channel policy even though it has no runtime effect (WXT artifacts are published as browser extension archives, not npm packages). If this assertion fails, set publish_node_npmjs=true in the caller workflow."
    assert_equals "publish_node_npmjs" "${PUBLISH_NODE_NPMJS}" "true"
    assert_equals "publish_ruby_gpr" "${PUBLISH_RUBY_GPR}" "true"
    assert_equals "publish_ruby_rubygems" "${PUBLISH_RUBY_RUBYGEMS}" "true"
    assert_equals "enable_attestation" "${ENABLE_ATTESTATION}" "true"
    assert_equals "github_release_prerelease" "${GITHUB_RELEASE_PRERELEASE}" "false"
    ;;
  buddy)
    assert_equals "enforce_prerelease_only" "${ENFORCE_PRERELEASE_ONLY}" "true"
    assert_equals "enforce_non_clobber" "${ENFORCE_NON_CLOBBER}" "true"
    # SYNC: add-new-language (B2) — add assert_equals / assert_disabled checks for the new
    # language flags under the buddy channel case. This step is NOT optional: omitting it causes a
    # silent policy bypass where the buddy channel will not enforce the new language's flags and
    # misconfigured callers will pass policy validation without error.
    # B2: Add assert_disabled / assert_equals checks for the new language flags under the buddy channel case.
    assert_equals "publish_python_pypi" "${PUBLISH_PYTHON_PYPI}" "false"
    assert_equals "publish_node_gpr" "${PUBLISH_NODE_GPR}" "true"
    assert_equals "publish_node_npmjs" "${PUBLISH_NODE_NPMJS}" "false"
    assert_equals "publish_ruby_gpr" "${PUBLISH_RUBY_GPR}" "true"
    assert_equals "publish_ruby_rubygems" "${PUBLISH_RUBY_RUBYGEMS}" "false"
    assert_equals "enable_attestation" "${ENABLE_ATTESTATION}" "false"
    assert_equals "github_release_prerelease" "${GITHUB_RELEASE_PRERELEASE}" "true"
    ;;
  *)
    # SECURITY: Allowlisted channels bypass ALL policy assertions (official/buddy
    # profile matrix is not enforced). This is an intentional escape hatch for
    # non-production channels (e.g., staging, canary). Access control relies on
    # repository branch protection — restrict default-branch merge to trusted contributors.
    if is_channel_allowlisted "${CHANNEL}"; then
      echo "Channel '${CHANNEL}' is explicitly allowlisted; skipping strict channel profile assertions."
      # github_release_prerelease is not enforced for custom channels.
      # The caller is responsible for setting it correctly.
      # SECURITY: Production registry publishing (PyPI/npmjs/RubyGems) is prohibited
      # for allowlisted channels. These registries rely on GitHub environment required
      # reviewers as the sole gating mechanism; the reusable workflow cannot enforce
      # caller origin. To intentionally publish a custom channel to a production
      # registry, remove these assertions and accept full responsibility for access control.
      if [[ "${PUBLISH_PYTHON_PYPI}" == "true" ]]; then
        echo "Allowlisted channel '${CHANNEL}' may not set publish_python_pypi=true (production registry)." >&2
        echo "Production registry publishing is reserved for official and buddy channels." >&2
        exit 1
      fi
      if [[ "${PUBLISH_NODE_NPMJS}" == "true" ]]; then
        echo "Allowlisted channel '${CHANNEL}' may not set publish_node_npmjs=true (production registry)." >&2
        echo "Production registry publishing is reserved for official and buddy channels." >&2
        exit 1
      fi
      if [[ "${PUBLISH_RUBY_RUBYGEMS}" == "true" ]]; then
        echo "Allowlisted channel '${CHANNEL}' may not set publish_ruby_rubygems=true (production registry)." >&2
        echo "Production registry publishing is reserved for official and buddy channels." >&2
        exit 1
      fi
      # NOTE: publish_node_gpr and publish_ruby_gpr are intentionally not blocked for
      # allowlisted channels. GitHub Packages (GPR) authenticates via github.token which
      # is already scoped to the repository — no OIDC nor environment reviewers are
      # involved. Custom channels (e.g., staging) commonly use GPR to distribute
      # pre-production artifacts. If you want to prevent GPR publishing for custom
      # channels, add an explicit prohibition here.
      echo "::notice title=Custom channel::Allowlisted channel '${CHANNEL}' active. Actor: ${GITHUB_ACTOR}, ref: ${GITHUB_REF_NAME}. Verify branch protection restricts merge access to trusted contributors."
      {
        echo ""
        echo "> [!WARNING]"
        echo "> **Custom channel \`${CHANNEL}\` is allowlisted.** Actor: \`${GITHUB_ACTOR}\`, ref: \`${GITHUB_REF_NAME}\`."
        echo "> Access control relies entirely on repository branch protection."
        echo "> Ensure only trusted contributors have merge access to this caller workflow file."
      } >> "${GITHUB_STEP_SUMMARY}"
    else
      echo "Unknown channel '${CHANNEL}'. Refusing to continue without explicit allowlisting." >&2
      echo "Set 'channel_allowlist' to include '${CHANNEL}' only when this is intentional." >&2
      exit 1
    fi
    ;;
esac
