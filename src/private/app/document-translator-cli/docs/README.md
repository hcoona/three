# Document Translator Records

These records describe two capabilities of the same CLI. The numbered directory
names are retained reference paths, not delivery phases or separate products.

- [Baseline requirements](00-baseline-document-translation/requirements.md) and
  [high-level design](00-baseline-document-translation/high-level-design.md)
  describe the whole-document service route and shared CLI behavior.
- [Markdown-aware requirements](01-markdown-aware-translation/requirements.md)
  and [high-level design](01-markdown-aware-translation/high-level-design.md)
  extend that baseline with Markdown routing, protected source spans, text
  translation, and output validation. Their explicit routing rules cover
  `auto`, `aware`, and `legacy`; the baseline's original Markdown exclusion
  describes its capability boundary, not an exclusion from the extended CLI.
- The [baseline implementation plan](00-baseline-document-translation/implementation-plan.md)
  and [Markdown implementation plan](01-markdown-aware-translation/implementation-plan.md)
  retain detailed component contracts, safety constraints, and validation
  guidance. Their construction steps and phase names are design context, not
  current progress or grants to implement them again.

Project authors maintain these records when the relevant capability changes;
implementers and reviewers consume the requirements and matching design
together. In particular, Markdown work must preserve the non-Markdown route and
the shared credential, output, and error contracts. The existing source and
tests provide implementation evidence; a plan's readiness label does not prove
that its validation ran.

Generic work authorization, coordination, and finding disposition follow the
[repository record system](../../../../../docs/governance/record-system.md)
and the accepted [Delivery Wave](../../../../../docs/delivery-wave.md).
