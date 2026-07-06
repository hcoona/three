# Agents Team

## Organization

<!-- Draw the organization of the agents team here, including the different types of agents and their relationships. -->
```text
User-invokable entry points
───────────────────────────────────────────────────────────────────────
  code-review-fix-loop-orchestrator          code-fix-orchestrator
  (Claude Sonnet 4.6)                        (Claude Sonnet 4.6)
  [Review-Fix loop, max 25 iterations]       [Fix-Review loop, max 25 iterations]
           │                                          │
     ┌─────┴──────┐                            ┌─────┴──────┐
     │ Review     │ Fix                        │ Fix        │ Review
     ▼            ▼                            ▼               ▼
  code-review-    code-fix-              code-fix-openai   code-review-claude
  orchestrator    orchestrator           (GPT-5.4)         (Claude Opus 4.6)
  (see below)     (see right ──────────►)
                   column)
───────────────────────────────────────────────────────────────────────
  code-review-orchestrator
  (Claude Sonnet 4.6)
  [Multi-model parallel review + re-review]
           │
           │  [spawn per review dimension, in parallel]
           ├──────────────────────────────────────┐
           │                                      │
     ┌─────┴──────────────────┐                   │
     │   3 reviewers          │                   │ [re-review after all done]
     ▼   (run in parallel)    │                   ▼
  code-review-claude          │          review-code-review-claude
  (Claude Opus 4.6)  ─────────┤          (Claude Sonnet 4.6)
  code-review-gemini          │
  (Gemini 3.1 Pro)   ─────────┤
  code-review-openai          │
  (GPT-5.4)          ─────────┘
───────────────────────────────────────────────────────────────────────

Legend
  ──►  spawns sub-agent via runSubagent
  [user-invokable: false]  code-review-claude, code-review-gemini,
                           code-review-openai, review-code-review-claude
```
