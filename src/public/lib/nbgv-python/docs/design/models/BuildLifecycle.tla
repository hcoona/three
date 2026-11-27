--------------------------- MODULE BuildLifecycle ---------------------------
EXTENDS Naturals

\* Static project and Hatchling environment configuration bundled as one record.
Configs == [
    layout           : {"repo-root", "monorepo-subdir"},
    sdistInOuterGit  : BOOLEAN,
    writesFile       : BOOLEAN,
    hookConfigured   : BOOLEAN,
    legacyWrite      : BOOLEAN,
    gitIgnored       : BOOLEAN,
    pathTraversed    : BOOLEAN,
    staticArtifact   : BOOLEAN,
    wheelStaticForce : BOOLEAN,
    otherEditableMap : {"empty", "other-only", "targets-version"}
]
ValidConfigs == {c \in Configs : c.writesFile => c.hookConfigured}

NoArtifact == [built |-> FALSE, builtAt |-> 0, metaVer |-> 0, fileVer |-> 0, hasGitIgnore |-> FALSE]

VARIABLES cfg, headCommit, diskFileVer, readMutated,
          preparedStatus, preparedMeta, editable, sdist, directWheel, sdistWheelStatus, sdistWheel
vars == <<cfg, headCommit, diskFileVer, readMutated,
          preparedStatus, preparedMeta, editable, sdist, directWheel, sdistWheelStatus, sdistWheel>>

\* ---------------------------------------------------------------------------
\* Hatchling 1.33.0 Platform Mechanics (`ProjectMetadata` & `WheelBuilder`)
\* - `ProjectMetadata`: trusts `self.root/PKG-INFO` unconditionally without calling
\*   `VersionSource`; otherwise calls `VersionSource`.
\* - `BuilderInterface`: invokes `BuildHook` ONLY when `[tool.hatch.build.hooks.<name>]`
\*   is configured (`c.hookConfigured`).
\* - `WheelBuilder`: copies `force_include` into `site-packages/` during editable builds
\*   whenever `force_include_editable` is empty!
\* ---------------------------------------------------------------------------
IncludedByHatchling(fileVer, traversed, ignored, staticArt, hookArt, forceInc) ==
    IF fileVer = 0 THEN 0
    ELSE IF forceInc \/ (traversed /\ (~ignored \/ staticArt \/ hookArt)) THEN fileVer ELSE 0

EvalMetadataWith(c, rule, tree) ==
    IF tree.hasRootPkgInfo THEN
        [ok |-> TRUE, ver |-> tree.pkgInfoVer, diskAfter |-> tree.diskVer]
    ELSE IF c.legacyWrite /\ rule # "source-side-effect" THEN
        [ok |-> FALSE, ver |-> 0, diskAfter |-> tree.diskVer]
    ELSE IF rule = "self-dot-git" /\ ~tree.hasSelfDotGit THEN
        [ok |-> FALSE, ver |-> 0, diskAfter |-> tree.diskVer]
    ELSE IF tree.projectGitOwned /\ tree.hasNbgv THEN
        [ok        |-> TRUE,
         ver       |-> tree.gitCommit,
         diskAfter |-> IF rule = "source-side-effect" /\ (c.writesFile \/ c.legacyWrite) THEN tree.gitCommit ELSE tree.diskVer]
    ELSE IF rule = "trust-stale-disk" /\ tree.diskVer # 0 THEN
        [ok |-> TRUE, ver |-> tree.diskVer, diskAfter |-> tree.diskVer]
    ELSE [ok |-> FALSE, ver |-> 0, diskAfter |-> tree.diskVer]

