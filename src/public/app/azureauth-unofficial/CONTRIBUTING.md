# Contributing to AzureAuth Unofficial V2

**AzureAuth Unofficial V2** is an unofficial, pre-release fork. Contributions must remain
within the current project boundary and work authorization.

## Before Starting

1. Read the [project record index](docs/README.md).
2. Determine authorization from the target branch's accepted
   [`docs/delivery-wave.md`](docs/delivery-wave.md), normally the copy on `main-v2`.
3. Confirm that one current entry authorizes the bounded advancement and that its
   accepted inputs, exclusions, and external-effects boundary fit the work.
4. Treat a working-branch change to that file as a proposal that cannot authorize
   additional work before merge.
5. Read the applicable Issue when work needs separate proposal, dependency, or progress
   coordination. Otherwise, use the pull request as the bounded work carrier.
6. Read the canonical product, architecture, research, security, and validation records
   relevant to the change.

Do not add private company information, credentials, tokens, private account details, or
unpublished downstream evidence.

## Work Carriers and Concurrency

Use a separate Issue when work spans multiple pull requests or contributors, coordinates
dependencies or progress, or benefits from separate proposal discussion. A bounded
single-PR change may use the pull request itself as its work carrier when its accepted
Wave entry already contains sufficient scope.

An Issue, Milestone, branch, pull request, comment, label, or unmerged Wave edit does not
grant or enlarge work authorization.

An explicitly repository-owner-approved pull request limited to adding, changing,
deleting, or replacing Delivery Wave entries may be prepared and reviewed without an
existing entry. Newly proposed work begins only after that change merges.

Independent authorized work may proceed concurrently. Work remains ordered when it
depends on an unaccepted result or changes the same canonical authority under conflicting
assumptions. Implementation requires accepted Slice requirements, applicable
architecture and contracts, a validation basis, explicit unsupported cases, and every
applicable domain precondition. Another authorized Slice may concurrently remain in
requirements or architecture work. If the target branch materially changes a relied-on
prerequisite or shared canonical authority, pause dependent work and refresh its gate
evidence, validation, and review before merge.

