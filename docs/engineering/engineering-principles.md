# Shared Engineering Principles

The repository owner maintains these principles for authors and reviewers of
design, security and validation decisions. Apply the relevant sections with the
owning project's accepted requirements and concern records. Project scope, threat
models, validation gates and [work authorization](../delivery-wave.md) retain
their existing authorities. Concrete decisions belong in the owning project or
its work carrier; this shared guide does not combine project knowledge.

## Design

### Explicit Assumptions and Dependency Responsibilities

Base each design on accepted requirements, concrete user scenarios and documented
dependency contracts. Identify assumptions that could invalidate a choice and
the boundary responsible for satisfying them. Rely on supported platform,
library and service abstractions within their declared contracts; application
code need not independently reimplement or mechanically prove those abstractions.

The application remains responsible for its request constraints and required
result checks. Trust in a dependency does not establish an undocumented
capability or prove that a particular configuration and environment satisfies
those constraints. When a required capability is absent, keep that path
unavailable. If the limitation blocks a required journey, report the feasibility
gap for owner disposition rather than silently weakening the requirement or
compensating through an expanded application boundary.

### Scenario-Driven Abstractions

Introduce a component, interface or extension point only when a current scenario,
responsibility boundary or independently changing dependency justifies it.
Evaluate the abstraction against the primary journey and another applicable
scenario or failure path; do not invent future consumers to justify generality.
A conceptual responsibility does not automatically require a separate service,
package, interface or class.

Keep design and implementation inside the accepted product boundary. Additional
consumer protocols, platform services or compatibility behavior require an
explicit owner scope decision and applicable accepted work authorization.

### Bounded Failure and Proportionate Assurance

When an edge case prevents establishing a required safety condition, use the
existing terminal outcome or mark the path unavailable. Do not add unbounded
retries, fallback chains or speculative repair mechanisms to make every
environment succeed. Apply accepted recovery and persistence semantics where
requirements already permit a safe outcome; fail-closed behavior does not turn
every recoverable condition into failure.

The project's security authority owns its threat assumptions and mitigation
tradeoffs; its validation authority owns the required scenario, unit, contract
and real-environment evidence. Mechanical checks and exhaustive testing do not
substitute for contextual design judgment.

### Architecture Views

Use standard C4 system-context and container views, with component views where
they clarify responsibilities inside a container. A C4 container denotes an
application or data store, not necessarily a deployment container. Use UML
sequence and state-machine views for interactions and lifecycles whose ordering
or termination matters. Keep each diagram at one stated level and consistent
with its surrounding authoritative text.

Choose diagrams for a concrete reader question; a complete diagram catalog is
not a deliverable. Existing user stories, requirements and validation scenarios
provide the requirements basis. Add a use-case diagram only if it resolves an
actual ambiguity about actors, goals or the system boundary.

## Security

Evaluate a mitigation against a concrete scenario, protected asset, credible
attacker or failure, and the trust boundary it crosses. Compare risk reduction
with implementation complexity, ongoing maintenance and operational cost.
Prefer the smallest mechanism that satisfies accepted security requirements;
speculative attack chains outside the project's stated threat model do not
justify unlimited application hardening.

Within that threat model, rely on documented protections of maintained
dependencies and platforms. Review the application's configuration and use of
those contracts rather than building a second implementation of their security
guarantees. Missing or contradictory capability evidence remains an integration
question, not a reason to assume a stronger guarantee.

If required trust or safety conditions cannot be established, fail closed at the
affected boundary using existing outcome semantics. Bound recovery and cleanup
to state the application owns and effects it is authorized to perform. Do not
broaden access, repair unrelated state or invent additional services to handle
an extreme case. Preserve explicitly permitted recovery and validated-success
outcomes with persistence warnings; those outcomes cannot bypass required
validation.

Cost is not permission to weaken an accepted requirement. If a required journey
cannot be realized under these assumptions at reasonable cost, present the
limitation and tradeoff for owner disposition. A broader threat model, reduced
security guarantee or expanded product boundary needs an explicit accepted
decision.

## Testing

Organize most business-behavior coverage around user journeys and applicable
interaction, reuse and failure scenarios. Exercise the application boundary with
controlled dependency substitutes where practical, and assert caller-visible
outcomes and required side effects. Scenario tests need not contact a service,
open real UI or run the entire deployed system.

Use focused unit tests for core algorithms and functions whose correctness
warrants precise protection, such as matching rules, invariant checks, outcome
classification and deadline transitions. Protect serialized public behavior with
contract tests. Avoid locking ordinary orchestration code to private method
calls, mock call counts, class layouts or incidental internal ordering. Required
ordering remains observable behavior to test. A behavior-preserving refactor
should not require rewriting business expectations.

Test layers and matrices describe coverage responsibilities, not a requirement
to repeat every case at every level. Choose the least costly level that
establishes the property. Rely on documented dependency contracts for delegated
behavior; test the application's use of the contract and integration assumptions
that could change an architectural choice. Do not attempt to re-prove a platform
or provider through an exhaustive mock suite.

Use public desk evidence when it answers a decision-relevant question. Support
claims depending on actual platform or external-system behavior still require
the applicable bounded observations; simulated success cannot supply that evidence. This
guidance does not waive primary-journey gates, required rechecks, experiment
authorization or accepted protocols. Evidence depth should reflect the decision,
credible failure and cost, rather than an arbitrary unit-test count or coverage
target.

## Source

Adapted from the three engineering sections introduced by upstream PR #37 at
`hcoona/microsoft-authentication-cli@f2e36bbccbda134ed8aa21f783cd76a9cdbeaf64`:
[design principles][design-source], [security design tradeoffs][security-source]
and [test design and evidence selection][testing-source]. Monorepo adaptations
replace authentication-specific dependencies, scenarios and outcome examples
with each project's accepted counterparts; they do not import the source
product's scope, threat model or release gates. The source
[copyright and MIT notice](../governance/reference-license.txt) is retained.

[design-source]: https://github.com/hcoona/microsoft-authentication-cli/blob/f2e36bbccbda134ed8aa21f783cd76a9cdbeaf64/docs/architecture/overview.md#design-principles
[security-source]: https://github.com/hcoona/microsoft-authentication-cli/blob/f2e36bbccbda134ed8aa21f783cd76a9cdbeaf64/docs/security/threat-model.md#security-design-tradeoffs
[testing-source]: https://github.com/hcoona/microsoft-authentication-cli/blob/f2e36bbccbda134ed8aa21f783cd76a9cdbeaf64/docs/validation/strategy.md#test-design-and-evidence-selection