\* Target `BuildHook` policy (`rule = "target"`), contrasted algebraically in
\* `AlternativeBoundaryLaw` against 5 tempting-but-broken rules (`"source-side-effect"`,
\* `"self-dot-git"`, `"reject-outer-git"`, `"trust-stale-disk"`, `"editable-force"`).
EvalHookRule(c, rule, mode, tree, diskVer) ==
    IF ~c.hookConfigured \/ rule = "source-side-effect" THEN
        [ok |-> TRUE, diskAfter |-> diskVer, art |-> FALSE, force |-> FALSE]
    ELSE IF rule = "reject-outer-git" /\ tree.hasAncestorGit /\ tree.hasRootPkgInfo THEN
        [ok |-> FALSE, diskAfter |-> diskVer, art |-> FALSE, force |-> FALSE]
    ELSE IF tree.hasRootPkgInfo THEN
        IF c.writesFile /\ diskVer = 0 THEN
            [ok |-> FALSE, diskAfter |-> 0, art |-> FALSE, force |-> FALSE]
        ELSE [ok        |-> TRUE,
              diskAfter |-> diskVer,
              art       |-> c.writesFile,
              force     |-> c.writesFile /\ (mode = "standard" \/ rule = "editable-force")]
    ELSE IF rule = "self-dot-git" /\ ~tree.hasSelfDotGit THEN
        [ok |-> FALSE, diskAfter |-> diskVer, art |-> FALSE, force |-> FALSE]
    ELSE IF tree.projectGitOwned /\ tree.hasNbgv THEN
        [ok        |-> TRUE,
         diskAfter |-> IF c.writesFile THEN tree.gitCommit ELSE diskVer,
         art       |-> c.writesFile,
         force     |-> c.writesFile /\ (mode = "standard" \/ rule = "editable-force")]
    ELSE IF rule = "trust-stale-disk" /\ diskVer # 0 THEN
        [ok |-> TRUE, diskAfter |-> diskVer, art |-> c.writesFile, force |-> c.writesFile]
    ELSE [ok |-> FALSE, diskAfter |-> diskVer, art |-> FALSE, force |-> FALSE]

EvalBuildWith(c, rule, target, mode, tree) ==
    LET meta == EvalMetadataWith(c, rule, tree)
        hook == IF ~meta.ok THEN [ok |-> FALSE, diskAfter |-> meta.diskAfter, art |-> FALSE, force |-> FALSE]
                ELSE EvalHookRule(c, rule, mode, tree, meta.diskAfter)
        stdForce  == ((target = "wheel") /\ c.wheelStaticForce) \/ hook.force
        editForce == CASE c.otherEditableMap = "targets-version" -> TRUE
                       [] c.otherEditableMap = "other-only"      -> FALSE
                       [] OTHER                                  -> stdForce
        fileVer   == IncludedByHatchling(hook.diskAfter, tree.pathTraversed, tree.hasGitIgnore,
                                         c.staticArtifact, hook.art, stdForce)
    IN  IF ~hook.ok THEN [status |-> "failed", diskAfter |-> meta.diskAfter, siteCopy |-> 0, artifact |-> NoArtifact]
        ELSE [status    |-> "ok",
              diskAfter |-> hook.diskAfter,
              siteCopy  |-> IF mode = "editable" /\ editForce THEN hook.diskAfter ELSE 0,
              artifact  |-> [built        |-> TRUE,
                             builtAt      |-> tree.originCommit,
                             metaVer      |-> meta.ver,
                             fileVer      |-> IF c.writesFile \/ c.legacyWrite THEN fileVer ELSE 0,
                             hasGitIgnore |-> tree.hasGitIgnore]]

EvalMetadata(tree)            == EvalMetadataWith(cfg, "target", tree)
EvalBuild(target, mode, tree) == EvalBuildWith(cfg, "target", target, mode, tree)

LiveTree == [
    originCommit    |-> headCommit,
    hasSelfDotGit   |-> (cfg.layout = "repo-root"),
    hasAncestorGit  |-> TRUE,
    projectGitOwned |-> TRUE,
    hasNbgv         |-> TRUE,
    hasRootPkgInfo  |-> FALSE,
    gitCommit       |-> headCommit,
    pkgInfoVer      |-> 0,
    diskVer         |-> diskFileVer,
    hasGitIgnore    |-> cfg.gitIgnored,
    pathTraversed   |-> cfg.pathTraversed
]

UnpackedSdistTree(a) == [
    originCommit    |-> a.builtAt,
    hasSelfDotGit   |-> FALSE,
    hasAncestorGit  |-> cfg.sdistInOuterGit,
    projectGitOwned |-> FALSE,
    hasNbgv         |-> FALSE,
    hasRootPkgInfo  |-> TRUE,
    gitCommit       |-> 0,
    pkgInfoVer      |-> a.metaVer,
    diskVer         |-> a.fileVer,
    hasGitIgnore    |-> a.hasGitIgnore,
    pathTraversed   |-> cfg.pathTraversed
]

