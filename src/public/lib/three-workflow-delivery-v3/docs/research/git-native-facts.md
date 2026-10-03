# Git Native Revision and Path Facts

## Question and Consumer

Can the replacement application's repository analysis obtain committed identities,
recursive path inventory and complete changed-name sets through supported Git
interfaces, without implementing revision/tree/diff semantics or adding fetching?
The internal [Git reader](../../../../../private/app/workflow-delivery/Repository/GitReader.cs)
consumes these findings under the HLD's [native boundary](../high-level-design.md#native-integrations)
and MLD's [selection contract](../middle-level-design.md#selection-algorithm).
The CI event caller chooses its candidate and comparison basis.

## Public Source Findings

Git sources below are pinned to v2.45.4, commit
`f94b90ad6e49cc7f15c4171c5a434aa459e82d2d`, retrieved on 2026-10-03.
The installed local Git is 2.45.4. These are source findings, not hosted execution
or a claim of every Git version/configuration's behavior.

| Finding                                                                                                                                     | Immutable supporting source                                                                 |
| ------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| `--no-lazy-fetch` disables on-demand promisor fetching; `--no-replace-objects` selects stored objects                                       | [`git.txt`, lines 176–187][global]                                                          |
| `rev-parse --verify --end-of-options <ref>^{commit}` verifies and peels the caller's ref as a commit                                        | [`git-rev-parse.txt`, `--verify`][resolve]                                                  |
| Recursive `ls-tree -r -z --full-tree` reports native modes, object types and paths independent of the current subdirectory                  | [`git-ls-tree.txt`, options and output format][tree]                                        |
| Direct two-tree `diff-tree -r` compares the selected endpoints                                                                              | [`git-diff-tree.txt`, description and synopsis][compare]                                    |
| `--name-only -z --no-renames` yields unquoted names; disabling rename folding retains deletion/addition paths                               | [`diff-options.txt`, lines 322–340, 498–500][diff]; [`diff.c`, name-only output][diff-code] |
| `--no-relative`, `--no-ext-diff`, `--no-textconv` and `--ignore-submodules=none` retain the intended native name set under ambient settings | [`diff-options.txt`, respective options][diff]                                              |

The selected recipe applies both global options to every query. Resolve each
endpoint once and reuse its native full ID without a SHA-1 width assumption.
Inventory uses `ls-tree -r -z --full-tree <id> --`. Comparison uses
`diff-tree -r --no-commit-id --name-only -z --no-renames --no-relative
--no-ext-diff --no-textconv --ignore-submodules=none <basis-id> <candidate-id> --`.
There is no pathspec, merge-base substitution or application rename classifier.
The changed-name consumer does not need status parsing.

## Raw Output Decoding

Git's NUL mode emits filenames verbatim. Native names require strict byte decoding
before the reader parses records; default replacement fallback changes identities.
The .NET v10.0.0 runtime source, commit
`60629d14374c56f1cb51819049ad1fa529307f8d`, shows that redirected Process stdout
uses a BOM-detecting StreamReader on both [Unix][process-unix] (lines 476–477)
and [Windows][process-windows] (lines 640–641). Supplying only
`StandardOutputEncoding` does not disable that behavior.

The supported [StreamReader constructor][stream-reader] (lines 139–174) accepts an
explicit encoding and `detectEncodingFromByteOrderMarks: false`. An owned reader
over untouched stdout `BaseStream`, with `new UTF8Encoding(false, true)`, has an
empty preamble and rejects malformed UTF-8. The empty preamble also prevents the
reader from independently consuming an initial UTF-8 signature. This preserves a
first filename beginning U+FEFF and prevents UTF-16/32-looking bytes from selecting
another decoder. This is a supported native-text boundary; arbitrary-byte Git
names remain unsupported.

Parse NUL records and split `ls-tree` metadata at its first tab. Preserve the
remaining path exactly, including tabs/newlines, case and Unicode. Gitlink and
symlink modes/types remain visible; no submodule traversal is claimed. The later
CI path contract still decides representability and ownership.

## Validation and Limits

Ordinary application integration tests use finite isolated Git repositories and
the actual bounded process helper. Scenarios cover peeled refs, committed
inventory despite dirty/untracked state, additions/modifications/deletions and
both rename names, divergent endpoints, conflicting diff configuration, replacement
refs, native symlink/gitlink metadata and changed gitlinks, unusual UTF-8 names,
SHA-1/SHA-256 IDs, empty trees, missing refs/required tree objects and cancellation.
Raw-byte process cases preserve leading U+FEFF and reject malformed/BOM-looking
invalid UTF-8. Index metadata avoids requiring OS symlink privileges or checking
out unusual names. Results and exact reviewed trees belong in the delivery PR.

This component supplies committed tree/path inputs. Complete RepositoryFacts,
ownership/input expansion, ecosystem evaluation at each revision, event mapping
and caller cutover remain pending. Missing objects required by a selected query
fail; no unrelated blob/history audit, fetching, repair, retry, trust record or
full-run fallback is added. Ordinary Windows changed-code CI remains the existing
validation path; its feasibility is the owner's accepted assumption. No external
experiment, credentials, hosted dispatch or publication is part of these tests.

[global]: https://github.com/git/git/blob/f94b90ad6e49cc7f15c4171c5a434aa459e82d2d/Documentation/git.txt#L176
[resolve]: https://github.com/git/git/blob/f94b90ad6e49cc7f15c4171c5a434aa459e82d2d/Documentation/git-rev-parse.txt
[tree]: https://github.com/git/git/blob/f94b90ad6e49cc7f15c4171c5a434aa459e82d2d/Documentation/git-ls-tree.txt
[compare]: https://github.com/git/git/blob/f94b90ad6e49cc7f15c4171c5a434aa459e82d2d/Documentation/git-diff-tree.txt
[diff]: https://github.com/git/git/blob/f94b90ad6e49cc7f15c4171c5a434aa459e82d2d/Documentation/diff-options.txt
[diff-code]: https://github.com/git/git/blob/f94b90ad6e49cc7f15c4171c5a434aa459e82d2d/diff.c
[process-unix]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Unix.cs#L476
[process-windows]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Windows.cs#L640
[stream-reader]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/libraries/System.Private.CoreLib/src/System/IO/StreamReader.cs#L139
