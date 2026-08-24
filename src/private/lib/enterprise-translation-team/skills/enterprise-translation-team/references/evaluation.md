# Evaluation Plan

Evaluate this skill before instruction iteration.
Follow the Agent Skills pattern: define prompts and expected outputs,
run each case with and without the skill or against the previous version,
grade assertions with evidence, then iterate only on recurring failures.

## Baselines

Use at least two baselines:

1. No plugin loaded.
2. Current plugin loaded with `--plugin-dir` or local plugin install.
3. APM deployment staged from `.agents/skills` and `.github/agents`,
   loaded through Copilot project discovery without `--plugin-dir`.

For later iterations, snapshot the previous plugin and compare old versus new.

## GPT-5.5 review gates

Every material step must pass two independent GPT-5.5 review gates:

| Gate            | Agent                                                       | Blocking threshold                                                                                                                                               |
| --------------- | ----------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Positive review | `enterprise-translation-team:translation-positive-reviewer` | Blocks if the objective, role boundary, source fidelity, structure, applicable terminology, or eval criterion is not satisfied.                                  |
| Negative review | `enterprise-translation-team:translation-negative-reviewer` | Blocks on mistranslation, omission/addition, non-translation, terminology conflicts, schema incompatibility, unsafe permissions, no-op evals, or false sign-off. |

Use the explicit model flag when invoking review gates from Copilot CLI:

```powershell
copilot -C <scratch> `
  --plugin-dir <plugin-root> `
  --agent enterprise-translation-team:translation-positive-reviewer `
  --model gpt-5.5 -p "<review prompt>" `
  --allow-tool=read --allow-tool=search --silent
copilot -C <scratch> `
  --plugin-dir <plugin-root> `
  --agent enterprise-translation-team:translation-negative-reviewer `
  --model gpt-5.5 -p "<review prompt>" `
  --allow-tool=read --allow-tool=search --silent
```

Proceed only when both invocations succeed
and each returns exactly one standalone `PASS`.
Treat `BLOCK`, missing output,
and malformed or ambiguous verdicts as gate failures.
After fixing a failure, rerun both gates on the revised step.

## Public dataset registry

The plugin does not vendor public datasets.
Use these as external benchmark sources
when a real eval run needs standard data:

| Dataset                   | Best use                                              | Notes                                                                             |
| ------------------------- | ----------------------------------------------------- | --------------------------------------------------------------------------------- |
| FLORES-200                | Sentence-level zh-Hans/en translation sanity checks.  | Professionally translated benchmark; useful for direction and leakage checks.     |
| WMT MQM Human Evaluation  | Bilingual review and MQM category/severity behavior.  | Includes Chinese-to-English WMT outputs with professional translator annotations. |
| WMT Terminology Task 2023 | Glossary adherence and terminology constraints.       | Includes zh-en terminology hints and target terms.                                |
| MLQE-PE                   | Post-editing and quality-estimation behavior.         | Useful for edit minimality and error resolution checks.                           |
| ACES                      | Adversarial accuracy and contrastive error detection. | Useful for reviewer negative cases.                                               |
| BWB/BlonDe                | Document-level Chinese-English consistency.           | Useful for entity/coreference consistency across segments.                        |

Do not copy dataset text into this repository by default.
Download to a temporary eval workspace only after checking the dataset license
and the user's authorization.

## Local fixture evals

`evals/evals.json` contains small synthetic fixtures for fast package checks.
They are not a replacement for public dataset runs;
they prove that the skill has stable output contracts
before expensive `copilot -p` evaluation.

## Copilot CLI run shape

Use a disposable workspace outside canonical `raw/` and `wiki/` content.

```powershell
copilot -C <scratch> `
  --plugin-dir <plugin-root> `
  -p "<eval prompt>" `
  --allow-tool=read --allow-tool=write --allow-tool=edit --allow-tool=search `
  --deny-tool=execute --deny-tool=shell --silent
```

When selecting a custom agent directly:

```powershell
copilot -C <scratch> `
  --plugin-dir <plugin-root> `
  --agent enterprise-translation-team:translation-workflow-lead `
  -p "<eval prompt>" `
  --allow-tool=read --allow-tool=write --allow-tool=edit --allow-tool=search `
  --deny-tool=execute --deny-tool=shell --silent
```

Record prompt, plugin version, model, output directory, duration,
and pass/fail evidence.
The helper loads the current plugin through an explicit `--plugin-dir`.
Before a `no-plugin` generation run,
it aborts if Copilot can discover this plugin or skill from any other scope.
Use a clean scratch directory when unrelated personal or repository components
must also be excluded.

From the `enterprise-translation-team` skill directory,
the packaged helper can prepare or run local fixture evals:

```powershell
mise exec -- python scripts\run_copilot_evals.py --mode dry-run
mise exec -- python scripts\run_copilot_evals.py `
  --mode copilot --case mqm-review-json --workspace <scratch>
mise exec -- python scripts\run_copilot_evals.py `
  --mode copilot --baseline no-plugin `
  --case mqm-review-json --workspace <scratch>
```

An intact source or packed plugin root is auto-detected.
To exercise the exact APM-deployed components, pass the repository root:

```powershell
mise exec -- python scripts\run_copilot_evals.py `
  --mode copilot --apm-root <repository-root> `
  --case mqm-review-json --workspace <scratch>
```

The helper copies only the deployed target skill and seven repository agents
into an isolated project under the disposable workspace.
It verifies Copilot discovers the skill from that project path,
does not pass `--plugin-dir`,
uses unnamespaced repository-agent identifiers,
and keeps no-plugin generation in a separate project.
Both baseline outputs are still graded by the staged reviewer agents.

In `copilot` mode, the helper defaults to enforcing both GPT-5.5 review gates
for both baseline outputs.
The `no-plugin` baseline omits the plugin only while generating the output;
the plugin's reviewers still grade that output afterward.
Review gates still run when deterministic grading fails,
so their independent assessment remains available with the grading evidence.
Use `--skip-review-gates` only when debugging harness mechanics,
not when accepting a skill iteration.

Deterministic grading verifies the exact pending-sign-off sentence and other
machine-readable output contracts. It does not infer approval semantics from
free-form prose; the negative review gate owns false-sign-off detection.

The final-QA fixture intentionally contains one TBX mismatch and one TSV
mismatch against canonical JSON, in addition to forbidden terminology,
open conflicts, and one open Major MQM issue.

In this repository, prefer the task for dry runs:

```powershell
mise run translation-agent-plugin-eval-dry-run
```