Init ==
    /\ cfg \in ValidConfigs /\ headCommit = 1 /\ diskFileVer = 0 /\ readMutated = FALSE
    /\ preparedStatus = "unrun" /\ preparedMeta = [prepared |-> FALSE, commit |-> 0, metaVer |-> 0]
    /\ editable = [installed |-> FALSE, metaVer |-> 0, siteCopyVer |-> 0]
    /\ sdist = NoArtifact /\ directWheel = NoArtifact
    /\ sdistWheelStatus = "unbuilt" /\ sdistWheel = NoArtifact

QueryMetadata ==
    LET m == EvalMetadata(LiveTree)
    IN  /\ diskFileVer' = m.diskAfter
        /\ preparedStatus' = IF m.ok THEN "ok" ELSE "failed"
        /\ preparedMeta' = IF m.ok THEN [prepared |-> TRUE, commit |-> headCommit, metaVer |-> m.ver] ELSE preparedMeta
        /\ readMutated' = (readMutated \/ (m.diskAfter # diskFileVer))
        /\ UNCHANGED <<cfg, headCommit, editable, sdist, directWheel, sdistWheelStatus, sdistWheel>>

BuildSdist ==
    LET r == EvalBuild("sdist", "standard", LiveTree)
    IN  /\ r.status = "ok" /\ diskFileVer' = r.diskAfter /\ sdist' = r.artifact
        /\ UNCHANGED <<cfg, headCommit, readMutated, preparedStatus, preparedMeta, editable, directWheel, sdistWheelStatus, sdistWheel>>

BuildWheel ==
    LET r == EvalBuild("wheel", "standard", LiveTree)
    IN  /\ r.status = "ok" /\ diskFileVer' = r.diskAfter /\ directWheel' = r.artifact
        /\ UNCHANGED <<cfg, headCommit, readMutated, preparedStatus, preparedMeta, editable, sdist, sdistWheelStatus, sdistWheel>>

BuildWheelFromSdist ==
    /\ sdist.built
    /\ LET r == EvalBuild("wheel", "standard", UnpackedSdistTree(sdist))
       IN  /\ sdistWheelStatus' = r.status
           /\ sdistWheel' = IF r.status = "ok" THEN r.artifact ELSE NoArtifact
    /\ UNCHANGED <<cfg, headCommit, diskFileVer, readMutated, preparedStatus, preparedMeta, editable, sdist, directWheel>>

BuildEditable ==
    LET r == EvalBuild("wheel", "editable", LiveTree)
    IN  /\ r.status = "ok" /\ diskFileVer' = r.diskAfter
        /\ editable' = [installed |-> TRUE, metaVer |-> r.artifact.metaVer, siteCopyVer |-> r.siteCopy]
        /\ UNCHANGED <<cfg, headCommit, readMutated, preparedStatus, preparedMeta, sdist, directWheel, sdistWheelStatus, sdistWheel>>

AdvanceCommit ==
    /\ headCommit = 1 /\ headCommit' = 2
    /\ UNCHANGED <<cfg, diskFileVer, readMutated, preparedStatus, preparedMeta, editable, sdist, directWheel, sdistWheelStatus, sdistWheel>>

Next == QueryMetadata \/ BuildSdist \/ BuildWheel \/ BuildWheelFromSdist \/ BuildEditable \/ AdvanceCommit
Spec == Init /\ [][Next]_vars

EditableObservedFileVer == IF editable.siteCopyVer # 0 THEN editable.siteCopyVer ELSE diskFileVer

