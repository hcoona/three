# WebHDFS File Provider: Abstract Semantics and TLA+ Models

Adapting [Apache Hadoop WebHDFS v1][webhdfs] to
[`Microsoft.Extensions.FileProviders`][file-providers] bridges two well-specified
but semantically distinct models: `.NET` callers expect a rooted, read-only,
capability-confined file tree with coherent `IFileInfo`, `IDirectoryContents`,
and `IChangeToken` contracts, whereas WebHDFS exposes non-transactional HTTP
operations split across NameNode metadata (`GETFILESTATUS`, `LISTSTATUS`) and
redirected DataNode byte streams (`OPEN` via HTTP `307`).

This nonnormative design candidate and its finite TLA+ models provide an
implementation-independent specification for analyzing
`WebHdfs.Extensions.FileProviders`. The package [`README.md`](../../README.md)
owns the currently published `1.x` behavior; this document neither describes
the current code, alters published package contracts, nor authorizes
implementation changes—any future public-contract or capability rewrite must be
separately authorized in [`docs/delivery-wave.md`](../../../../../../docs/delivery-wave.md),
reviewed, and merged.

[webhdfs]: https://hadoop.apache.org/docs/stable/hadoop-project-dist/hadoop-hdfs/WebHDFS.html
[file-providers]: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/file-providers

## Four Core Decisions

| Decision                                                                        | What establishes the answer                                                                                                                                                                                                                                                                                                                                                                                       | What does not                                                                                                                                                                                                                                             |
| ------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Which caller-addressed HDFS coordinate may an operation target?                 | Normalizing `subpath` or `filter` relative to the mounted HDFS `Root` without ever dropping above `Root`, locally rejecting trailing-slash `GetFileInfo("a/")` and mid-pattern `..` globs, and bounding `Watch` to $O(1)$-request patterns (server-side symlink chasing on default HttpFS remains an explicit backend boundary below).                                                                            | Stripping slashes onto HDFS `/` regardless of `Root`, letting WebHDFS strip trailing `/` on a file, allowing `a/../../root/a` to re-enter `Root`, or throwing `NotSupportedException` on `**` globs.                                                      |
| Does `GetFileInfo` or `GetDirectoryContents` report `Exists = true`?            | Matching both existence and target node kind (`FILE` only for `GetFileInfo`; `DIRECTORY` only for `GetDirectoryContents`), while separating authoritative absence and file-ancestor traversal from remote observation faults across NameNode and HttpFS.                                                                                                                                                          | Any HTTP `200` `LISTSTATUS` payload (which emits `pathSuffix = ""` on a `FILE`), or classifying by HTTP status / `RemoteException.exception` alone without accounting for HttpFS HTTP `500` wrapping and `FSDirectory` `AccessControlException` wrapping. |
| What may a caller assume when opening `CreateReadStream()` from an `IFileInfo`? | Local rejection of missing or directory handles without network I/O, untorn delivery of a valid live `FILE` version whenever already-opened bytes are neither replaced nor truncated mid-read (capped at `Content-Length` on DataNode redirect, or matching cached / refreshed same-inode `LocatedBlocks` length on chunked HttpFS), and explicit failure on `< Content-Length` truncation or block invalidation. | Assuming `Stream` byte count equals the earlier `IFileInfo.Length` snapshot, assuming snapshot isolation when `<path>` is replaced or truncated mid-read, or coupling transport disposal to `IFileInfo`.                                                  |
| When must an `IChangeToken` latch `HasChanged` and invoke callbacks?            | A healthy poll observing a signature different from the token's baseline (or recovering from an `unobserved` initial outage), latching `HasChanged` monotonically before invoking each active registration at most once.                                                                                                                                                                                          | Comparing `modificationTime` alone, watching only parent directory `modificationTime` for child file edits, case-insensitive globbing on POSIX HDFS, firing during an active outage, or mutating an existing token's spec on `Provider.Dispose()`.        |

## Root Confinement and Path Normalization

An `IFileProvider` instance mounts an immutable HDFS directory `Root` (such as
`/` or `/app`). Every caller `subpath` or `Watch(filter)` is interpreted
relative to `Root`:

- **Leading slashes stay relative:** `"a"`, `"/a"`, and `"b/.././a"` resolve to
  the same target under `Root`. When `Root = "/app"`, `"/outside"` resolves to
  `"/app/outside"`, never HDFS `"/outside"`.
