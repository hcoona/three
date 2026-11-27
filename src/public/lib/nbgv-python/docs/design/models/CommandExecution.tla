-------------------------- MODULE CommandExecution --------------------------
EXTENDS Naturals

CallerDirs == {"manifest-repo-root", "manifest-repo-subdir", "external-git-repo", "non-git-dir"}
TargetDirs == CallerDirs \cup {"missing-dir"}
ExplicitMechanisms == {"standalone-bare", "standalone-rel", "standalone-abs", "dotnet-tool-run"}

VARIABLES op, callerCwd, configRoot, targetProjectDir, configSource,
          configuredMech, binState, autoHasPathNbgv, autoHasPathDotnet, manifestRestored, sdkCompatible
vars == <<op, callerCwd, configRoot, targetProjectDir, configSource,
          configuredMech, binState, autoHasPathNbgv, autoHasPathDotnet, manifestRestored, sdkCompatible>>

DirExists(d)               == d # "missing-dir"
IsGitRepo(d)               == d \in {"manifest-repo-root", "manifest-repo-subdir", "external-git-repo"}
HasAncestorDotnetManifest(d) == d \in {"manifest-repo-root", "manifest-repo-subdir"}

EffectiveMechanism ==
    IF configSource \in {"explicit", "env"} THEN configuredMech
    ELSE IF autoHasPathNbgv THEN "standalone-bare"
    ELSE IF autoHasPathDotnet THEN "dotnet-tool-run"
    ELSE "none"

RelativeCommandAnchor == IF configSource = "explicit" THEN configRoot ELSE callerCwd
TargetSubprocCwd      == IF op = "forward-inherit-cwd" THEN callerCwd ELSE targetProjectDir
EvaluatedGitDir       == IF op = "forward-inherit-cwd" THEN callerCwd ELSE targetProjectDir

\* Pure OS (`chdir` before `execve`) and `.NET` CLI (upward manifest/SDK walk from `cwd`) physics:
EvalSpawn(subprocCwd, anchorRel) ==
    LET relMisresolved == EffectiveMechanism = "standalone-rel" /\ ~anchorRel /\ subprocCwd # RelativeCommandAnchor
    IN  IF EffectiveMechanism = "none" THEN "tool-not-found"
        ELSE IF ~DirExists(subprocCwd) THEN "invalid-cwd"
        ELSE IF binState = "missing" \/ relMisresolved THEN "tool-not-found"
        ELSE IF binState = "no-exec-perm" THEN "tool-not-executable"
        ELSE IF EffectiveMechanism = "dotnet-tool-run" /\
                ~(HasAncestorDotnetManifest(subprocCwd) /\ manifestRestored /\ sdkCompatible) THEN
            "dotnet-tool-unavailable"
        ELSE IF ~DirExists(EvaluatedGitDir) THEN "invalid-project-dir"
        ELSE IF ~IsGitRepo(EvaluatedGitDir) THEN "not-a-git-repo"
        ELSE "ok"

Outcome == EvalSpawn(TargetSubprocCwd, TRUE)

Init ==
    /\ op \in {"get-version", "forward-inherit-cwd", "forward-explicit-cwd"}
    /\ callerCwd \in CallerDirs /\ configRoot \in {"manifest-repo-root", "external-git-repo"}
    /\ targetProjectDir \in TargetDirs /\ configSource \in {"explicit", "env", "auto"}
    /\ configuredMech \in ExplicitMechanisms /\ (configSource = "auto" => configuredMech = "standalone-bare")
    /\ binState \in {"runnable", "missing", "no-exec-perm"}
    /\ autoHasPathNbgv \in BOOLEAN /\ autoHasPathDotnet \in BOOLEAN
    /\ (configSource # "auto" => (~autoHasPathNbgv /\ ~autoHasPathDotnet))
    /\ manifestRestored \in BOOLEAN /\ sdkCompatible \in BOOLEAN
    /\ (EffectiveMechanism # "dotnet-tool-run" => (~manifestRestored /\ ~sdkCompatible))

Spec == Init /\ [][UNCHANGED vars]_vars

\* 1. Explicit/env `dotnet tool run nbgv` obeys the exact same `.NET` manifest/SDK rules as `auto`.
MechanismUniformityLaw ==
    (EffectiveMechanism = "dotnet-tool-run" /\ Outcome = "ok") =>
        HasAncestorDotnetManifest(TargetSubprocCwd) /\ manifestRestored /\ sdkCompatible

\* 2. Location-independent monorepo execution from ANY `callerCwd`.
MonorepoGetVersionSuccessLaw ==
    (op = "get-version" /\ HasAncestorDotnetManifest(targetProjectDir) /\
     EffectiveMechanism # "none" /\ binState = "runnable" /\
     (EffectiveMechanism = "dotnet-tool-run" => (manifestRestored /\ sdkCompatible))) =>
        Outcome = "ok"

\* 3. Failure-classification precision AND algebraic refutation of `callerCwd` / unanchored `./tools/nbgv`.
FailureAndShortcutLaw ==
    /\ (Outcome = "tool-not-found" <=>
          EffectiveMechanism = "none" \/ (DirExists(TargetSubprocCwd) /\ binState = "missing"))
    /\ ((EffectiveMechanism # "none" /\ ~DirExists(TargetSubprocCwd)) => Outcome = "invalid-cwd")
    /\ ((EffectiveMechanism # "none" /\ DirExists(TargetSubprocCwd) /\ binState = "no-exec-perm") =>
          Outcome = "tool-not-executable")
    /\ ((op = "get-version" /\ callerCwd = "non-git-dir" /\ HasAncestorDotnetManifest(targetProjectDir) /\
         EffectiveMechanism = "dotnet-tool-run" /\ binState = "runnable" /\ manifestRestored /\ sdkCompatible) =>
          EvalSpawn(callerCwd, TRUE) = "dotnet-tool-unavailable" /\ Outcome = "ok")
    /\ ((op = "get-version" /\ EffectiveMechanism = "standalone-rel" /\ binState = "runnable" /\
         IsGitRepo(targetProjectDir) /\ TargetSubprocCwd # RelativeCommandAnchor) =>
          EvalSpawn(TargetSubprocCwd, FALSE) = "tool-not-found" /\ Outcome = "ok")

\* Deliberately refuted conjecture: cross-repo `dotnet-tool-run` without target manifest.
CrossRepoWithoutTargetManifestSucceeds ==
    (op = "get-version" /\ callerCwd = "manifest-repo-root" /\ targetProjectDir = "external-git-repo" /\
     EffectiveMechanism = "dotnet-tool-run" /\ binState = "runnable" /\ manifestRestored /\ sdkCompatible) =>
        Outcome = "ok"
=============================================================================
