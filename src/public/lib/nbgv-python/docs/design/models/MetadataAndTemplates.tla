------------------------ MODULE MetadataAndTemplates ------------------------
EXTENDS NbgvPep440Rules

SelectedFields == {
    "SemVer2", "SemVer1", "NuGetPackageVersion",
    "SimpleVersion", "Version", "AssemblyInformationalVersion", "MissingField"
}
MetadataRecipes  == {"none", "dev-h", "beta-1", "beta-h", "beta-1-dev-h"}
CommitPrefixOpts == {"null-default-g", "u", "invalid-empty"}

VARIABLES coreShape, preRecipe, commitPrefixOpt, selectedField,
          commit, publicRelease, epochCfg, hasBuildMeta

vars == <<coreShape, preRecipe, commitPrefixOpt, selectedField,
          commit, publicRelease, epochCfg, hasBuildMeta>>

Payload   == EvalNbgvJsonWithPrefix(coreShape, preRecipe, 2, 0, publicRelease, commit, hasBuildMeta, commitPrefixOpt)
FullSha   == IF commit = "digit-sha" THEN "0481029000000000000000000000000000000000"
             ELSE "ccf1759a04affcd5f4e0d751b8085485fe5c18e2"
RawPreStr ==
    IF ~Payload.versionFileFound THEN ""
    ELSE CASE preRecipe = "none"   -> ""       [] preRecipe = "dev-h"        -> "dev.2"
           [] preRecipe = "beta-1" -> "beta.1" [] preRecipe = "beta-h"       -> "beta.2"
           [] OTHER                -> "beta.1.dev.2"

\* Minimal sufficient JSON key set covering `Version`/`version` alias collision,
\* `workflow-delivery/Native/Python/packages.py:211-215` (`GitCommitId`, `PublicRelease`),
\* and `nbgv` tuple fields.
RawJsonMap(ngPrec) == [
    Version                          |-> StrV("dotnet-version"),
    SimpleVersion                    |-> StrV("simple-version"),
    SemVer2                          |-> StrV("semver2"),
    SemVer1                          |-> StrV("semver1"),
    NuGetPackageVersion              |-> StrV(IF ngPrec = "build" THEN "nuget-build" ELSE "nuget-rev"),
    AssemblyInformationalVersion     |-> StrV("info-version"),
    VersionMajor                     |-> IntV(Payload.versionMajor),
    BuildNumber                      |-> IntV(Payload.buildNumber),
    VersionRevision                  |-> IntV(Payload.versionRevision),
    PublicRelease                    |-> BoolV(Payload.publicRelease),
    PrereleaseVersionNoLeadingHyphen |-> StrV(RawPreStr),
    GitCommitId                      |-> StrV(FullSha),
    GitCommitIdShort                 |-> StrV(Payload.commitShort)
]

UnpadSemVer1Pre(ts) == [i \in 1..Len(ts) |->
    IF ts[i].kind = "str" /\ ts[i].s \in PaddedDigitStrings THEN IntV(DigitStrVal(ts[i].s)) ELSE ts[i]]

StripTrailingPrefixedCommit(ts, prefix, hex) ==
    LET pTag == PrefixedCommitId(prefix, hex)
    IN  IF Len(ts) >= 1 /\ ts[Len(ts)] = StrV(pTag) THEN SubSeq(ts, 1, Len(ts) - 1) ELSE ts