- **Sticky above-root rejection:** Segment reduction tracks whether `..` ever
  attempts to pop an empty relative stack. Once traversal drops above `Root`
  (including re-entry attempts such as `"a/../../app/a"`), resolution fails
  closed locally with zero WebHDFS requests: `GetFileInfo` returns
  `NotFoundFileInfo`, `GetDirectoryContents` returns
  `NotFoundDirectoryContents`, and `Watch` returns `NullChangeToken`.
- **Root and trailing-slash asymmetry:** `GetDirectoryContents("")`,
  `GetDirectoryContents("/")`, and `GetDirectoryContents("a/")` target `Root` or
  subdirectory `"a"`, whereas `GetFileInfo("")`, `GetFileInfo("/")`, and any
  trailing-slash path `GetFileInfo("a/")` resolve locally to `NotFoundFileInfo`
  with zero WebHDFS calls—preventing WebHDFS from stripping a trailing `/` and
  returning `200 FILE` for `"a/"`. Every `IFileInfo.Name` exposes only a
  path-free leaf name (`""` for empty or trailing-slash inputs, leaf segment
  otherwise).

## Type Segregation, Symlinks, and Epistemic Error Mapping

`.NET` separates regular-file lookup from directory enumeration, whereas WebHDFS
exposes raw inode metadata with overloaded status codes and wrapped exceptions:

- **`GetFileInfo(subpath)`:** Projects `Exists = true` only when `subpath` has
  no trailing slash and `GETFILESTATUS` returns a `FILE` (`IsDirectory = false`,
  `Length >= 0`, `Name != ""`). When the target is a `DIRECTORY`, `GetFileInfo`
  returns `Exists = false`, `IsDirectory = false`, and `Length = -1` so
  `CompositeFileProvider` and static file consumers do not mistake a directory
  for a readable file.
- **`GetDirectoryContents(subpath)` in one `LISTSTATUS` call:** On HDFS and
  HttpFS, `LISTSTATUS` on a `FILE` returns HTTP `200` with a `FileStatus` whose
  `pathSuffix` is `""`, whereas every child inside a `DIRECTORY` has a non-empty
  `pathSuffix` (and an empty `DIRECTORY` returns `[]`). Rejecting any response
  where **any** element has `pathSuffix == ""` establishes both
  `IDirectoryContents.Exists` and the materialized child list in a single HTTP
  request—avoiding the two-RPC (`GETFILESTATUS` then `LISTSTATUS`) split-brain
  window AND rejecting torn multi-page NameNode streams where a directory is
  replaced by a `FILE` between batches (causing `FSDirStatAndListingOp` to
  append a `pathSuffix == ""` record on the final batch and close `]}}`
  cleanly). Because NameNode's `getListingStream` and HttpFS's
  `DistributedFileSystem.listStatusInternal` page large directories without a
  cross-page lock, a completed listing is a locally coherent value rather than
  an atomic server-side cut; if the directory is deleted between batches,
  NameNode aborts the already-started HTTP `200` JSON stream (`Fault`), whereas
  HttpFS buffers before headers and returns HTTP `404`
  (`NotFoundDirectoryContents`). Conversely, on HttpFS specifically,
  `FSOperations.FSListStatus` buffers all `fs.listStatus(path)` pages and then
  separately calls `fs.getFileStatus(path).isFile()` to format `pathSuffix`; if
  `path` was observed as a `FILE` during any buffered page and is a `DIRECTORY`
  by that final check (such as `FILE -> DIRECTORY` or multi-page `DIRECTORY ->
FILE -> DIRECTORY`), HttpFS formats the buffered file status with a non-empty
  `pathSuffix` (`HttpFsListFileToDirRaceCounterexample.cfg`), an unpreventable
  server-side non-atomicity. Enumerated child directories project `Exists = true`,
  `IsDirectory = true`, and `Length = -1`; enumerated child files project
  `Exists = true`, `IsDirectory = false`, and `Length >= 0`.