[Development validation](docs/research/experiment-safety.md#development-validation) is not
an experiment. Before an experiment runs, its accepted Wave entry must bound the outcome and maximum
effects and its accepted protocol must define environment, account-state and effects
boundaries, observations, finite attempt/time limits, stop conditions, and cleanup or
intentional retention. Owner-operated steps and machine switches use the same protocol
and cumulative limits. Repeated executions inside those bounds
do not require another owner approval. A material effects boundary requires the owner
risk decision defined by the experiment policy.

## Local Checks

`mise.toml` and `mise.lock` define one locked toolchain for checks and development.
Repository checks need only the check tools, so install those and the hooks without
downloading the .NET SDK, APM, or Node.js:

```sh
mise install --locked actionlint gitleaks hk lychee pkl python shellcheck uv
mise run hooks:install
```

`mise install --locked` without tool names also installs the development tools below.
`mise.lock` locks each check tool for the platforms listed in its entries; CI runs the
checks on Linux x64 only. The pinned hk release has no Intel macOS binary; use CI or
another host on Intel macOS.

The pre-commit hook runs the local fast set through hk. Run the complete repository
check set, which CI's Repository Controls job runs, before requesting review:

```sh
mise run check
```

By default, mise installs a missing tool automatically when `mise exec` or a task
needs it. The repository job in `.github/workflows/check.yml` turns off both
automatic installs
([`exec_auto_install`](https://mise.jdx.dev/configuration/settings.html#exec_auto_install)
and
[`task.run_auto_install`](https://mise.jdx.dev/configuration/settings.html#task.run_auto_install)),
so a check run never downloads the development SDK, APM, or Node.js. To match CI, set
the same variables for local check runs; mise then leaves a missing tool uninstalled
instead of downloading it:

```sh
export MISE_EXEC_AUTO_INSTALL=false
export MISE_TASK_RUN_AUTO_INSTALL=false
```

The hook fails closed while any unstaged or untracked path remains. Stage the final
intended snapshot or temporarily set other work aside before committing so repository-wide
path-based checks inspect the same tree Git will record.

The record checker's Python dependencies, including `check-jsonschema` for
structured-record schema validation, are locked in
`tools/check_repository_records.py.lock`, and hk runs it with `uv run --locked`. After a
reviewed change to its inline dependency list, regenerate that lock with
`mise exec -- uv lock --script tools/check_repository_records.py`.

CI also runs the scenario and Windows jobs described in the
[validation strategy](docs/validation/strategy.md#continuous-integration-and-host-timing-evidence).
The Windows scenarios keep their files under `TEMP` and refuse to reuse a scenario
directory, so run them with `TEMP` and `TMP` set to a new empty directory, and dispose
of that directory afterward.
With the .NET SDK below installed, these commands reproduce the Linux scenario check:

```text
mise exec -- dotnet restore Linux.slnx --locked-mode
mise exec -- dotnet build Linux.slnx --configuration Release --no-restore
mise exec -- dotnet tests/Authentication.Scenarios/bin/Release/net10.0/Authentication.Scenarios.dll
```

These reproduce the Windows build check on a Linux host:

```text
mise exec -- dotnet restore Windows.slnx --locked-mode -p:EnableWindowsTargeting=true
mise exec -- dotnet build Windows.slnx --configuration Release --no-restore -p:EnableWindowsTargeting=true
```

Builds compute the product version with Nerdbank.GitVersioning from `version.json` and
the Git history, so build from a full clone. A shallow clone fails the build, and a tree
without `.git`, such as a `git archive` copy, does not build. The Windows portable
package is built on Windows x64, from a clean full clone with `main-v2` checked out, with
the Visual Studio C++ tools that Native AOT needs. On another branch, add
`-AllowNonPublicRelease` for development validation only:

```text
pwsh packaging/windows/package.ps1
```

The [Windows design](docs/designs/windows-ado-authentication.md#portable-package-and-version)
owns the package content and checks. Prereleases are built only by the
[release workflow](docs/designs/windows-ado-authentication.md#release-workflow), started by
hand from `main-v2`; the repository owner publishes each draft after its release review.

## .NET and Agent Tooling

The same toolchain selects .NET SDK 10.0.403 (`http:dotnet-development`), APM v0.29.0,
and Node.js 22.22.2. Root `global.json` requires that SDK without roll-forward. It is
generated from the `mise.lock` SDK entry by `global.pkl` and is never edited by hand;
the `global-json` hk step fails when it differs. Every project targets the runtime
bundled with that SDK (`TargetLatestRuntimePatch`) with roll-forward disabled, so no
project states a runtime patch version. `src/Directory.Build.props` holds the shared
build properties, and `tests/Directory.Build.props` imports it. Central package
management waits for a concrete product consumer.

`mise.lock` is generator output: never edit it by hand. Generate it with the mise
version that CI pins, the `MISE_VERSION` value in `.github/workflows/check.yml`;
`mise.toml` sets the same `min_version`, and `.github/workflows/release.yml` uses the same
`MISE_VERSION`. Another mise
version can rewrite unrelated entries.

To change the SDK, edit its version and each platform's archive URL and `checksum` in
`mise.toml`, taking the checksums from the SDK's published release checksums. With that
mise version, regenerate the lock and `global.json`, then refresh the package lock files so
they follow the new runtime:

```text
mise lock http:dotnet-development --platform linux-x64,windows-x64
mise exec -- pkl eval -f json global.pkl -o global.json
mise exec -- dotnet restore Windows.slnx --force-evaluate -p:EnableWindowsTargeting=true
```

Keep the SDK lock to the two platforms whose archives `mise.toml` names; without
`--platform`, mise also adds entries for other locked platforms, such as a glibc archive
under `linux-x64-musl`. `.gitattributes` normalizes every `packages.lock.json` to LF line
endings, so a lock regenerated on either host differs only in its package selections.

Together with [Local Checks](#local-checks), this section is the single authority for
contributor tooling setup. These development tools have Linux x64 and Windows x64 lock
entries. The completed
[developer-tooling evidence](docs/research/experiments/developer-tooling.md) records the
WSL Linux installation, discovery, and retrieval observations behind it; Windows runtime
discovery is not established.

Install from public sources without ambient credentials: use an empty process-local
GitHub CLI configuration directory and npm user configuration, disable Git credential
helpers and prompts through process-only Git configuration, omit token variables, and set
`NETRC` to an empty file. Before invoking APM, confirm its manager configuration is absent
or holds only `default_client`, `install_target`, and `auto_integrate`. Keep APM's
built-in checks enabled. An inherited registry, policy, credential, or hook configuration
is a reason to stop rather than to work around it. From the repository root, these
command forms work in PowerShell and a POSIX shell:

```text
mise install --locked http:dotnet-development github:microsoft/apm node
mise exec -- npm ci --prefix tools/learn-cli --ignore-scripts --no-audit --no-fund
mise exec -- apm install --frozen --target copilot,codex --no-trust-bin --https
mise exec -- dotnet --version
```

Use public package sources and preserve existing instruction/configuration entries.
Do not invoke an authentication fallback or override a conflicting installation. For
.NET commands, set `DOTNET_CLI_TELEMETRY_OPTOUT=1`.

`apm.yml` selects exactly `directory-build-organization`, `property-patterns`,
`msbuild-antipatterns`, `microsoft-docs`, and `microsoft-code-reference`. APM owns their
shared `.agents/skills/` deployment and the named `microsoft-learn` entries in
`.github/mcp.json` and `.codex/config.toml`. Both clients consume that shared Skill
location. The two hand-maintained review Skills remain in `.github/skills/`.

Commit `apm.lock.yaml`, `mise.lock`, and `tools/learn-cli/package-lock.json`.
Generated dependency directories and client projections remain ignored. Preserve
`AGENTS.md`, review Skills, and the ignored private `AGENTS.local.md`. Never run
`apm compile` over the hand-maintained instructions, duplicate the selected Skills with a
global installer, or use `mslearn setup` to create another installation owner.

Microsoft's official Skill content owns its usage guidance. The repository adds no tool
preference or invocation order. Upstream examples may use floating `npx` references;
those examples are not this repository's locked installation. The locked official CLI
is available as:

```text
mise exec -- node tools/learn-cli/node_modules/@microsoft/learn-cli/dist/index.js --help
```

[UPSTREAM.md](UPSTREAM.md) identifies sources and license notices.

### Explicit Updates and Removal

An update is a reviewed change to the direct version/ref declarations and their generated
locks under current work authorization. Regenerate only the `mise.lock` entries for
`http:dotnet-development`, `github:microsoft/apm`, and `node`, on `linux-x64,windows-x64`,
and leave the check-tool entries unchanged.
Use the exact HTTP SDK tool rather than the generic `dotnet` alias, whose lock does not
pin SDK archive checksums. Resolve the Learn package lock with
`--package-lock-only --ignore-scripts --no-audit --no-fund`, review its graph, then use
`npm ci` with the same restrictions. Resolve APM with the same explicit targets,
`--no-trust-bin`, and `--https`, then verify a frozen replay and content preservation.
Review provenance, licenses, Skill support files, generated targets, and any new effects
before accepting a changed lock.

To remove this setup, first inventory its owned paths and compare their content with the
accepted lock/source. Remove only its five Skill directories, its APM dependency data,
and the Learn `node_modules` directory; remove only the `microsoft-learn` entry from each
client configuration. Preserve unrelated servers, settings, Skills, and instructions.
Remove an empty generated config only if this setup originally created it and no other
content remains. Do not use `apm uninstall --dry-run`: upstream documents that it can run
pre-uninstall scripts. mise-managed tools and public caches are intentionally retained
and may be shared by other projects; repository removal does not authorize deleting
them. Removing the committed manifests/locks is a separate reviewed repository change.

## Pull Requests

A pull request should:

- state the repository outcome if merged;
- link the accepted Delivery Wave entry, any applicable Issue, and governing records;
- define its scope and material non-goals;
- identify any record-system impact;
- distinguish evidence from inference and decision;
- provide the smallest validation that supports the claimed change;
- describe material security or external effects.

Not every change needs an Issue, decision record, design record, or documentation update.
Use them only when their repository policies or the work's coordination needs require
them.

## Review Routing

Before merge:

1. Consult [`docs/governance/controls.yaml`](docs/governance/controls.yaml) and run or
   request every control required by the change and its execution point.
2. Evaluate every fired entry in
   [`docs/research/rechecks.yaml`](docs/research/rechecks.yaml), even when no research file
   changed before the trigger.
3. For governance amendments, use the accepted target-branch versions of `AGENTS.md`, the
   governance policies, `controls.yaml`, and the applicable review Skill as the review
   authority. Treat proposed versions as review subjects until merge.
4. Send each material review finding to a reviewer independent of the originating
   review, change author, and implementation agent for triage before remediation or
   dismissal.
5. Obtain repository-owner disposition when an applicable control, unresolved finding,
   or owner-decision finding requires it.
6. Record the review, triage, recheck, and owner-disposition evidence in the pull request.

## Commits

Use Conventional Commits. Keep each commit focused on one human-reviewable semantic
concern and include a nonempty body and final footer block. CI reports a non-merge commit
that misses this shape as an advisory warning.

## Upstream Source

Follow [`UPSTREAM.md`](UPSTREAM.md) when copying or substantially deriving upstream
source. Preserve required copyright and license notices and record the exact source
commit.

## License

By contributing, you agree that your contribution is provided under the MIT License in
[`LICENSE.txt`](LICENSE.txt).