ProjectFieldAt(f, pubState, ep, ngSv, ngPrec) ==
    LET pay == EvalNbgvJsonWithPrefix(coreShape, preRecipe, 2, 0, pubState, commit, hasBuildMeta, commitPrefixOpt)
        ng  == EvalNuGetField(pay, ngSv, ngPrec)
    IN  IF ~pay.versionFileFound THEN Reject("version-file-not-found")
        ELSE CASE f = "SemVer2" -> TargetProject(pay, ep, "local")
               [] f = "NuGetPackageVersion" ->
                    ProjectVersionFromComponents(
                        pay.versionFileFound, ng.core,
                        UnpadSemVer1Pre(StripTrailingPrefixedCommit(ng.pre, pay.commitPrefix, pay.commitShort)),
                        ng.build, pay.publicRelease, pay.versionHeight, pay.versionHeightOffset,
                        pay.heightPosition, pay.commitPrefix, pay.commitShort, ep, "local")
               [] f = "SemVer1" ->
                    ProjectVersionFromComponents(
                        pay.versionFileFound, pay.semVer1.core, UnpadSemVer1Pre(pay.semVer1.pre), <<>>,
                        pay.publicRelease, pay.versionHeight, pay.versionHeightOffset,
                        pay.heightPosition, "", pay.commitShort, ep, "local")
               [] f = "SimpleVersion" ->
                    IF ~pubState THEN Reject("simple-lacks-lowering")
                    ELSE FinalPep440(ResolveEpoch(ep), pay.simpleVersion)
               [] f = "Version" ->
                    IF ~pubState THEN Reject("dotnet-lacks-lowering")
                    ELSE FinalPep440(ResolveEpoch(ep), pay.version)
               [] f = "AssemblyInformationalVersion" ->
                    IF ~pubState THEN Reject("info-indistinguishable")
                    ELSE ProjectVersionFromComponents(
                             pay.versionFileFound, pay.infoVersion.core, pay.prereleaseNoHyphen,
                             pay.infoVersion.build, TRUE, pay.versionHeight, pay.versionHeightOffset,
                             pay.heightPosition, pay.commitPrefix, pay.commitShort, ep, "local")
               [] OTHER -> Reject("missing-field")

\* Structured component tuples (delegated to `packaging.version.Version` in Python,
\* modeled here without hand-rolled string concatenation):
PreElem(pre, dev) ==
    IF pre.present /\ dev >= 0 THEN [kind |-> "pre-dev", n |-> pre.num * 10 + dev, s |-> pre.tag]
    ELSE IF pre.present THEN [kind |-> "pre", n |-> pre.num, s |-> pre.tag]
    ELSE [kind |-> "dev-only", n |-> dev, s |-> "dev"]

SegCode(t) == IF t.kind = "int" THEN 100 + t.n ELSE StrRank(t.s)
LocalElem(loc) ==
    [kind |-> "local",
     n    |-> IF Len(loc) = 1 THEN SegCode(loc[1]) ELSE SegCode(loc[1]) * 100 + SegCode(loc[2]),
     s    |-> ""]

