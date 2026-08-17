# Phase 1.5 Git GUI and PATH Discovery Evidence Gate

Status: **Blocked; local shell subset accepted**

Date: **2026-06-05**

Decision ID: **phase-1.5-git-discovery-evidence**

Gate name: **Phase 1.5 Git GUI and PATH discovery gate**

Owner: **ADAPTER-GIT and PLATFORM**

## Gate Status and Decision

| Field                      | Decision                                                                                                                                                                                                                                                                                                                               |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Gate status                | Local Linux Git shell evidence passed for helper-shorthand discovery, PATH-sensitive failure, `useHttpPath`, and an absolute helper under a stripped environment. The full mandatory gate is blocked because this environment did not provide Git for Windows or an actual GUI-launched Git client.                                    |
| Decision                   | Keep the Git adapter shape as `git-credential-<helper-name>` and keep `credential.https://dev.azure.com.useHttpPath=true` for `dev.azure.com`. Do not approve PATH-only installation as a supported Git for Windows or GUI mode from this evidence.                                                                                    |
| Evidence scope             | Source inspection covers Git Credential Manager Azure Repos behavior and its Git configuration model at a clean local mirror. Disposable local prototypes cover Git 2.43.0 on Ubuntu only. The GUI-like prototype used a stripped process environment; it is not evidence for a real GUI Git client.                                   |
| Implementation may proceed | Yes for helper-name discovery shape, `useHttpPath` configuration, and doctor design that asks Git to invoke a fake helper. No for `store`, `erase`, full adapter stdout contract validation, Windows, Git for Windows, GUI, or PATH-only installation modes. Phase 9 and Phase 15 must still run acceptance before release support.    |
| Phase 1R routing           | Required unless the missing Windows and GUI evidence closes first. If that evidence remains missing, dependent work stays blocked until Phase 1R records an approved supported-mode narrowing or resequence decision. PATH-only support for Git for Windows or GUI-launched clients remains unapproved until platform evidence closes. |

## Upstream Snapshot

