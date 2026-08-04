# Contract fix implementation plan

1. Replace the broad enum converter with explicitly registered strict generic converters.
2. Add all-public-enum wire, invalid-input, reflection-disabled, and literal-number regressions.
3. Add Yarn secret token binding validation equivalent to npm, constrained to secret value writes.
4. Switch Yarn token producers to the canonical selector while leaving `npmAlwaysAuth` unchanged.
5. Teach the Yarn writer and Phase 14 ownership checks to recognize the canonical token selector.
6. Add focused binding-scope and producer tests.
7. Run Contracts tests, focused Yarn/ConfigurationManager tests, full platform tests, and the
   affected project build.
8. Review pseudo-mutations and assertion quality before committing.