BuildPep440Tuple(proj, epochOpt) ==
    LET incEp == epochOpt = "true" \/ (epochOpt = "auto" /\ proj.epoch # 0)
    IN  (IF incEp THEN <<IntV(proj.epoch)>> ELSE <<>>)
        \o [i \in 1..Len(proj.release) |-> IntV(proj.release[i])]
        \o (IF proj.pre.present THEN <<PreElem(proj.pre, -1)>> ELSE <<>>)
        \o (IF proj.dev >= 0 THEN <<StrV("dev"), IntV(proj.dev)>> ELSE <<>>)
        \o (IF proj.local # <<>> THEN <<StrV("+"), LocalElem(proj.local)>> ELSE <<>>)

NormalizedPreFor(preTokens) ==
    LET p == ParseStrictPep440Pre(preTokens)
    IN  IF preTokens = <<>> \/ ~p.ok THEN SkipV ELSE PreElem(p.pre, p.dev)

BuildNbgvTuple(normPre, coerceDigits) ==
    LET jmap   == RawJsonMap("build")
        fields == <<"VersionMajor", "BuildNumber", "VersionRevision",
                    "PublicRelease", "PrereleaseVersionNoLeadingHyphen", "GitCommitIdShort">>
        elem(f) ==
            IF f = "PrereleaseVersionNoLeadingHyphen" /\ normPre THEN
                NormalizedPreFor(Payload.prereleaseNoHyphen)
            ELSE IF jmap[f] = StrV("") THEN SkipV
            ELSE IF coerceDigits /\ jmap[f] = StrV("0481029") THEN IntV(481029)
            ELSE jmap[f]
    IN  SelectSeq([i \in 1..Len(fields) |-> elem(fields[i])], LAMBDA e : e # SkipV)

TemplateKeys == (DOMAIN RawJsonMap("build")) \cup {"version", "normalized_version", "version_tuple"}
AliasesFirstDict(ngPrec) ==
    LET jmap == RawJsonMap(ngPrec)
    IN  [k \in TemplateKeys |->
            CASE k = "version"            -> jmap[selectedField]
              [] k = "normalized_version" -> StrV("canonical-pep440")
              [] k = "version_tuple"      -> StrV("tuple-expr")
              [] OTHER                    -> jmap[k]]
AliasesLastDict(ngPrec) == [AliasesFirstDict(ngPrec) EXCEPT !["version"] = RawJsonMap(ngPrec)["Version"]]

Init ==
    /\ coreShape \in CoreShapes /\ preRecipe \in MetadataRecipes
    /\ commitPrefixOpt \in CommitPrefixOpts /\ selectedField \in SelectedFields
    /\ commit \in Commits /\ publicRelease \in BOOLEAN
    /\ epochCfg \in EpochConfigs /\ hasBuildMeta \in BOOLEAN

Spec == Init /\ [][UNCHANGED vars]_vars

\* 1. Multi-field projection, prefix handling, 4-part NuGet padding, and commit-first InfoVersion.
FieldProjectionFidelityLaw ==
    LET ngDomain == IF selectedField = "NuGetPackageVersion" THEN {1, 2} \X {"build", "revision"}
                    ELSE {<<2, "build">>}
        expectedPrefTag  == PrefixedCommitId(Payload.commitPrefix, Payload.commitShort)
        metaSuffix       == IF hasBuildMeta THEN <<StrV("meta")>> ELSE <<>>
        expectedInfoHead == IF commit = "digit-sha" THEN IntV(481029) ELSE StrV("ccf1759")
        devSaturated     == preRecipe \in {"dev-h", "beta-1-dev-h"}
    IN  \A ngPair \in ngDomain :
            LET ngSv   == ngPair[1]
                ngPrec == ngPair[2]
                proj   == ProjectFieldAt(selectedField, publicRelease, epochCfg, ngSv, ngPrec)
                pubC   == ProjectFieldAt(selectedField, TRUE, epochCfg, ngSv, ngPrec)
            IN  /\ (proj.ok <=>
                      /\ Payload.versionFileFound /\ selectedField # "MissingField"
                      /\ (selectedField \in {"SemVer1", "SimpleVersion", "Version", "AssemblyInformationalVersion"} => publicRelease)
                      /\ ((selectedField \in {"SemVer2", "NuGetPackageVersion"} /\ ~publicRelease) => ~devSaturated))
                /\ (proj.ok => Pep440Valid(proj))
                /\ ((~publicRelease /\ proj.ok /\ pubC.ok) => Pep440Less(proj, pubC))
                /\ ((proj.ok /\ selectedField = "NuGetPackageVersion" /\ ngPrec = "build") =>
                      /\ (ngSv = 2 => proj = TargetProject(Payload, epochCfg, "local"))
                      /\ ((ngSv = 1 /\ ~hasBuildMeta) => proj = TargetProject(Payload, epochCfg, "local"))
                      /\ (ngSv = 1 => proj.local = IF publicRelease THEN <<>> ELSE <<StrV(expectedPrefTag)>>))
                /\ ((proj.ok /\ selectedField = "NuGetPackageVersion" /\ ngPrec = "revision") =>
                      /\ Len(proj.release) = 4 /\ (~publicRelease => proj.dev = 0)
                      /\ (Payload.heightPosition = PosRevision => proj.release[4] = Payload.versionHeight))
                /\ ((proj.ok /\ selectedField = "AssemblyInformationalVersion") =>
                      /\ (Len(proj.release) = 4 <=> Payload.heightPosition = PosRevision)
                      /\ ((coreShape = "two-part" /\ Payload.heightPosition = PosPrerelease) =>
                            proj.release = <<1, 2, 0>> /\ Payload.simpleVersion = <<1, 2>>)
                      /\ (coreShape = "four-part" => proj.release = <<1, 2, 3>>)
                      /\ proj.local = (<<expectedInfoHead>> \o metaSuffix))
                /\ ((proj.ok /\ selectedField = "SemVer2" /\ ~publicRelease) =>
                      proj.local = (<<StrV(expectedPrefTag)>> \o metaSuffix))

\* 2. Zero-epoch extensional equivalence across all fields.
ZeroEpochExtensionalityLaw ==
    epochCfg = "zero" =>
        \A ngSv \in {1, 2}, ngPrec \in {"build", "revision"} :
            ProjectFieldAt(selectedField, publicRelease, "zero", ngSv, ngPrec) =
            ProjectFieldAt(selectedField, publicRelease, "unset", ngSv, ngPrec)

\* 3. Dictionary precedence, `pep440` & orthogonal `nbgv` tuple fidelity, AND
\*    algebraic refutation of `AliasesLastDict` and digit-string coercion!
TemplateAndTupleLaw ==
    \A ngPrec \in (IF selectedField = "NuGetPackageVersion" THEN {"build", "revision"} ELSE {"build"}) :
        LET proj == ProjectFieldAt(selectedField, publicRelease, epochCfg, 2, ngPrec)
            jmap == RawJsonMap(ngPrec)
            dOk  == AliasesFirstDict(ngPrec)
            dBad == AliasesLastDict(ngPrec)
        IN  proj.ok =>
                /\ dOk["version"] = jmap[selectedField] /\ dOk["Version"] = jmap["Version"]
                /\ dOk["GitCommitId"] = StrV(FullSha) /\ dOk["PublicRelease"] = BoolV(Payload.publicRelease)
                /\ (selectedField # "Version" => dBad["version"] # jmap[selectedField])
                /\ \A epochOpt \in {"auto", "true", "false"} :
                      LET pTup   == BuildPep440Tuple(proj, epochOpt)
                          hasEp  == epochOpt = "true" \/ (epochOpt = "auto" /\ proj.epoch # 0)
                          epOff  == IF hasEp THEN 1 ELSE 0
                          preOff == epOff + Len(proj.release)
                          devOff == preOff + (IF proj.pre.present THEN 1 ELSE 0)
                          locOff == devOff + (IF proj.dev >= 0 THEN 2 ELSE 0)
                      IN  /\ Len(pTup) = locOff + (IF proj.local # <<>> THEN 2 ELSE 0)
                          /\ (hasEp => pTup[1] = IntV(proj.epoch))
                          /\ \A i \in 1..Len(proj.release) : pTup[epOff + i] = IntV(proj.release[i])
                          /\ (proj.pre.present => pTup[preOff + 1] = PreElem(proj.pre, -1))
                          /\ (proj.dev >= 0 => pTup[devOff + 1] = StrV("dev") /\ pTup[devOff + 2] = IntV(proj.dev))
                          /\ (proj.local # <<>> =>
                                /\ pTup[locOff + 1] = StrV("+") /\ pTup[locOff + 2] = LocalElem(proj.local)
                                /\ ((selectedField \in {"SemVer2", "NuGetPackageVersion"} /\ ~publicRelease) =>
                                      LET otherPay  == EvalNbgvJsonWithPrefix(
                                                           coreShape, preRecipe, 2, 0, FALSE,
                                                           IF commit = "digit-sha" THEN "alpha-sha" ELSE "digit-sha",
                                                           hasBuildMeta, commitPrefixOpt)
                                          otherProj == TargetProject(otherPay, epochCfg, "local")
                                          otherTup  == BuildPep440Tuple(otherProj, epochOpt)
                                      IN  otherProj.ok => otherTup[Len(otherTup)] # pTup[Len(pTup)])
                                /\ ((selectedField = "AssemblyInformationalVersion" /\ hasBuildMeta /\ commit = "digit-sha") =>
                                      proj.local = <<IntV(481029), StrV("meta")>> /\
                                      pTup[locOff + 2] = LocalElem(<<IntV(481029), StrV("meta")>>)))
                /\ \A normPre \in BOOLEAN :
                      LET nTup == BuildNbgvTuple(normPre, FALSE)
                          badN == BuildNbgvTuple(normPre, TRUE)
                      IN  /\ nTup[Len(nTup)] = StrV(Payload.commitShort)
                          /\ (commit = "digit-sha" => badN[Len(badN)] = IntV(481029) /\ badN[Len(badN)] # nTup[Len(nTup)])
                          /\ nTup[3] = IntV(Payload.versionRevision)
                          /\ Len(nTup) = IF preRecipe = "none" THEN 5 ELSE 6
                          /\ ((~normPre /\ preRecipe # "none") => nTup[5] = StrV(RawPreStr))
                          /\ ((normPre /\ preRecipe # "none") =>
                                nTup[5] = NormalizedPreFor(Payload.prereleaseNoHyphen) /\ nTup[5].kind # "str")
=============================================================================