Reference source inspected from the local mirror of
[git-ecosystem/git-credential-manager](https://github.com/git-ecosystem/git-credential-manager).
The local mirror resolved to commit
[`312354b884aca75efb078bedccf033df97fabb1f`](https://github.com/git-ecosystem/git-credential-manager/commit/312354b884aca75efb078bedccf033df97fabb1f),
described as `v2.8.0-7-g312354b`.

Commands used to identify the snapshot:

```bash
git -C /workspace/public/git-credential-manager --no-pager rev-parse HEAD
git -C /workspace/public/git-credential-manager --no-pager remote -v
git -C /workspace/public/git-credential-manager --no-pager describe --tags --always --dirty
git -C /workspace/public/git-credential-manager --no-pager status --short
```

Results:

```text
HEAD: 312354b884aca75efb078bedccf033df97fabb1f
origin: https://github.com/git-ecosystem/git-credential-manager.git
version description: v2.8.0-7-g312354b
status --short: no output
```

## Evidence Sources

Upstream source and documentation inspected:

- [GCM README status][gcm-readme-status]:
    - GCM is a Git credential helper for HTTP(S) remotes on Windows, macOS, and
      Linux.
- [GCM configuration documentation][gcm-config-helper]:
    - GCM is used by Git only when installed and configured.
    - The documented helper setting is `credential.helper manager`.
- [GCM application configuration][gcm-application-config]:
    - GCM writes a blank `credential.helper` reset entry followed by its executable
      path.
    - This supports an explicit helper path rather than relying only on an ambient
      shell command.
- [GCM path escaping][gcm-path-escaping]:
    - On Windows, GCM converts backslashes to forward slashes for Git config.
    - It escapes spaces and parentheses in the configured helper path.
- [Azure Repos `useHttpPath` configuration][gcm-azrepos-configure]:
    - GCM sets `credential.https://dev.azure.com.useHttpPath=true` during Azure
      Repos configuration.
- [Azure Repos service-name remarks][gcm-azrepos-service]:
    - For `dev.azure.com`, GCM needs Git's path argument to obtain the Azure DevOps
      organization.
    - For `*.visualstudio.com`, the organization is in the host name and the same
      `useHttpPath` workaround is not needed.
- [Azure Repos unconfigure behavior][gcm-azrepos-unconfigure]:
    - On Windows system unconfigure, GCM avoids clearing the `dev.azure.com`
      `useHttpPath` setting if `manager` or `manager-core` remains configured,
      because that would break the Git for Windows bundled GCM.
- [Azure Repos configuration tests][gcm-azrepos-config-tests]:
    - Tests cover setting `useHttpPath=true`, leaving it when already true, and
      removing or preserving it according to configuration scope.
- [GCM architecture UI helper model][gcm-architecture-ui]:
    - Provider-specific GUI interaction is modeled as a separate helper executable
      that core code shells out to.

## Disposable Local Prototype

The disposable harness was launched from repository root:
`/workspace/three-workspaces/azureauth-credprovider`. Each Git credential
probe itself ran with `git -C "$PROBE_CWD"` where `PROBE_CWD` was the
scratch subdirectory
`/workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe/work`.
`GIT_CEILING_DIRECTORIES` was set to the fixture root so Git discovery stopped
before reaching the repository root and could not read repository-local
`.git/config`.

Tool version:

```text
git version 2.43.0
```

Fixture root:

```bash
SCRATCH="$PWD/.copilot-scratch/phase-1.5-git-discovery-probe"
```

The probe used an isolated Git environment for every case:

- `HOME="$SCRATCH/home"` prevented reading the user's global Git config.
- `GIT_CONFIG_NOSYSTEM=1` prevented reading system Git config.
- `GIT_CEILING_DIRECTORIES="$SCRATCH"` plus `git -C "$PROBE_CWD"` prevented
  Git from discovering the repository root and reading repository-local
  `.git/config`.
- `-c credential.helper=` blank-reset the multi-valued helper list before adding
  the case helper.
- stdout, stderr, exit status, and helper logs were captured per case.

This isolation is required for the missing-helper case: the expected failure must
come from Git being unable to discover `git-credential-phase15probe`, not from
falling through to another configured helper such as `manager` or
`manager-core`.

### Reproducible Harness

Runnable harness used for the disposable probe:

```bash
set -u
SCRATCH="$PWD/.copilot-scratch/phase-1.5-git-discovery-probe"
rm -rf "$SCRATCH"
mkdir -p "$SCRATCH/bin" "$SCRATCH/home" "$SCRATCH/cases" "$SCRATCH/work"
cat > "$SCRATCH/bin/git-credential-phase15probe" <<'HELPER'
#!/usr/bin/env bash
set -u
: "${PHASE15_LOG:?PHASE15_LOG is required}"
{
  printf 'argv: [%s]\n' "$*"
  printf 'cwd: %s\n' "$PWD"
  printf 'HOME: %s\n' "${HOME-}"
  printf 'GIT_CONFIG_NOSYSTEM: %s\n' "${GIT_CONFIG_NOSYSTEM-}"
  printf 'GIT_CEILING_DIRECTORIES: %s\n' "${GIT_CEILING_DIRECTORIES-}"
  printf 'PATH: %s\n' "${PATH-}"
  printf 'stdin-begin\n'
  cat
  printf 'stdin-end\n'
  printf -- '---\n'
} >> "$PHASE15_LOG"
if [ "${1-}" = "get" ]; then
  printf 'username=probe-user\n'
  printf 'password=probe-password\n'
  printf '\n'
fi
HELPER
chmod +x "$SCRATCH/bin/git-credential-phase15probe"
INPUT='protocol=https
host=dev.azure.com
path=org/project/_git/repo
'
PROBE_CWD="$SCRATCH/work"
printf 'repo_discovery_check<<EOF\n'
env -i \
  HOME="$SCRATCH/home" \
  GIT_CONFIG_NOSYSTEM=1 \
  GIT_CEILING_DIRECTORIES="$SCRATCH" \
  PATH="/usr/bin:/bin" \
  git -C "$PROBE_CWD" rev-parse --show-toplevel 2>&1 || true
printf 'EOF\n\n'
run_case() {
  name="$1"; shift
  out="$SCRATCH/cases/$name.stdout"
  err="$SCRATCH/cases/$name.stderr"
  log="$SCRATCH/cases/$name.helper.log"
  : > "$log"
  status=0
  printf '%b' "$INPUT" |
    env -i \
      HOME="$SCRATCH/home" \
      GIT_CONFIG_NOSYSTEM=1 \
      GIT_CEILING_DIRECTORIES="$SCRATCH" \
      PATH="$1" \
      PHASE15_LOG="$log" \
      "$2" -C "$PROBE_CWD" "${@:3}" > "$out" 2> "$err" || status=$?
  printf 'case=%s\n' "$name"
  printf 'status=%s\n' "$status"
  printf 'stdout<<EOF\n'; cat "$out"; printf 'EOF\n'
  printf 'stderr<<EOF\n'; cat "$err"; printf 'EOF\n'
  printf 'helper_log<<EOF\n'
  if [ -s "$log" ]; then cat "$log"; else printf '<empty>\n'; fi
  printf 'EOF\n\n'
}
run_case path-helper-usehttppath \
  "$SCRATCH/bin:/usr/bin:/bin" \
  git \
  -c credential.helper= \
  -c credential.helper=phase15probe \
  -c credential.https://dev.azure.com.useHttpPath=true \
  credential fill
run_case path-helper-no-usehttppath \
  "$SCRATCH/bin:/usr/bin:/bin" \
  git \
  -c credential.helper= \
  -c credential.helper=phase15probe \
  credential fill
run_case path-helper-missing \
  "/usr/bin:/bin" \
  git \
  -c credential.helper= \
  -c credential.helper=phase15probe \
  -c credential.https://dev.azure.com.useHttpPath=true \
  credential fill
run_case absolute-helper-minpath \
  "/usr/bin:/bin" \
  git \
  -c credential.helper= \
  -c credential.helper="$SCRATCH/bin/git-credential-phase15probe" \
  -c credential.https://dev.azure.com.useHttpPath=true \
  credential fill
run_case gui-like-absolute \
  "/usr/bin:/bin" \
  git \
  -c credential.helper= \
  -c credential.helper="$SCRATCH/bin/git-credential-phase15probe" \
  -c credential.https://dev.azure.com.useHttpPath=true \
  credential fill
rm -rf "$SCRATCH"
printf 'cleanup_exists='
if [ -e "$SCRATCH" ]; then printf 'yes\n'; else printf 'no\n'; fi
```

Synthetic Git credential input piped to every case:

```text
protocol=https
host=dev.azure.com
path=org/project/_git/repo
```

### Prototype Cases

| Case                      | Isolation and command shape                                                                                                                                                                                                                                                               | Result                                           |
| ------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------ |
| Helper shorthand on PATH  | `env -i HOME="$SCRATCH/home" GIT_CONFIG_NOSYSTEM=1 GIT_CEILING_DIRECTORIES="$SCRATCH" PATH="$SCRATCH/bin:/usr/bin:/bin" PHASE15_LOG=... git -C "$PROBE_CWD" -c credential.helper= -c credential.helper=phase15probe -c credential.https://dev.azure.com.useHttpPath=true credential fill` | Pass                                             |
| `useHttpPath` omitted     | Same isolated environment, scratch `git -C` working directory, ceiling directory, and blank helper reset, but without `credential.https://dev.azure.com.useHttpPath=true`                                                                                                                 | Pass, but Git omitted `path` from helper stdin   |
| Helper shorthand missing  | Same isolated environment, scratch `git -C` working directory, ceiling directory, and blank helper reset with `PATH="/usr/bin:/bin"`; no other helper config was visible                                                                                                                  | Expected failure with no helper log              |
| Absolute helper           | Same isolated environment, scratch `git -C` working directory, ceiling directory, and blank helper reset with `PATH="/usr/bin:/bin"` and `credential.helper="$SCRATCH/bin/git-credential-phase15probe"`                                                                                   | Pass                                             |
| GUI-like stripped process | Same absolute-helper command under `env -i HOME=... GIT_CONFIG_NOSYSTEM=1 GIT_CEILING_DIRECTORIES=... PATH="/usr/bin:/bin"` and `git -C "$PROBE_CWD"`                                                                                                                                     | Pass as an environment-stripping simulation only |

### Probe Transcript

Observed command results and helper logs:

```text
repo_discovery_check<<EOF
fatal: not a git repository (or any of the parent directories): .git
EOF

case=path-helper-usehttppath
status=0
stdout<<EOF
protocol=https
host=dev.azure.com
path=org/project/_git/repo
username=probe-user
password=probe-password
EOF
stderr<<EOF
EOF
helper_log<<EOF
argv: [get]
cwd: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe/work
HOME: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe/home
GIT_CONFIG_NOSYSTEM: 1
GIT_CEILING_DIRECTORIES: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe
PATH: /usr/lib/git-core:/usr/lib/git-core:/workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe/bin:/usr/bin:/bin
stdin-begin
protocol=https
host=dev.azure.com
path=org/project/_git/repo
stdin-end
---
EOF

case=path-helper-no-usehttppath
status=0
stdout<<EOF
protocol=https
host=dev.azure.com
username=probe-user
password=probe-password
EOF
stderr<<EOF
EOF
helper_log<<EOF
argv: [get]
cwd: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe/work
HOME: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe/home
GIT_CONFIG_NOSYSTEM: 1
GIT_CEILING_DIRECTORIES: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe
PATH: /usr/lib/git-core:/usr/lib/git-core:/workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe/bin:/usr/bin:/bin
stdin-begin
protocol=https
host=dev.azure.com
stdin-end
---
EOF

case=path-helper-missing
status=128
stdout<<EOF
EOF
stderr<<EOF
git: 'credential-phase15probe' is not a git command. See 'git --help'.
fatal: could not read Username for 'https://dev.azure.com/org/project/_git/repo': No such device or address
EOF
helper_log<<EOF
<empty>
EOF

case=absolute-helper-minpath
status=0
stdout<<EOF
protocol=https
host=dev.azure.com
path=org/project/_git/repo
username=probe-user
password=probe-password
EOF
stderr<<EOF
EOF
helper_log<<EOF
argv: [get]
cwd: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe/work
HOME: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe/home
GIT_CONFIG_NOSYSTEM: 1
GIT_CEILING_DIRECTORIES: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe
PATH: /usr/lib/git-core:/usr/bin:/bin
stdin-begin
protocol=https
host=dev.azure.com
path=org/project/_git/repo
stdin-end
---
EOF

case=gui-like-absolute
status=0
stdout<<EOF
protocol=https
host=dev.azure.com
path=org/project/_git/repo
username=probe-user
password=probe-password
EOF
stderr<<EOF
EOF
helper_log<<EOF
argv: [get]
cwd: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe/work
HOME: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe/home
GIT_CONFIG_NOSYSTEM: 1
GIT_CEILING_DIRECTORIES: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.5-git-discovery-probe
PATH: /usr/lib/git-core:/usr/bin:/bin
stdin-begin
protocol=https
host=dev.azure.com
path=org/project/_git/repo
stdin-end
---
EOF

cleanup_exists=no
```

## Findings

1. Git helper shorthand `credential.helper=phase15probe` caused Git to invoke
   `git-credential-phase15probe get` when the helper directory was on the Git
   process `PATH`.
2. The same shorthand failed when the helper directory was absent from the Git
   process `PATH`. This confirms that shell PATH success is not enough evidence
   for GUI-launched Git clients that may inherit a different environment.
3. `credential.https://dev.azure.com.useHttpPath=true` was required for the
   helper to receive `path=org/project/_git/repo` in this probe. Without it, Git
   omitted `path` before invoking the helper.
4. An absolute helper path worked under a minimal environment where helper
   shorthand failed. This supports absolute-path or installer-placed discovery as
   safer than PATH-only configuration.
5. The stripped-environment run is useful for checking that the command does not
   depend on shell startup files, but it does not prove real GUI client behavior.
   No Git for Windows, Windows GUI shell, Visual Studio, VS Code, or other
   GUI-launched Git evidence was available in this environment.

## Supported Installation Mode Implications

- The product should continue to ship a helper-shaped entry point:
  `git-credential-<helper-name>`.
- Default production setup must not rely on shell snippets.
- PATH-only installation is acceptable only for explicitly documented shell
  scenarios that pass doctor checks in the same environment that launches Git.
- Windows and GUI-supported modes require either:
    1. placing the helper where Git for Windows itself discovers
       `git-credential-<helper-name>`, or
    2. configuring a carefully escaped absolute helper path that Git can invoke
       without shell profile initialization.
- The configuration manager must own Git config writes and removal metadata,
  including any `credential.helper` entry and the `dev.azure.com` `useHttpPath`
  entry it installs.
- Doctor must validate discovery by invoking Git with a synthetic credential
  record, not only by checking the current shell's `PATH`.

## Git for Windows and GUI Status

Git for Windows and a real GUI-launched Git client were not available in this
local Linux environment. This record therefore does not pass the full Phase 1.5
mandatory exit criterion. It records a local subset pass and a platform evidence
blocker.

Because the Git for Windows and GUI installation modes remain blocked, dependent
work cannot rely on those modes until the missing evidence closes or Phase 1R
approves supported-mode narrowing or resequencing. Scheduling future evidence is
not evidence and does not unblock dependent work.

Required missing evidence:

1. Git for Windows helper shorthand and absolute-path behavior, including `.exe`
   and `.cmd` path handling and paths with spaces.
2. Git for Windows system/global helper precedence when another helper such as
   `manager` or `manager-core` is already configured.
3. At least one GUI-launched Git scenario, such as Visual Studio, VS Code, Git
   GUI, or another approved desktop client, proving that the selected
   installation mode is visible to the Git process launched by that GUI.
4. Windows doctor behavior that detects and reports PATH-only failures without
   leaking credentials or writing repository-local secrets.

## Validation and Checks

Commands run from repository root:

```bash
git --version
git -C /workspace/public/git-credential-manager --no-pager rev-parse HEAD
git -C /workspace/public/git-credential-manager --no-pager remote -v
git -C /workspace/public/git-credential-manager --no-pager describe --tags --always --dirty
git -C /workspace/public/git-credential-manager --no-pager status --short
```

Results:

```text
git --version: git version 2.43.0
upstream mirror HEAD: 312354b884aca75efb078bedccf033df97fabb1f
upstream mirror remote: https://github.com/git-ecosystem/git-credential-manager.git
upstream mirror description: v2.8.0-7-g312354b
upstream mirror status --short: no output
```

Disposable probe validation:

```text
repository discovery exclusion: `git -C "$PROBE_CWD" rev-parse --show-toplevel` reported not a Git repository with `GIT_CEILING_DIRECTORIES="$SCRATCH"`
helper shorthand on PATH: exit 0, helper log recorded argv [get] from scratch `work` cwd
helper shorthand without useHttpPath: exit 0, helper stdin omitted path
helper shorthand missing from PATH: exit 128, no helper log
absolute helper with minimal PATH: exit 0, helper log recorded argv [get] from scratch `work` cwd
GUI-like stripped absolute-helper run: exit 0, helper log recorded argv [get] from scratch `work` cwd
cleanup check: .copilot-scratch/phase-1.5-git-discovery-probe did not exist after cleanup
```

Markdown validation:

Prettier formatting was run before the final check:

```text
pnpm exec prettier --write src/private/app/azureauth-credprovider/docs/phase-1.5-git-discovery-evidence.md
src/private/app/azureauth-credprovider/docs/phase-1.5-git-discovery-evidence.md 90ms
prettier_write_status=0
```

Final markdown checks then produced:

```text
pnpm exec prettier --check src/private/app/azureauth-credprovider/docs/phase-1.5-git-discovery-evidence.md
Checking formatting...
All matched files use Prettier code style!
prettier_check_status=0

pnpm exec markdownlint-cli2 src/private/app/azureauth-credprovider/docs/phase-1.5-git-discovery-evidence.md
markdownlint-cli2 v0.22.1 (markdownlint v0.40.0)
Finding: src/private/app/azureauth-credprovider/docs/phase-1.5-git-discovery-evidence.md
Linting: 1 file(s)
Summary: 0 error(s)
markdownlint_status=0

if [ -e .copilot-scratch/phase-1.5-git-discovery-probe ]; then \
  echo 'scratch_cleanup=FAILED'; exit 1; \
else \
  echo 'scratch_cleanup=ok'; \
fi
scratch_cleanup=ok
```

## Affected Requirements and Designs

- `requirements.md`: Git requirement 1 is evidenced only for helper discovery
  through the `git-credential-<helper-name>` entry point shape used by Git helper
  shorthand resolution.
- `requirements.md`: Git requirement 4 is evidenced only for preserving Azure
  Repos organization identity through Git credential path handling.
- `requirements.md`: Git requirement 5 is evidenced for
  `credential.https://dev.azure.com.useHttpPath=true` configuration in local
  Linux Git shell probes.
- `requirements.md`: Git requirement 6 is evidenced only for avoiding default
  shell snippets when helper shorthand or an absolute helper command is
  configured.
- `requirements.md`: Git requirement 2 full adapter stdout behavior and Git
  requirement 3 `store` and `erase` handling are not covered by this gate. They
  remain Phase 9 contract validation follow-up work.
- `requirements.md`: Non-functional requirement 3 remains open for Git for
  Windows, `.exe`, `.cmd`, and path-with-spaces evidence.
- `high-level-design.md`: Supported mapping is limited to helper discovery
  shape, `useHttpPath`, and doctor discovery checks that ask Git to invoke a
  fake helper. Full protocol stdout constraints and `store` or `erase` behavior
  remain Phase 9 contract validation follow-up work.
- `mid-level-design.md`: Git adapter discovery doctor checks are supported for
  local shell probing. The `Git GUI client PATH differences` required prototype
  remains open for real Windows and GUI evidence.
- `project-breakdown.md`: Phase 1.5 does not fully close. Dependent work cannot
  rely on Windows or GUI installation modes until missing evidence closes or
  Phase 1R records an explicit PLATFORM/PL supported-mode narrowing or resequence
  decision. Dependent work also cannot treat `store`, `erase`, or full adapter
  stdout behavior as validated by this gate.

## Follow-up Actions

| Owner                    | Follow-up                                                                                                                                       | Dependency effect                                                |
| ------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| PLATFORM                 | Run Git for Windows helper discovery with helper shorthand, absolute helper path, `.exe`, `.cmd`, and path-with-spaces cases.                   | Blocks Windows installation-mode lock.                           |
| PLATFORM and ADAPTER-GIT | Run at least one GUI-launched Git scenario using the selected installation mode and capture Git-invoked helper logs with fake credentials only. | Blocks GUI-supported mode claims.                                |
| CONFIG                   | Design Git change plans so helper entries and `useHttpPath` writes have ownership metadata and safe removal behavior.                           | Required before configure/unconfigure implementation.            |
| ADAPTER-GIT              | Validate the implemented Git adapter contract for stdout-only credential fields and `get`, `store`, and `erase` behavior.                       | Required before Phase 9 acceptance.                              |
| ADAPTER-GIT              | Implement doctor probes that call Git with a synthetic Azure Repos credential record and redact any returned credential fields.                 | Required before Phase 9 acceptance.                              |
| PL and PLATFORM          | Enter Phase 1R if Windows/GUI evidence remains missing and dependent work must proceed before the full gate closes.                             | Required to unblock dependent work if full gate remains blocked. |

## Residual Risks

- Real GUI clients may launch Git with a different `PATH`, different environment
  variables, or a different Git executable than the terminal probe.
- Git for Windows may differ from Linux Git in helper path quoting, `.exe` or
  `.cmd` suffix handling, system/global precedence, and path-with-spaces
  behavior.
- Existing helpers such as `manager` or `manager-core` may interact with helper
  ordering and `useHttpPath` cleanup. Removal must avoid breaking unrelated Git
  Credential Manager installations.
- Absolute helper paths reduce PATH dependence but still require robust quoting,
  installer repair, and doctor checks for moved or removed artifacts.

[gcm-application-config]: https://github.com/git-ecosystem/git-credential-manager/blob/312354b884aca75efb078bedccf033df97fabb1f/src/shared/Core/Application.cs#L182-L230
[gcm-architecture-ui]: https://github.com/git-ecosystem/git-credential-manager/blob/312354b884aca75efb078bedccf033df97fabb1f/docs/architecture.md#L70-L84
[gcm-azrepos-config-tests]: https://github.com/git-ecosystem/git-credential-manager/blob/312354b884aca75efb078bedccf033df97fabb1f/src/shared/Microsoft.AzureRepos.Tests/AzureReposHostProviderTests.cs#L902-L980
[gcm-azrepos-configure]: https://github.com/git-ecosystem/git-credential-manager/blob/312354b884aca75efb078bedccf033df97fabb1f/src/shared/Microsoft.AzureRepos/AzureReposHostProvider.cs#L768-L789
[gcm-azrepos-service]: https://github.com/git-ecosystem/git-credential-manager/blob/312354b884aca75efb078bedccf033df97fabb1f/src/shared/Microsoft.AzureRepos/AzureReposHostProvider.cs#L414-L452
[gcm-azrepos-unconfigure]: https://github.com/git-ecosystem/git-credential-manager/blob/312354b884aca75efb078bedccf033df97fabb1f/src/shared/Microsoft.AzureRepos/AzureReposHostProvider.cs#L791-L812
[gcm-config-helper]: https://github.com/git-ecosystem/git-credential-manager/blob/312354b884aca75efb078bedccf033df97fabb1f/docs/configuration.md#L22-L25
[gcm-path-escaping]: https://github.com/git-ecosystem/git-credential-manager/blob/312354b884aca75efb078bedccf033df97fabb1f/src/shared/Core/Application.cs#L281-L296
[gcm-readme-status]: https://github.com/git-ecosystem/git-credential-manager/blob/312354b884aca75efb078bedccf033df97fabb1f/README.md#L7-L45
