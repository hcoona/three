# Enterprise Translation Team Plugin

This Copilot CLI plugin packages role-based custom agents and one shared skill
for enterprise Chinese-English professional document translation workflows.
GitHub Copilot CLI is the only supported target; VS Code integration is
outside this plugin's scope.

Install locally during development:

```powershell
copilot plugin install .\src\private\lib\enterprise-translation-team
```

For cache refresh after edits, reinstall the local plugin.
To run static package checks through the repository toolchain:

```powershell
mise run translation-agent-plugin-check
```

Prepare a disposable eval workspace without calling Copilot:

```powershell
mise run translation-agent-plugin-eval-dry-run
```

Run a real fixture eval with enforced GPT-5.5 review gates:

```powershell
mise exec -- python `
  .\src\private\lib\enterprise-translation-team\skills\enterprise-translation-team\scripts\run_copilot_evals.py `
  --mode copilot `
  --case mqm-review-json `
  --workspace <scratch>
```

Exercise the APM-deployed `.agents` and `.github` components without loading
the source plugin:

```powershell
mise exec -- python `
  .\src\private\lib\enterprise-translation-team\skills\enterprise-translation-team\scripts\run_copilot_evals.py `
  --mode copilot `
  --apm-root . `
  --case mqm-review-json `
  --workspace <scratch>
```

The plugin does not vendor public benchmark datasets.
See `skills\enterprise-translation-team\references\evaluation.md`
and `skills\enterprise-translation-team\evals\evals.json`
for dataset references, baseline design, and fixture-driven eval cases.

Terminology is JSON-first and TBX-compatible.
The canonical job termbase is `termbase.job.json`;
`termbase.delta.jsonl` records job-local proposals and conflicts;
`termbase.tbx` is the exchange export;
and `terminology-review.tsv` is a lossy review view only.