- **Wire-visible `SYMLINK` exclusion and server-side symlink boundary:**
  Inside a listed `DIRECTORY`, both NameNode (`JsonUtil.toJsonMap`) and
  HDFS-backed HttpFS (`DistributedFileSystem.listStatus` +
  `FSOperations.toJsonInner`) emit direct child symlinks as `type = "SYMLINK"`
  with non-empty `pathSuffix`, which `GetDirectoryContents` and flat-directory
  watches exclude without chasing targets. When the requested path itself or a
  proper ancestor is a symlink, NameNode rejects with HTTP `403`
  `UnresolvedPathException` (and WebHDFS-proxied HttpFS emits HTTP `500`
  `UnresolvedPathException`), whereas HDFS-backed HttpFS (`DistributedFileSystem`)
  with `fs.client.resolve.remote.symlinks = false` wraps
  `UnresolvedLinkException` via `FileSystemLinkResolver.resolve` into HTTP `500`
  `IOException` with message `"Path <target> contains a symlink and symlink
resolution is disabled (fs.client.resolve.remote.symlinks)."`—all mapped
  uniformly to non-existent. Conversely, under Hadoop's default
  `fs.client.resolve.remote.symlinks = true` (or on `RawLocalFileSystem`),
  HttpFS resolves symlinks server-side transparently (`200 FILE`/`DIRECTORY` or
  `404`), leaving no HTTP provenance to detect outside-root targets (matching
  `PhysicalFileProvider`'s root-scoping boundary); and hypothetical custom
  non-HDFS `FileSystem` plugins that return `FileStatus(isSymlink = true)`
  directly from `getFileStatus(path)` and `listStatus(path)` are outside scope
  because `FSOperations.FSListStatus` (`fs.getFileStatus(path).isFile() ==
false`) would emit `[{pathSuffix: leaf, type: "SYMLINK"}]`, wire-identical in
  one `LISTSTATUS` call to a directory containing a single child symlink.
- **Strict epistemic separation across NameNode and HttpFS errors:** NameNode's
  `ExceptionHandler` maps `SecurityException` and non-`FileNotFoundException`
  `IOException` subtypes to HTTP `403`, whereas `HttpFSExceptionProvider` maps
  `SecurityException` to HTTP `401` and non-`FileNotFoundException`
  `IOException` subtypes to HTTP `500`. Furthermore, Hadoop's
  `FSDirectory.resolvePath` wraps read-path (`!isCreate`)
  `ParentNotDirectoryException` into
  `org.apache.hadoop.security.AccessControlException` with message
  `"<ancestor> (is not a directory)"` (`FSDirStatAndListingOp.getFileInfo`
  swallows that `AccessControlException` to `404` only for HDFS superuser
  `getFileInfo` lookups—covering NameNode `GETFILESTATUS`, superuser HDFS-backed
  HttpFS `GETFILESTATUS`, and NameNode `OPEN`'s preliminary lookup—never for
  `LISTSTATUS` or non-superuser calls). In a single RPC, structured fields
  `(statusCode, exception, javaClassName)` alone provably cannot separate a
  `FILE` ancestor from a real directory permission denial
  (`StructuredExceptionOnlyCounterexample.cfg`), and generic `IOException` on
  HttpFS HTTP `500` likewise requires matching
  `FileSystemLinkResolver`'s disabled-symlink message (`ServerVariantUniformityLaw`).
  The target model therefore classifies HTTP `404` `FileNotFoundException`, HTTP
  `403`/`500` `UnresolvedPathException`, HTTP `500` `IOException` matching
  `FileSystemLinkResolver`'s exact disabled-symlink message for `<target>`, and
  HTTP `403`/`500` `AccessControlException` whose first-line `message` equals
  `<ancestor> (is not a directory)` for a resolved proper ancestor as
  `Exists = false`, while propagating all other `AccessControlException`,
  `IOException`, `SecurityException`, `StandbyException`, `5xx`, transport, and
  mid-stream truncation errors as explicit faults.

## Snapshot Coherence, `OPEN` Streaming, and `Dispose()` Lifecycles

`IFileInfo` and `IDirectoryContents` do not implement `IDisposable` in
`Microsoft.Extensions.FileProviders`. Materialized instances must therefore be
pure immutable values carrying zero owned sockets or `HttpClient` lifecycles;
disposable ownership belongs solely to the provider, each opened `Stream`, and
each `IChangeToken` callback registration:

- **Explicit `Provider.Dispose()` contract:** Once `Provider.Dispose()` runs,
  provider entrypoints `GetFileInfo` and `GetDirectoryContents` fail closed with
  `ObjectDisposedException` (`"disposed"`) before path or network steps, while
  `Watch` returns `NullChangeToken` (stopping background polling without
  crashing racing `ChangeToken.OnChange` re-registration blocks). Already
  materialized `IFileInfo` and `IDirectoryContents` values remain readable and
  enumerable without error. On `CreateReadStream()`, intrinsic non-streamable
  values (`!Exists` -> `FileNotFoundException`, `IsDirectory` ->
  `InvalidOperationException`) fail locally without touching the provider, while
  existing file handles check `Provider.Dispose()` (`ObjectDisposedException`)
  before network I/O, and already-opened streams remain closable.
- **Non-atomic `CreateReadStream()` across NameNode redirect and HttpFS:**
  WebHDFS has two distinct `OPEN` execution profiles:
    - **NameNode + DataNode (`307` redirect + `Content-Length`):** NameNode
      issues HTTP `307` carrying `<path>` for both `FILE` and `0`-length
      `DIRECTORY` (`NamenodeWebHdfsMethods.chooseDatanode`), and DataNode
      `WebHdfsHandler.onOpen` rejects `DIRECTORY` with `404` (`"Path is not a
file"`) while binding `FILE` responses to HTTP `Content-Length` and
      `LimitInputStream(in, in.getVisibleLength())`. Pure appends after DataNode
      open are ignored beyond `Content-Length`, and mid-stream replacements
      shorter than `Content-Length` fail closed on premature EOF rather than
      returning truncated bytes as clean EOF.
    - **HttpFS (`200` chunked without `Content-Length`):** `HttpFSServer` opens
      `FileSystem.open(path)` directly without `307` redirect or
      `Content-Length`, streaming chunks via `InputStreamEntity`. On HDFS
      (`DistributedFileSystem`), `DFSInputStream` caches `locatedBlocks` length
      at open time (`dfs.client.refresh.read-block-locations.ms = 0` by default)
      and reaches clean EOF at that cached length unless `LocatedBlocks`
      refresh (or a non-caching `FileSystem` such as `RawLocalFileSystem`)
      updates the unframed stream to a prefix-compatible appended or
      block-truncated version on the same inode (if `pos` already passed a
      refreshed truncated length, chunked `200` closes at `pos` with the
      already-read prefix of the pre-truncate version).
    - Across both profiles, `IFileInfo.Length` need not equal clean EOF length
      (due to under-construction last blocks and concurrent appends/overwrites),
      and because `DFSInputStream` refetches uncached block locations by
      pathname, rewriting already-opened bytes mid-stream (via replacement or
      same-inode `TRUNCATE` + `APPEND`) can finish from cache, abort with an
      explicit I/O fault (preserving any caller-read prefix), or splice blocks
      within framing limits—matching non-transactional `PhysicalFileProvider`
      semantics.

## Polling `Watch` and Observability Boundaries

WebHDFS v1 provides no server-push notification channel. Under bounded $O(1)$
per-poll cost:

- **Filter classification and case-sensitive globbing:** `Watch(filter)` binds
  an immutable `tokenSpec` at creation following `.NET` `PatternBuilder` rules
  configured with **`StringComparison.Ordinal`** (required because HDFS is a
  case-sensitive POSIX namespace where `"A"` and `"a"` are distinct). Literal
  paths without wildcards or trailing slashes use `GETFILESTATUS` (allowing
  in-root relative normalization such as `"b/../a"`). Single-directory
  non-recursive wildcards (`*`, `*.*` folded by `PatternBuilder` to `*`, `a*`,
  `sub/*`) without directory-segment wildcards, trailing slashes, or `..`
  segments use a single `LISTSTATUS` matching direct child `FILE` entries
  ordinally. Invalid filters, empty/root filters, trailing-slash filters (`dir/`
  -> `dir/**`), explicit `**` / `**.<ext>` recursive `RaggedPattern` forms,
  directory-segment wildcards (`b*/a`), any `..` inside a wildcard pattern (`PatternBuilder` rejects mid-pattern `b/../a*`, and
  leading `../a*` escapes `Root`), disabled polling, and calls after
  `Provider.Dispose()` return `NullChangeToken` without throwing.
- **Rich metadata signatures:** Exact watches compare `(state, kind, fileId?,
length, mtime)` rather than `mtime` alone, catching same-millisecond atomic
  `RENAME` and file/directory swaps. Flat-directory watches compare the set of
  `(name, kind, fileId?, length, mtime)` tuples across matching direct child
  files, catching in-place child appends/modifications that leave the parent
  directory's `mtime` unchanged while ignoring non-matching sibling churn and
  ignoring file modifications when the watched flat target path itself is a
  `FILE` rather than a `DIRECTORY`.
- **Outage discipline (`unobserved` vs valid baseline):** If the initial
  observation at `Watch()` fails transiently, the token records an `unobserved`
  baseline with `HasChanged = false` instead of throwing or spinning. Subsequent
  polls that also fail keep `HasChanged = false`. Once remote observation
  recovers, an `unobserved` baseline latches `HasChanged = true` so callers such
  as `ChangeToken.OnChange` synchronize with recovered state and establish a
  valid baseline on their successor token. If a valid baseline was already
  captured, transient mid-watch poll failures preserve that baseline without
  spurious invalidation, and any change whose post-recovery signature still
  differs from the preserved baseline is detected on recovery.
- **Callback ordering and independent token generations:** Every transition of a
  callback into `"invoked"` (whether asynchronous delivery after polling or
  synchronous registration on an already-changed token) requires `HasChanged` to
  hold in both the pre-state and post-state. Each undisposed active registration
  fires at most once. Creating a successor token via `ChangeToken.OnChange`
  leaves secondary undisposed registrations on the prior token intact and
  deliverable. Disposing the provider halts further polls without retroactively
  invalidating callbacks on an already-latched token.
- **Physical limits of polling:** Without server-side event logs, pure polling
  cannot detect transient `A -> B -> A` mutations that revert completely between
  two polls; on generic WebHDFS backends that omit HDFS `fileId` (such as
  non-HDFS `HttpFS` mounts), an atomic swap between two distinct files of
  identical byte length and millisecond `modificationTime` is observationally
  indistinguishable from no change; and on `HttpFS`, the two-step `FSListStatus`
  `FILE -> DIRECTORY` race above can likewise produce one benign spurious
  invalidation on a flat-directory watch.

## Finite TLA+ Model Suite

All modules share [`WebHdfsRules.tla`](models/WebHdfsRules.tla), which encodes
the HDFS skeleton directly as segment sequences (`<<>>`, `<<"app">>`,
`<<"app", "a">>`, ...) so path confinement (`root \o stack`), child resolution,
wire behavior, projections, and watch signatures have a single algebraic
definition. Upstream protocol and framework facts were cross-checked by source
inspection (no live cluster or external empirical runs) against pinned commits
of [`apache/hadoop`][hadoop-rev], [`dotnet/runtime`][dotnet-runtime-rev], and
[`dotnet/aspnetcore`][aspnetcore-rev]; state counts below were checked
exhaustively via `github:tlaplus/tlaplus` `v1.7.4` (`TLC` `2.19`) and hold
within each configuration's declared finite path, byte, and mutation bounds.

[hadoop-rev]: https://github.com/apache/hadoop/tree/429bf2ee95ad823179d320aa2570f1ad2ef57ccd
[dotnet-runtime-rev]: https://github.com/dotnet/runtime/tree/5b65c8d478e0b783ba7a01bc3aea5d57a1903abd
[aspnetcore-rev]: https://github.com/dotnet/aspnetcore/tree/ba2a2584971cd590f98dd5ed16c6eba4e30c23e3

| Model and configuration                                                                                                    | Checked boundary and invariants                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | Distinct states |
| -------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------: |
| [`PathAndProjectionDecisions`](models/PathAndProjectionDecisions.tla) ([cfg](models/PathAndProjectionDecisions.cfg))       | Two mount roots (`/` and `/app` with sibling `/outside`), five HDFS tree topologies (including symlinks, file-as-ancestor, case-distinct `"A"`/`"x"`, empty and under-construction files), four server variants (`namenode-user`, `namenode-super`, `httpfs`, `httpfs-webhdfs`), seven fault modes, sixteen caller path forms (including trailing slashes), and nineteen raw watch filter forms (including `b/../a*`, `**.txt`, `*.*`, and `Ordinal` case checks) across enabled/disabled polling and disposal.                                                                                                                  |             560 |
| [`SnapshotAndStreamConsistency`](models/SnapshotAndStreamConsistency.tla) ([cfg](models/SnapshotAndStreamConsistency.cfg)) | Orthogonal caller scenarios (`list-dir`, `stream-from-file`, `stream-from-dir`) across `namenode-redirect` (`307` + `Content-Length`) and `httpfs-direct` (chunked `200`), covering `0`-byte and under-construction files, two-page `GetDirectoryContents` on mutable `/app/a` and `/app` (routing every successful HttpFS listing through final `FSListStatus` `getFileStatus`), `DFSInputStream` cached reads (`ReadCachedByte`), HttpFS same-inode append/truncate refresh (`RefreshHttpFsLocatedBlocks`), uncached offset refetches (`FetchUncachedBlockAtOffset`), `< Content-Length` truncation, and `Provider.Dispose()`. |         219,260 |
| [`PollingChangeToken`](models/PollingChangeToken.tla) ([safety cfg](models/PollingChangeToken.cfg))                        | Interleaves trailing-slash/exact/root-flat/nested-flat filters, `fileId` presence, active vs passive callbacks, independent multi-token callback lifecycles across `ChangeToken.OnChange` re-registration, `Provider.Dispose()`, initial and mid-watch outages, and up to two HDFS mutations, verified without fairness assumptions against an independent direct-tree `OracleProjection`, pre-state `HasChanged` step ordering, and `ENABLED` readiness for recovery and successor creation.                                                                                                                                    |         432,896 |
| [`PollingChangeToken`](models/PollingChangeToken.tla) ([liveness cfg](models/PollingChangeTokenLiveness.cfg))              | Checks `LiveSpec` (`WF` on `Poll` and `DeliverCallback` with `MaxMutations = 1`), proving temporal `QuiescentEventualNotification`: persistent fault-free signature divergence on an undisposed provider eventually latches `HasChanged`, and every registered active callback on a latched token eventually settles (`invoked` or `disposed`).                                                                                                                                                                                                                                                                                  |          93,192 |

Nine companion counterexample configurations produce expected TLC violation
traces (exit code `12`), each refuting a plausible implementation shortcut or
establishing an unpreventable physical WebHDFS/HttpFS boundary:

| Configuration                                                                                       | Module                         | Violated invariant and established result                                                                                                                                                                                          |
| --------------------------------------------------------------------------------------------------- | ------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [`StructuredExceptionOnlyCounterexample`](models/StructuredExceptionOnlyCounterexample.cfg)         | `PathAndProjectionDecisions`   | `StructuredExceptionFieldsAloneSuffice`: proves `\E aceOutcome \in {"ok", "fault"}` is false—structured fields `(statusCode, exception)` alone cannot separate a `FILE` ancestor from `Permission denied` (subsuming status-only). |
| [`NaiveListStatusCounterexample`](models/NaiveListStatusCounterexample.cfg)                         | `PathAndProjectionDecisions`   | `Http200ListMeansDirectoryExists`: `LISTSTATUS` on a regular file returns HTTP `200` with a singleton `pathSuffix = ""` entry, refuting unguarded `200 => directory` mapping.                                                      |
| [`TrustSnapshotLengthCounterexample`](models/TrustSnapshotLengthCounterexample.cfg)                 | `SnapshotAndStreamConsistency` | `SnapshotLengthMatchesStreamBytes`: an under-construction block flush or concurrent append between `GetFileInfo` and `OPEN` yields clean EOF bytes longer than `IFileInfo.Length`.                                                 |
| [`TwoStepDirEnumerationCounterexample`](models/TwoStepDirEnumerationCounterexample.cfg)             | `SnapshotAndStreamConsistency` | `UncachedTwoStepDirectoryCoherent`: after `Exists = true`, replacing or deleting the directory before a separate `LISTSTATUS` call yields HTTP `404` or a `pathSuffix = ""` file singleton.                                        |
| [`ConcurrentReplaceTornStreamCounterexample`](models/ConcurrentReplaceTornStreamCounterexample.cfg) | `SnapshotAndStreamConsistency` | `UntornEvenUnderMidStreamReplacement`: replacing `/app/a` mid-stream across an uncached `DFSInputStream` block refetch splices old-prefix and new-suffix bytes into a hybrid sequence outside `seenFileBytes`.                     |
| [`HttpFsListFileToDirRaceCounterexample`](models/HttpFsListFileToDirRaceCounterexample.cfg)         | `SnapshotAndStreamConsistency` | `HttpFsListNeverFabricatesChild`: observing `/app/a` as `FILE` during HttpFS `fs.listStatus` buffering and as `DIRECTORY` during final `fs.getFileStatus.isFile()` formats the buffered file status with `pathSuffix = "a"`.       |
| [`MtimeOnlyBlindSpotCounterexample`](models/MtimeOnlyBlindSpotCounterexample.cfg)                   | `PollingChangeToken`           | `MtimeOnlyDetectsSameMsReplace`: same-millisecond atomic replacement changes `(kind, length, fileId)` without changing `modificationTime`.                                                                                         |
| [`ParentDirMtimeOnlyCounterexample`](models/ParentDirMtimeOnlyCounterexample.cfg)                   | `PollingChangeToken`           | `ParentDirectoryMtimeDetectsChildModify`: in-place modification of `/app/a` changes child metadata while parent `/app`'s `modificationTime` stays constant.                                                                        |
| [`PollingAbaBoundaryCounterexample`](models/PollingAbaBoundaryCounterexample.cfg)                   | `PollingChangeToken`           | `PollingDetectsTransientAbaMutation`: creating and deleting a file strictly between two polls leaves both observed states identical (`absent -> file -> absent`).                                                                  |

Reproduce any check from the repository root with the locked
[TLA+ toolchain](../../../../../../docs/engineering/workspaces.md#tla):

```powershell
$models = "src/public/lib/WebHdfs.Extensions.FileProviders/docs/design/models"
mise run tla:tlc -- -workers 1 -config "$models/PathAndProjectionDecisions.cfg" "$models/PathAndProjectionDecisions.tla"
mise run tla:tlc -- -workers 1 -config "$models/SnapshotAndStreamConsistency.cfg" "$models/SnapshotAndStreamConsistency.tla"
mise run tla:tlc -- -workers 1 -config "$models/PollingChangeToken.cfg" "$models/PollingChangeToken.tla"
mise run tla:tlc -- -workers 1 -config "$models/PollingChangeTokenLiveness.cfg" "$models/PollingChangeToken.tla"
```

## Native Assumptions and Omitted Details

- **De facto Hadoop non-directory and disabled-symlink messages:** Apache Hadoop
  does not declare `RemoteException.message` formatting as a versioned wire
  contract, but `FSPermissionChecker.checkTraverse` stably formats non-directory
  ancestor traversal as `<ancestorPath> (is not a directory)` and
  `FileSystemLinkResolver.resolve` stably formats disabled remote symlink
  resolution on HDFS-backed HttpFS as `Path <targetPath> contains a symlink and
symlink resolution is disabled (fs.client.resolve.remote.symlinks).` (with
  `HttpExceptionUtils` stripping trailing stack-trace lines on HttpFS). The
  single-RPC error classifier relies on exact first-line equality against those
  two resolved path patterns; if a non-Hadoop WebHDFS implementation formats
  either condition differently, the call fails closed as a fault rather than
  misclassifying real permission or storage failures as missing files.
- **HTTP, TLS, and gateway transport:** Standard `HttpMessageInvoker` /
  `HttpClient` pooling, URI escaping, TLS certificate validation, gateway prefix
  rewriting (such as Apache Knox), and authentication handlers (anonymous
  `user.name`, delegation tokens, SPNEGO, or OAuth2) belong to the transport
  configuration boundary. The abstract machine consumes only the resulting
  decoded WebHDFS status/stream outcomes.
- **Redirect trust boundary:** WebHDFS `OPEN` redirects from the NameNode to a
  DataNode URI. Credential forwarding rules (for example not leaking
  NameNode-scoped `Authorization` headers across untrusted origins while
  preserving WebHDFS query-token parameters emitted in `Location`) belong to the
  HTTP handler contract rather than filesystem state transitions.
- **Concrete runtime structures:** Whether the implementation represents
  flat-directory signatures via sorted incremental hashing (`SHA-256` /
  `XxHash128`) or structural maps, and how a single provider-scoped background
  timer multiplexes active polling tokens, are refinement choices constrained by
  the safety and progress properties above rather than prescribed by the model.