\* 1. Read-only metadata, `legacyWrite` checkout rejection, and outer-Git unpacked-`sdist` safety.
ReadOnlyAndGitFreeSdistLaw ==
    /\ ~readMutated /\ sdistWheelStatus # "failed"
    /\ (~cfg.legacyWrite => preparedStatus # "failed")
    /\ (cfg.legacyWrite => preparedStatus # "ok" /\ ~sdist.built /\ ~directWheel.built /\ ~editable.installed)
    /\ ((preparedMeta.prepared /\ directWheel.built /\ preparedMeta.commit = directWheel.builtAt) =>
          directWheel.metaVer = preparedMeta.metaVer)

\* 2. Direct wheel == unpacked-sdist wheel equivalence.
DirectVsSdistWheelEquivalence ==
    (directWheel.built /\ sdistWheel.built /\ directWheel.builtAt = sdistWheel.builtAt) =>
        directWheel.metaVer = sdistWheel.metaVer /\ directWheel.fileVer = sdistWheel.fileVer

\* 3. Archive provenance, `prepare_metadata`/`build_wheel` `PKG-INFO` symmetry, and fail-closed snapshots.
ProvenanceAndArchiveIntegrityLaw ==
    LET gitlessSnap    == [LiveTree EXCEPT !.hasSelfDotGit = FALSE, !.hasAncestorGit = FALSE,
                                           !.projectGitOwned = FALSE, !.hasNbgv = FALSE]
        corruptSdist   == UnpackedSdistTree([NoArtifact EXCEPT !.built = TRUE, !.builtAt = 1, !.metaVer = 1])
        validRootSdist == [UnpackedSdistTree([NoArtifact EXCEPT !.built = TRUE, !.builtAt = 1, !.metaVer = 1, !.fileVer = 1])
                           EXCEPT !.hasSelfDotGit = TRUE]
    IN  /\ (sdist.built       => sdist.metaVer = sdist.builtAt /\ (cfg.writesFile => sdist.fileVer = sdist.builtAt))
        /\ (directWheel.built => directWheel.metaVer = directWheel.builtAt /\ (cfg.writesFile => directWheel.fileVer = directWheel.builtAt))
        /\ (sdistWheel.built  => sdistWheel.metaVer = sdistWheel.builtAt /\ (cfg.writesFile => sdistWheel.fileVer = sdistWheel.builtAt))
        /\ EvalBuild("wheel", "standard", gitlessSnap).status = "failed"
        /\ (EvalMetadata(validRootSdist).ok <=> EvalBuild("wheel", "standard", validRootSdist).status = "ok")
        /\ (cfg.writesFile => EvalBuild("wheel", "standard", corruptSdist).status = "failed")

\* 4. Editable `site-packages/` isolation.
EditableNoSitePackagesShadowLaw ==
    (editable.installed /\ cfg.otherEditableMap # "targets-version" /\
     ~(cfg.otherEditableMap = "empty" /\ cfg.wheelStaticForce)) =>
        editable.siteCopyVer = 0 /\ EditableObservedFileVer = diskFileVer

\* 5. Algebraic refutation of the 6 tempting shortcuts on the same state space (zero extra `.cfg`s!):
AlternativeBoundaryLaw ==
    LET cNoHook      == [cfg EXCEPT !.hookConfigured = FALSE]
        gitlessStale == [LiveTree EXCEPT !.hasSelfDotGit = FALSE, !.hasAncestorGit = FALSE,
                                         !.projectGitOwned = FALSE, !.hasNbgv = FALSE, !.diskVer = 1]
    IN  /\ ((cfg.writesFile /\ ~cfg.legacyWrite /\ cfg.gitIgnored /\
             ~cfg.staticArtifact /\ ~cfg.wheelStaticForce /\ diskFileVer = 0) =>
              EvalBuildWith(cNoHook, "target", "wheel", "standard", LiveTree).artifact.fileVer = 0)
        /\ ((cfg.writesFile /\ diskFileVer # headCommit) =>
              EvalMetadataWith(cfg, "source-side-effect", LiveTree).diskAfter # diskFileVer)
        /\ ((~cfg.legacyWrite /\ cfg.layout = "monorepo-subdir") =>
              EvalBuildWith(cfg, "self-dot-git", "wheel", "standard", LiveTree).status = "failed")
        /\ ((sdist.built /\ cfg.hookConfigured /\ cfg.sdistInOuterGit) =>
              EvalBuildWith(cfg, "reject-outer-git", "wheel", "standard", UnpackedSdistTree(sdist)).status = "failed")
        /\ ((~cfg.legacyWrite /\ cfg.writesFile) =>
              EvalBuildWith(cfg, "trust-stale-disk", "wheel", "standard", gitlessStale).artifact.fileVer = 1)
        /\ ((~cfg.legacyWrite /\ cfg.writesFile /\ cfg.otherEditableMap = "empty") =>
              EvalBuildWith(cfg, "editable-force", "wheel", "editable", LiveTree).siteCopy # 0)

\* Deliberately refuted temporal conjecture (PEP 660 frozen `.dist-info` after `AdvanceCommit`):
EditableMetadataTracksCommitWithoutReinstall ==
    (editable.installed /\ cfg.writesFile /\ diskFileVer # 0) =>
        editable.metaVer = EditableObservedFileVer
=============================================================================
