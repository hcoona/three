# Final Perspective Map

## Final adjudication

The final count is **20 first-level perspectives**. The map is **complete and parsimonious** for this offline WinUI save editor: no perspective is added, merged, split, or removed.

The final structure retains every independent durable decision authority while sharpening five labels and their boundaries:

- #3 becomes **Ecosystem legitimacy and social license**.
- #6 becomes **Save-system representation and runtime truth**.
- #8 becomes **Product outcome definition and learning interpretation**.
- #17 becomes **Release identity and delivery-lifecycle contract**.
- #18 becomes **Supported-life resilience and operational-risk policy**.
- #20 is clarified to own requirements and authority for decisions, continuity, and reconstructability—not records administration.

The groups below are navigation only. They carry no decision authority and do not imply teams, phases, gates, or five additional perspectives.

## A. Direction and legitimacy

### 1. Product strategy and positioning

**Dominant question:** What product promise, audience, strategic scope, differentiation, and value priority should be pursued relative to alternatives?

**Boundary:** Owns the strategic bet, product scope, value ordering, positioning, substitutes, and the strategic choice of acquisition or discovery-channel assumptions. It does not establish whether a user problem is real (#2), define the social-license conditions for a framing or channel (#3), interpret realized outcomes (#8), or decide lifecycle affordability (#19). It supplies the value and scope priorities used in integrated planning but does not administer a backlog or schedule.

### 2. User problems, demand, and segmentation

**Dominant question:** Which people have which problems, in what contexts, with what severity, frequency, and willingness to adopt?

**Boundary:** Owns empirical problem understanding, segment distinctions, workflows, unmet needs, adoption barriers, and demand signals. It does not select the strategic bet (#1), design the interaction (#9), or interpret whether shipped behavior achieved the intended outcome (#8). User research is a method serving this perspective, not another perspective.

### 3. Ecosystem legitimacy and social license

**Dominant question:** What legitimate expectations, community norms, disclosure commitments, and trust conditions must the product satisfy to earn and retain social license?

**Boundary:** Owns the normative trust conditions attached to “repair/editor” framing, mod and player-community norms, creator and maintainer expectations, and participation in community-facing discovery contexts. It does not choose the product proposition or acquisition channel (#1), manage stakeholder relationships or communications, determine legal permission (#15), implement distribution (#17), or write interface copy (#9).

For the #1/#3 overlap: **#1 selects** a framing or channel as part of the strategic bet; **#3 constrains whether and how that choice can be socially credible**. A strategically attractive route may therefore be rejected for lack of social license. #17 separately governs the technical delivery contract for an approved distribution channel.

### 4. Responsible-use and content policy

**Dominant question:** Even when a capability is feasible and lawful, what should the product permit, constrain, disclose, or refuse?

**Boundary:** Owns normative use boundaries, fair-play and achievement implications, spoiler and mature-content treatment, misuse-resistant defaults, and claims about downstream effects. It does not own hostile-input controls (#13), formal rights (#15), or the clarity with which policy is presented (#9).

## B. Domain truth and support promises

### 5. Game-domain semantics

**Dominant question:** What does game state mean, and which semantic invariants, authorities, couplings, and transitions must hold?

**Boundary:** Owns the canonical concept model, player-facing vocabulary, authoritative-versus-derived propositions, semantic invariants, transition preconditions, intended effects, and allowed blast radius. It does not decide where a concept is represented for a particular fingerprint (#6), which environments are promised support (#7), or whether a mutation protocol is safe (#12).

### 6. Save-system representation and runtime truth

**Dominant question:** Which fingerprint-specific claims about representation, transformation, persistence, and local environment behavior are accepted as runtime truth?

**Boundary:** Owns accepted empirical claims about serialization, object graphs, engine and plugin hooks, recomputation, write-back, path resolution, and observed local effects of cloud-sync clients. It does not define game meaning (#5), publish the support promise (#7), or authorize writes (#12). Research spikes, fixture creation, runtime tracing, and reverse-engineering execution are subordinate methods; #16 determines whether their evidence is sufficient.

### 7. Support contract and compatibility lifecycle

**Dominant question:** Which named operations are promised for which environment fingerprints, at what support state, and until what end condition?

**Boundary:** Owns the operation-by-environment matrix and its supported, read-only, experimental, refused, deprecated, and retired states, including requalification triggers after game, plugin, runtime, language, mod, or layout changes. It does not discover runtime truth (#6), package the editor (#17), conduct supported-life work (#18), or price the promise (#19). Recognition never by itself authorizes mutation, and archived never means supported.

## C. Human outcomes and access

### 8. Product outcome definition and learning interpretation

**Dominant question:** What intended outcomes and counter-signals matter, and how should observations change product decisions under uncertainty?

**Boundary:** Owns outcome definitions, signal meaning, interpretation thresholds, counter-signals, and decision implications. It does not choose the original strategic proposition (#1), execute research or telemetry, relax privacy constraints (#14), or set cross-cutting evidence-sufficiency rules (#16). For this offline product, valid learning may be telemetry-free, local, opt-in, qualitative, or aggregate.

### 9. Experience, comprehension, and visual communication

**Dominant question:** How should interaction and communication make intended tasks understandable, effective, and recoverable?

**Boundary:** Owns task flows, information hierarchy, previews and confirmations, terminology in use, visual communication, cognitive load, status and error comprehension, and understandable recovery. It does not decide what content is permissible (#4) or what adaptations are required for inclusive reach (#10).

### 10. Inclusive and adaptive access

**Dominant question:** What adaptations are required so people with differing abilities, languages, devices, and use conditions can reach and operate the product?

**Boundary:** Owns accessibility, assistive-technology support, editor localization, scaling, reduced motion, keyboard operation, and context-sensitive presentation alternatives. It does not own general task clarity (#9) or compatibility with localized game data (#7).

The #9/#10 distinction is consequential rather than craft-based: #9 asks whether an intended user can understand and complete the task; #10 asks who is excluded without an adaptation. Usability evidence cannot substitute for accessibility, localization, or assistive-technology evidence.

## D. Engineering, harm containment, rights, and confidence

### 11. System architecture and engineering viability

**Dominant question:** What system shape, dependencies, deployment model, and bounded operating envelope can feasibly sustain the intended product?

**Boundary:** Owns architecture, module and dependency boundaries, Windows/WinUI feasibility, concurrency and cancellation, failure containment, normal-workload resource budgets, and modifiability. **Normal save inspection, editing, validation, backup, and restore must require no account, external service, or network connection.** Optional network access may not become a hidden runtime dependency. This perspective does not own save mutation safety (#12), adversarial controls (#13), release identity (#17), or evidence sufficiency (#16).

### 12. Save-data safety and recoverability

**Dominant question:** Under what conditions and transactional protocol may a user artifact be mutated, refused, restored, or rolled back?

**Boundary:** Owns source identification, ambiguity refusal, mutation planning, conflict detection, pre/post-validation, bounded and tested replacement semantics, backup provenance, interruption behavior, retry safety, and verified restore. It consumes semantic and runtime truth (#5/#6) but does not create them; it does not govern data retention (#14) or replace hostile-input analysis (#13).

### 13. Application security and abuse resistance

**Dominant question:** What trust boundaries and controls are required to contain hostile artifacts, dependencies, environments, and abuse?

**Boundary:** Owns the application threat model, least privilege, bounded parsing and work, safe rendering and path handling, dependency/update compromise controls, and containment of malformed or crafted input. It does not decide responsible product use (#4), authorized data purpose and lifecycle (#14), or ordinary save consistency and recovery (#12).

### 14. Privacy and local-data stewardship

**Dominant question:** Which data may be observed, copied, retained, transmitted, disclosed, or deleted, for what purpose and duration?

**Boundary:** Owns the data inventory and flows for saves, paths, backups, temporary files, logs, dumps, screenshots, and support bundles, together with minimization, notice, choice, retention, deletion, and redaction. **Save contents and all save-derived data remain local by default; normal use has no telemetry requirement, and any intentional export or transmission requires explicit, scoped user action.** Recovery needs (#12) and diagnostic usefulness (#18) cannot silently expand collection or retention.

### 15. Legal and platform-rights constraints

**Dominant question:** What do law, licenses, contracts, platform rules, and third-party rights permit, require, or prohibit?

**Boundary:** Owns reverse-engineering boundaries, game and platform terms, fixture rights, dependency licenses, trademarks, notices, and distribution obligations. **Extracted proprietary game art, audio, fonts, and other presentation assets are excluded from the editor, its package, repository, documentation, and promotion; only original, licensed, or otherwise demonstrably permitted material may be used.** This perspective determines formal permission, not social license (#3), responsible choice among lawful options (#4), or delivery implementation (#17).

### 16. Claim substantiation and assurance confidence

**Dominant question:** Which claims may be made, and what evidence strength, independence, traceability, freshness, and release linkage justify them?

**Boundary:** Owns the proportional evidence standard, claim-to-hazard/control/evidence traceability, challenge and independence requirements, residual-uncertainty statement, evidence expiry, and invalidation triggers. It does not design or operate controls, run QA as a workstream, define product outcomes (#8), or accept residual risk (#20).

#16 remains first-level even though assurance applies across the map: it has independent authority to reject an inadequately substantiated claim, distinct evidence standards, and the distinct failure of shipping or communicating confidence that cannot be reproduced or linked to the released artifact.

## E. Delivery, supported life, and continuity

### 17. Release identity and delivery-lifecycle contract

**Dominant question:** What requirements and acceptance criteria must an authenticated release satisfy across approved channels from artifact creation through install, update, repair, rollback, and removal?

**Boundary:** Owns the contract for artifact identity, authenticity, provenance, reproducibility, channel equivalence, signing continuity, installation state, updates, rollback, repair, downgrade, and uninstall behavior. It does not execute builds, signing, packaging, or promotion; those are release-engineering workstreams. **Connectivity used for optional acquisition or updates is separate from offline runtime operation**, and an offline acquisition/install path is a channel requirement where promised. It does not define semantic compatibility (#7), supported-life response (#18), or exception authority (#20).

### 18. Supported-life resilience and operational-risk policy

**Dominant question:** What health, readiness, response, recovery, communication, maintenance, and sunset obligations keep the supported product resilient?

**Boundary:** Owns privacy-bounded diagnostic requirements, user-report and advisory intake policy, game-update response thresholds, release-stop and recovery criteria, vulnerability and dependency response policy, maintenance expectations, user communication obligations, and orderly sunset requirements. It does not perform support, monitoring, maintenance, incident command, or retirement work and does not imply always-on telemetry or a live-service operations organization. #7 changes support state, #17 supplies an authentic delivery response, #14 constrains diagnostics, and #20 identifies who may stop or approve.

### 19. Economic sustainability

**Dominant question:** Can the product's commitments and assurance burden be funded and staffed over their declared lifetime?

**Boundary:** Owns the lifecycle cost and capacity envelope, support and maintenance load, contingency, signing and supplier exposure, and economic thresholds for starting, continuing, narrowing, pausing, or retiring commitments. It does not choose strategic value (#1), waive safety or evidence standards, execute delivery/support (#17/#18), or assign authority (#20).

### 20. Decision rights, risk ownership, and institutional continuity

**Dominant question:** Who may commit, prioritize across perspectives, decide, stop, change scope, approve an exception, and accept residual risk, and how will that authority survive maintainer change?

**Boundary:** Owns decision, delegation, escalation, veto, release-stop, commitment-change, exception, and risk-acceptance rights; accountable ownership; maintainer succession; access/key handoff; and requirements for reconstructable rationale and authoritative records. It does not choose product value (#1), determine the resource envelope (#19), manufacture evidence (#16), administer records, maintain schedules or backlogs, or perform engineering and operational work.

For a small offline utility, the minimum realization is named maintainer and backup authority, explicit release-stop and exception rights, signing/account handoff, and a reconstructable support/retirement record—not an enterprise governance organization.

## Structural decisions and dissent resolution

| Proposal or concern | Decision | Authority, evidence, and consequence test |
|---|---|---|
| Merge #9 and #10 | **Reject; keep both.** | #9 authorizes interaction and communication choices and relies on comprehension/task evidence. #10 authorizes required adaptations and relies on accessibility, assistive-technology, locale, scaling, and context evidence. Confusion and exclusion are independently possible and require different corrections. |
| Merge #5 and #6 | **Reject; keep both.** | #5 authorizes canonical semantic propositions and invariants; #6 authorizes fingerprint-specific representation/runtime claims. Semantic models and domain contracts are not substitutes for controlled runtime traces and fixtures. A correctly located field can represent the wrong meaning, while correct semantics can be implemented against a brittle or obsolete location. |
| Merge #1 and #8 | **Reject; keep both.** | #1 authorizes the ex-ante strategic bet and scope; #8 authorizes the meaning and decision implications of observed outcomes. Strategy/alternatives evidence and outcome/counter-signal interpretation have different standards. A coherent bet may fail without detection, and good observations do not themselves define the product promise. |
| Remove #16 into cross-cutting assurance | **Reject; keep #16.** | Cross-cutting reach does not erase independent authority over what counts as sufficient, current, traceable evidence. Removing it would leave no owner able to reject unsupported compatibility, safety, privacy, or release claims. QA execution, tests, and evidence production remain below first level. |
| Merge #3 into #1 | **Reject; keep both.** | #1 authorizes strategic positioning and channel selection; #3 authorizes the social-license conditions those choices must satisfy. Market and alternative evidence cannot replace evidence of community norms, disclosure expectations, or creator/maintainer trust. A strategically sound product can still be rejected or stigmatized. |
| Add integrated delivery/program perspective | **Reject; route the decisions explicitly.** | Integrated delivery has no independent subject-matter truth or evidence standard. #1 owns value/scope priority; #7 and #17 own support and release commitments; every affected perspective supplies constraints and dependencies; #19 owns capacity and economic viability; #16 states evidentiary readiness; and #20 assigns the integrator, commitment/change authority, conflict resolution, stop rights, and residual-risk acceptance. |

Integrated increment prioritization, dependency sequencing, commitment tracking, and scope/schedule coordination are therefore a **program leadership and project-management function**, not a twenty-first perspective. A roadmap, dependency map, backlog, schedule, and commitment log are artifacts. Durable tradeoffs cannot disappear into that function: strategic value changes route to #1, support-promise changes to #7, release-contract changes to #17, affordability/capacity changes to #19, and approval or exception authority to #20.

No addition, split, merger, or removal survives the independent-authority, evidence-standard, and failure-consequence tests. The changes are label, charter, and routing corrections only.

## What belongs below the first level

- **Sublenses:** alternatives, spoiler handling, plugin order, localized game data, transaction steps, accessibility modes, channel matrices, certificate continuity, and sunset details.
- **Embedded criteria:** usability, accessibility conformance, performance, resource boundedness, reliability, maintainability, evidence freshness, privacy minimization, recovery time, and support capacity. Each criterion sits under the perspective owning its consequence.
- **Functions and workstreams:** discovery, user research, UX design, reverse engineering, fixture creation, software engineering, QA execution, release engineering, support, vulnerability response, program leadership, project management, procurement, communications, and records administration.
- **Gates:** architecture feasibility, support and named-operation qualification, mutation safety, hostile-input/privacy review, release evidence, rollback readiness, and go/no-go. Gates combine decisions and evidence; they do not acquire the contributing perspectives' authority.
- **Phases and spikes:** prototypes, format-research spikes, game-update investigations, migration efforts, incident investigations, requalification cycles, and retirement execution.
- **Artifacts and metrics:** roadmaps, backlogs, schedules, personas, glossaries, semantic models, compatibility matrices, threat models, data-flow maps, test corpora, assurance cases, SBOMs, packages, budgets, risk/dependency registers, runbooks, decision logs, support records, archives, outcome signals, accessibility results, failure rates, recovery measures, and bus-factor indicators.

## Why 20 is neither too few nor too many

The map is not too few because this editor combines unusually independent consequences despite its focused scope. It must distinguish game meaning from fingerprint-specific runtime truth and both from permission to mutate a save. It must preserve user artifacts transactionally, withstand hostile files and Windows path behavior, keep save-derived data local, respect third-party rights without bundling proprietary assets, and remain usable and accessible. It must also distinguish a compatibility promise from an authenticated install/update contract and from the policy for responding when a game update invalidates that promise. Evidence sufficiency, affordability, and authority can each fail while the other two remain sound.

The map is not too many because none of the 20 denotes a required team, specialist craft, lifecycle phase, ceremony, or document. One maintainer or workstream may serve several perspectives, and shared gates may evaluate them together. Each first-level item still answers one durable question, has an omission failure not reducible to a neighbor, and retains relevance from discovery through retirement. Proposed extra categories reduce to sublenses, criteria, functions, gates, phases, artifacts, or metrics; proposed mergers erase an independent authority, evidence standard, or failure consequence.

Accordingly, **20 is the decisive final count: complete at the durable-decision level and parsimonious for this specific offline WinUI save editor.**
