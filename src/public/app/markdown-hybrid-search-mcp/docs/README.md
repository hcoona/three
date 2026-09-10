# Markdown Hybrid Search Records

The [design](architecture/overview.md) describes the proposed v1 server.
The [open design questions](research/design-questions.md) preserve the remaining
review concerns and their provenance. `src/markdown_hybrid_search_mcp/server.py`
is still a placeholder; neither document is runtime verification or a release
claim. Project authors maintain the design and question synthesis for future
implementers, the owner, and domain reviewers.

Generic work authorization and review disposition follow the
[repository record system](../../../../../docs/governance/record-system.md) and
[Delivery Wave](../../../../../docs/delivery-wave.md). Product decisions and
preimplementation questions must be resolved within that authorization.

## Retained Source Inputs

These existing files retain their paths and bytes because they record supplied
answers, revisions to those answers, or corpus observations needed to interpret
the design. They are source evidence, not an additional current process policy.
Some were normalized in place before this migration; their own provenance notes
remain intact. The filenames' version ordering does not turn unanswered
questions or estimated defaults into confirmed decisions.

- [Initial clarifications and corpus sample](../.AGENTS/DESIGN/CLARIFY_v1_0.md):
  sample method, exact figures, source corpus paths, and initial scope.
- [v1.0 answers](../.AGENTS/DESIGN/CLARIFY_REVIEW_v1_0.md): required rewrite,
  backend/discovery decisions, heuristic large-document counts, and the
  correction excluding answer synthesis.
- [v1.1 answers](../.AGENTS/DESIGN/CLARIFY_REVIEW_v1_1.md): retrieval, token
  budget, output defaults, and the original questions answered by them.
- [v1.2 answers](../.AGENTS/DESIGN/CLARIFY_REVIEW_v1_2.md): real-path boundaries,
  ignore delegation, rewrite failures, extension availability, and dimensions.
- [v1.3 answers and estimates](../.AGENTS/DESIGN/CLARIFY_REVIEW_v1_3.md):
  ephemeral ownership, snapshot content, discovery/language scope, and explicitly
  unconfirmed rewrite defaults.
- [v1.4 answers](../.AGENTS/DESIGN/CLARIFY_REVIEW_v1_4.md): `.md` discovery,
  developer tokens, and no corpus cap.
- [v1.5 answers](../.AGENTS/DESIGN/CLARIFY_REVIEW_v1_5.md): corpus, runtime,
  authentication, data handling, and timestamp recommendations.
- [v1.6 answers](../.AGENTS/DESIGN/CLARIFY_REVIEW_v1_6.md): Azure API references,
  structured-output capability, DuckDB minimum, decoding, roots, and filters.
- [v1.7 answers](../.AGENTS/DESIGN/CLARIFY_REVIEW_v1_7.md): ANN oversampling,
  strict schema, timestamps, platform and normalization choices, and the
  reported DuckDB documentation observation.

The synthesis links the replaced reviews to their immutable Git versions. It
does not repeat the source answers as a second manually maintained requirements
set or claim that historical review findings have passed independent triage.
