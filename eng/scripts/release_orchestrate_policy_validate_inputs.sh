#!/usr/bin/env bash
set -Eeuo pipefail

assert_equals() {
  local key="$1"
  local actual="$2"
  local expected="$3"
  local channel="${CHANNEL}"  # capture at call-time; explicit rather than relying on scope
  if [[ "${actual}" != "${expected}" ]]; then
    echo "Channel '${channel}' policy mismatch for '${key}': expected '${expected}', got '${actual}'." >&2
    exit 1
  fi
}

is_channel_allowlisted() {
  local candidate="$1"
  local entry
  IFS=',' read -ra allowlist <<< "${CHANNEL_ALLOWLIST:-}"
  for entry in "${allowlist[@]}"; do
    # Trim leading and trailing whitespace.
    entry="${entry#"${entry%%[![:space:]]*}"}"
    entry="${entry%"${entry##*[![:space:]]}"}"
    [[ -z "${entry}" ]] && continue
    # Reserved channels cannot be allowlisted.
    # 'official' and 'buddy' are first-class policy channels with fixed assertion matrices.
    if [[ "${entry}" == "official" || "${entry}" == "buddy" ]]; then
      echo "Allowlist entry '${entry}' is invalid: 'official' and 'buddy' are policy-governed built-in channels and cannot appear in channel_allowlist. Remove this entry from channel_allowlist." >&2
      exit 1
    fi
    # 'x-official' and 'x-buddy' are reserved sanitization escape slugs used by the hub
    # context job to remap near-miss inputs (e.g. 'official-' → 'x-official') and thus
    # prevent impersonation of the real official/buddy environment gates.
    # NOTE: The underscore variants 'x_official' / 'x_buddy' are intentionally NOT blocked
    # here: the hub's sanitization always produces 'x-official'/'x-buddy' (hyphen), so
    # 'release-x_official' ≠ 'release-x-official' — they are distinct environment names and
    # do not collide with the escape-slug environments.
    if [[ "${entry}" == "x-official" || "${entry}" == "x-buddy" ]]; then
      echo "Channel '${entry}' is reserved (hyphen form only — underscore variants 'x_official'/'x_buddy' are distinct names and are not blocked). These hyphenated forms are reserved as internal sanitisation escape slugs; they prevent environment-name collisions with the release-official/release-buddy gates. Choose a different channel name." >&2
      exit 1
    fi
    # Validate channel name charset: only lowercase letters, digits, hyphens, and underscores;
    # must start with a lowercase letter or digit; every hyphen or underscore must be
    # immediately followed by a lowercase letter or digit (no leading/trailing separators,
    # no consecutive separator pairs: --, __, -_, _-). This guarantees the sanitised form produced
    # by resolve-hub-context (which collapses consecutive dashes via sed s/-{2,}/-/g) is
    # identical to the raw entry — making the allowlist → target_environment mapping injective
    # and preventing near-miss collisions like 'my--channel' → 'my-channel'.
    # BREAKING (Step 2): This regex is stricter than the pre-Step-2 pattern (^[a-z0-9_-]+$).
    # channel_allowlist entries with consecutive hyphens, consecutive underscores, leading/trailing
    # separators, or mixed sequences are no longer accepted. Migration examples:
    #   my--channel  → my-channel   (consecutive hyphens: rename manually — the new regex rejects
    #                               this allowlist entry; note: sed auto-collapse of consecutive dashes
    #                               applies only to direct CHANNEL inputs in the hub job, not here)
    #   my__channel  → my_channel or my-channel   (consecutive underscores: NOT auto-collapsed;
    #                               rename manually — sed preserves underscore runs by design)
    #   -beta        → beta         (leading separator removed)
    #   alpha-       → alpha        (trailing hyphen separator removed)
    #   alpha_       → alpha        (trailing underscore separator removed)
    #   a_-b         → a-b          (mixed sequence normalized)
    if [[ ! "${entry}" =~ ^[a-z0-9]([a-z0-9]|[-_][a-z0-9])*$ ]]; then
      echo "Invalid channel name '${entry}' in channel_allowlist." >&2
      echo "  Required format: start and end with a lowercase letter or digit; each hyphen or underscore must be immediately preceded and followed by a lowercase letter or digit (no consecutive separators, mixed sequences, or leading/trailing separators)." >&2
      echo "  Valid examples: staging, my-channel, canary2" >&2
      echo "  Common fixes: use lowercase only; remove leading/trailing separators; avoid consecutive hyphens/underscores." >&2
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

# Reject empty channel.
if [[ -z "${CHANNEL}" ]]; then
  echo "Channel must not be empty." >&2
  exit 1
fi

# Reject channel names containing any whitespace (leading, trailing, or internal).
# The hub routing job strips ALL whitespace via ${CHANNEL//[[:space:]]/} before routing,
# but policy evaluates the raw dispatch value. Any whitespace mismatch means policy and hub
# evaluate different effective channel strings, and is_channel_allowlisted would fail to
# match (the allowlist trimming loop trims allowlist entries, not CHANNEL itself).
if [[ "${CHANNEL}" =~ [[:space:]] ]]; then
  echo "Channel '${CHANNEL}' contains whitespace (leading, trailing, or internal). Channel names must be free of all whitespace." >&2
  exit 1
fi

# Channel names must be all-lowercase. The hub routing job normalises channel to
# lowercase before routing, but policy validates the raw dispatch value. A mismatch
# means policy and hub would evaluate different values — fail fast with an actionable
# message rather than falling through to 'Unknown channel' in the case below.
if [[ "${CHANNEL}" != "${CHANNEL,,}" ]]; then
  lower_channel="${CHANNEL,,}"
  if [[ "${lower_channel}" == "x-official" || "${lower_channel}" == "x-buddy" ]]; then
    echo "Channel '${CHANNEL}' contains uppercase characters, and its lowercase form '${lower_channel}' is reserved by the release system. Choose a different channel name." >&2
  else
    msg="Channel '${CHANNEL}' contains uppercase characters. Channel names must be all-lowercase (e.g., use '${lower_channel}')."
    if [[ "${lower_channel}" == "official" || "${lower_channel}" == "buddy" ]]; then
      msg+=' Note: the lowercase form is a policy-governed built-in channel that requires specific flag values — verify all channel profile flags before using it.'
    fi
    echo "${msg} The hub routing job normalises channel to lowercase; passing uppercase here means policy and hub would evaluate different values." >&2
  fi
  exit 1
fi

# Explicitly reject 'x-official' and 'x-buddy' as CHANNEL inputs. These values are
# reserved as sanitization escape slugs: the hub context job remaps near-miss inputs
# (e.g. 'official-' → 'x-official') to prevent them from impersonating 'release-official'
# or 'release-buddy'. Accepting them as direct channel inputs would route to
# 'release-x-official'/'release-x-buddy' and undermine the escape-slug convention.
if [[ "${CHANNEL}" == "x-official" || "${CHANNEL}" == "x-buddy" ]]; then
  echo "Channel '${CHANNEL}' is reserved as an internal remapping slug and cannot be used as a direct channel input. These names are used by the release system to prevent environment name collisions. Choose a different channel name. See .github/workflows/REFACTOR_PLAN.md \"Breaking changes in Step 2\" for migration guidance." >&2
  exit 1
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
    echo "::notice::Official channel policy requires publish_node_npmjs=true. For WXT (browser extension) projects this flag satisfies policy but has no runtime effect (WXT artifacts are not published as npm packages); for Node-npm projects it gates npmjs publishing."
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
    # Validate CHANNEL format before allowlist lookup so operators get a targeted
    # "invalid format" error (with rename guidance) rather than the generic
    # "Unknown channel — add to allowlist" message, which would send them in the
    # wrong direction (adding an invalid-format entry would itself fail immediately).
    # The official/buddy values never reach this branch (handled by the case arms above),
    # and x-official/x-buddy are rejected by the pre-case guards, so we check only
    # the custom-channel format here.
    if [[ ! "${CHANNEL}" =~ ^[a-z0-9]([a-z0-9]|[-_][a-z0-9])*$ ]]; then
      echo "Channel '${CHANNEL}' has an invalid format and cannot be used or allowlisted." >&2
      echo "  Required format: start and end with a lowercase letter or digit;" >&2
      echo "  each hyphen or underscore must be immediately preceded and followed by a letter or digit." >&2
      echo "  No consecutive separators, mixed sequences, or leading/trailing separators." >&2
      echo "  Valid examples: staging, my-channel, canary2" >&2
      echo "  Common fixes: use lowercase only; remove leading/trailing separators; avoid consecutive hyphens/underscores." >&2
      echo "  See .github/workflows/REFACTOR_PLAN.md \"Breaking changes in Step 2\" for migration examples." >&2
      exit 1
    fi
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
      # SYNC: add-new-language — add a prohibition clause here for each new production
      # registry flag (PUBLISH_<LANG>_<REGISTRY>) to prevent allowlisted custom channels
      # from publishing to production registries without explicit policy approval.
      # Note: GPR flags (publish_node_gpr, publish_ruby_gpr) are intentionally excluded —
      # GPR authenticates via github.token (no OIDC, no environment reviewers required).
      if [[ "${PUBLISH_PYTHON_PYPI}" == "true" ]]; then
        echo "Allowlisted channel '${CHANNEL}' may not set publish_python_pypi=true (production registry)." >&2
        echo "Production registry publishing (PyPI/npmjs/RubyGems) is restricted to the official channel only." >&2
        exit 1
      fi
      if [[ "${PUBLISH_NODE_NPMJS}" == "true" ]]; then
        echo "Allowlisted channel '${CHANNEL}' may not set publish_node_npmjs=true (production registry)." >&2
        echo "Production registry publishing (PyPI/npmjs/RubyGems) is restricted to the official channel only." >&2
        exit 1
      fi
      if [[ "${PUBLISH_RUBY_RUBYGEMS}" == "true" ]]; then
        echo "Allowlisted channel '${CHANNEL}' may not set publish_ruby_rubygems=true (production registry)." >&2
        echo "Production registry publishing (PyPI/npmjs/RubyGems) is restricted to the official channel only." >&2
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
      } >> "${GITHUB_STEP_SUMMARY:-/dev/null}" || true
    else
      echo "Unknown channel '${CHANNEL}'. Refusing to continue without explicit allowlisting." >&2
      echo "Set 'channel_allowlist' to include '${CHANNEL}' only when this is intentional." >&2
      exit 1
    fi
    ;;
esac
