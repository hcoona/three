--------------------------- MODULE NbgvPep440Rules ---------------------------
EXTENDS Integers, Sequences, FiniteSets

\* Representative short hashes: `"alpha-sha"` (`"ccf1759"`) vs `"digit-sha"` (`"0481029"`,
\* all-digit hex with leading zero; ASCII-smaller than `"ccf1759"`).
Commits         == {"alpha-sha", "digit-sha"}
RawHeights      == {0, 1, 2}
HeightOffsets   == {-2, -1, 0, 1}
EpochConfigs    == {"unset", "zero", "two"}
ChannelPolicies == {"local", "public-index", "official-release"}
CoreShapes      == {"two-part", "three-part", "four-part"}

\* NBGV `VersionOptions.Version.VersionHeightPosition` (`-1` denotes JSON `null`):
PosNone == -1    PosBuild == 2    PosRevision == 3    PosPrerelease == 4

CanonicalHomomorphicRecipes == {"none", "alpha-0", "alpha-h", "beta-h", "beta-1", "rc-h"}
AdmittedRecipes == CanonicalHomomorphicRecipes \cup {"dev-h", "beta-1-dev-h"}
PreRecipes == AdmittedRecipes \cup
              {"alpha-bare", "pre-h", "preview-h", "beta-1-h", "num-1", "post-1", "custom-h"}

\* Unified typed values shared across version tokens, JSON fields, and template tuples.
IntV(n)  == [kind |-> "int",  n |-> n,                  s |-> ""]
BoolV(b) == [kind |-> "bool", n |-> IF b THEN 1 ELSE 0, s |-> ""]
StrV(s)  == [kind |-> "str",  n |-> 0,                  s |-> s]
SkipV    == [kind |-> "skip", n |-> 0,                  s |-> ""]

AsciiOrder == <<
    "-1", "-2", "0000", "0001", "0002", "0003", "0481029",
    "a", "alpha", "b", "beta", "c", "ccf1759", "ci", "dev",
    "g0481029", "gccf1759", "meta", "post", "pre", "preview", "rc",
    "u0481029", "uccf1759"
>>
ValidStrings           == {AsciiOrder[i] : i \in 1..Len(AsciiOrder)}
PaddedDigitStrings     == {"0000", "0001", "0002", "0003"}
DigitStrings           == PaddedDigitStrings \cup {"0481029"}
NonNumericLocalStrings == ValidStrings \ (DigitStrings \cup {"-1", "-2"})
StrRank(s) ==
    CASE s = "-1" -> 1        [] s = "-2" -> 2        [] s = "0000" -> 3      [] s = "0001" -> 4
      [] s = "0002" -> 5      [] s = "0003" -> 6      [] s = "0481029" -> 7   [] s = "a" -> 8
      [] s = "alpha" -> 9     [] s = "b" -> 10        [] s = "beta" -> 11     [] s = "c" -> 12
      [] s = "ccf1759" -> 13  [] s = "ci" -> 14       [] s = "dev" -> 15      [] s = "g0481029" -> 16
      [] s = "gccf1759" -> 17 [] s = "meta" -> 18     [] s = "post" -> 19     [] s = "pre" -> 20
      [] s = "preview" -> 21  [] s = "rc" -> 22       [] s = "u0481029" -> 23 [] s = "uccf1759" -> 24

CommitRawHex(c) == IF c = "digit-sha" THEN "0481029" ELSE "ccf1759"
CommitRev(c)    == IF c = "digit-sha" THEN 18473 ELSE 52465
PaddedIntStr(n) == CASE n = 0 -> "0000" [] n = 1 -> "0001" [] n = 2 -> "0002" [] OTHER -> "0003"
DigitStrVal(s)  == CASE s = "0000" -> 0 [] s = "0001" -> 1 [] s = "0002" -> 2 [] s = "0003" -> 3 [] OTHER -> 481029

CanonicalPep440LocalSeg(t) ==
    IF t.kind = "str" /\ t.s \in DigitStrings THEN IntV(DigitStrVal(t.s)) ELSE t

RECURSIVE IntSeqLess(_, _), TrimTrailingZeros(_)
IntSeqLess(s1, s2) ==
    IF s1 = <<>> THEN s2 # <<>>
    ELSE IF s2 = <<>> THEN FALSE
    ELSE IF Head(s1) # Head(s2) THEN Head(s1) < Head(s2)
    ELSE IntSeqLess(Tail(s1), Tail(s2))

TrimTrailingZeros(rel) ==
    IF rel = <<>> THEN <<>>
    ELSE IF rel[Len(rel)] = 0 THEN TrimTrailingZeros(SubSeq(rel, 1, Len(rel) - 1))
    ELSE rel

\* ---------------------------------------------------------------------------
\* SemVer 2.0 & PEP 440 Orderings via Lexicographic Integer-Key Reduction
\* Notice the exact structural contrast:
\* - SemVer prerelease: `int < str` (`0 < 1`), and non-empty `pre < <<>>` (polarity flip);
\* - PEP 440 `+local`:  `str < int` (`0 < 1`), and `<<>> < non-empty` (prefix order).
\* ---------------------------------------------------------------------------
SemVerPreKey(p) == [i \in 1..(2 * Len(p)) |->
    LET t == p[(i + 1) \div 2]
    IN  IF i % 2 = 1 THEN (IF t.kind = "int" THEN 0 ELSE 1)
        ELSE (IF t.kind = "int" THEN t.n ELSE StrRank(t.s))]

SemVerLess(u, v) ==
    IF u.core # v.core THEN IntSeqLess(u.core, v.core)
    ELSE IF (u.pre = <<>>) # (v.pre = <<>>) THEN u.pre # <<>>
    ELSE IntSeqLess(SemVerPreKey(u.pre), SemVerPreKey(v.pre))

SemVerEq(u, v) == u.core = v.core /\ u.pre = v.pre

NoPre             == [present |-> FALSE, tag |-> "",  num |-> 0]
SomePre(tag, num) == [present |-> TRUE,  tag |-> tag, num |-> num]

Pep440Valid(v) ==
    /\ v.ok /\ v.epoch \in Nat /\ Len(v.release) >= 1
    /\ \A i \in 1..Len(v.release) : v.release[i] \in Nat
    /\ v.pre.present \in BOOLEAN
    /\ (v.pre.present => (v.pre.tag \in {"a", "b", "rc"} /\ v.pre.num \in Nat))
    /\ (~v.pre.present => (v.pre.tag = "" /\ v.pre.num = 0))
    /\ v.post \in ({-1} \cup Nat) /\ v.dev \in ({-1} \cup Nat)
    /\ \A i \in 1..Len(v.local) :
         (v.local[i].kind = "int" /\ v.local[i].n \in Nat /\ v.local[i].s = "") \/
         (v.local[i].kind = "str" /\ v.local[i].n = 0 /\ v.local[i].s \in NonNumericLocalStrings)

IsPublicPep440(v) == Pep440Valid(v) /\ v.local = <<>>

Pep440SuffixKey(v) ==
    LET preRank == IF ~v.pre.present /\ v.post = -1 /\ v.dev >= 0 THEN -1
                   ELSE IF ~v.pre.present THEN 3
                   ELSE CASE v.pre.tag = "a" -> 0 [] v.pre.tag = "b" -> 1 [] v.pre.tag = "rc" -> 2
    IN  <<preRank, IF v.pre.present THEN v.pre.num ELSE 0,
          IF v.post = -1 THEN 0 ELSE 1, IF v.post = -1 THEN 0 ELSE v.post,
          IF v.dev = -1 THEN 1 ELSE 0,  IF v.dev = -1 THEN 0 ELSE v.dev>>

Pep440LocalKey(loc) == [i \in 1..(2 * Len(loc)) |->
    LET t == loc[(i + 1) \div 2]
    IN  IF i % 2 = 1 THEN (IF t.kind = "int" THEN 1 ELSE 0)
        ELSE (IF t.kind = "int" THEN t.n ELSE StrRank(t.s))]

Pep440Less(u, v) ==
    LET uRel == TrimTrailingZeros(u.release)    vRel == TrimTrailingZeros(v.release)
        uSuf == Pep440SuffixKey(u)              vSuf == Pep440SuffixKey(v)
    IN  IF u.epoch # v.epoch THEN u.epoch < v.epoch
        ELSE IF uRel # vRel THEN IntSeqLess(uRel, vRel)
        ELSE IF uSuf # vSuf THEN IntSeqLess(uSuf, vSuf)
        ELSE IntSeqLess(Pep440LocalKey(u.local), Pep440LocalKey(v.local))

Pep440Eq(u, v) ==
    /\ u.epoch = v.epoch /\ TrimTrailingZeros(u.release) = TrimTrailingZeros(v.release)
    /\ Pep440SuffixKey(u) = Pep440SuffixKey(v) /\ u.local = v.local

FinalPep440(ep, rel) ==
    [ok |-> TRUE, reason |-> "", epoch |-> ep, release |-> rel,
     pre |-> NoPre, post |-> -1, dev |-> -1, local |-> <<>>]

Reject(why) ==
    [ok |-> FALSE, reason |-> why, epoch |-> 0, release |-> <<0>>,
     pre |-> NoPre, post |-> -1, dev |-> -1, local |-> <<>>]

\* ---------------------------------------------------------------------------
\* Observable NBGV 3.10.94 (`dea9a6c17c`) JSON Output Model
\* (`ManagedGitContext.GetIdAsVersionHelper`, `VersionExtensions.Create`,
\* `VersionOracle.cs:426, 497-529, 581-588`)
\* ---------------------------------------------------------------------------
RecipeUsesHeightInPre(r) ==
    r \in {"dev-h", "alpha-h", "beta-h", "pre-h", "preview-h", "rc-h",
           "beta-1-h", "beta-1-dev-h", "custom-h"}

HeightPosFor(shape, r) ==
    IF RecipeUsesHeightInPre(r) THEN PosPrerelease
    ELSE CASE shape = "two-part" -> PosBuild [] shape = "three-part" -> PosRevision [] OTHER -> PosNone

\* On `PosBuild`/`PosRevision`, `rawHeight = 0` clamps to `0`, while `rawHeight + offset = -1`
\* hits `System.Version`'s `-1` unspecified sentinel and exits `0`; only `<= -2` throws!
ValidNbgvHeight(shape, r, rawHeight, offset) ==
    (HeightPosFor(shape, r) \in {PosBuild, PosRevision} /\ rawHeight > 0) => (rawHeight + offset >= -1)

HeightTok(h) == CASE h = -2 -> StrV("-2") [] h = -1 -> StrV("-1") [] OTHER -> IntV(h)

PrereleaseTokens(r, h) ==
    CASE r = "none"         -> <<>>
      [] r = "dev-h"        -> <<StrV("dev"), HeightTok(h)>>
      [] r = "alpha-bare"   -> <<StrV("alpha")>>
      [] r = "alpha-0"      -> <<StrV("alpha"), IntV(0)>>
      [] r = "alpha-h"      -> <<StrV("alpha"), HeightTok(h)>>
      [] r = "beta-h"       -> <<StrV("beta"), HeightTok(h)>>
      [] r = "pre-h"        -> <<StrV("pre"), HeightTok(h)>>
      [] r = "preview-h"    -> <<StrV("preview"), HeightTok(h)>>
      [] r = "rc-h"         -> <<StrV("rc"), HeightTok(h)>>
      [] r = "beta-1"       -> <<StrV("beta"), IntV(1)>>
      [] r = "beta-1-h"     -> <<StrV("beta"), IntV(1), HeightTok(h)>>
      [] r = "beta-1-dev-h" -> <<StrV("beta"), IntV(1), StrV("dev"), HeightTok(h)>>
      [] r = "num-1"        -> <<IntV(1)>>
      [] r = "post-1"       -> <<StrV("post"), IntV(1)>>
      [] r = "custom-h"     -> <<StrV("ci"), HeightTok(h)>>

PadSemVer1Tokens(ts) == [i \in 1..Len(ts) |-> IF ts[i].kind = "int" THEN StrV(PaddedIntStr(ts[i].n)) ELSE ts[i]]
PadToFourComponents(v) == CASE Len(v) = 2 -> <<v[1], v[2], 0, 0>> [] Len(v) = 3 -> <<v[1], v[2], v[3], 0>> [] OTHER -> v
IsAlphabeticPrefix(p) == p \in {"g", "u"}

PrefixedCommitId(p, hex) ==
    CASE p = "g" -> IF hex = "0481029" THEN "g0481029" ELSE "gccf1759"
      [] p = "u" -> IF hex = "0481029" THEN "u0481029" ELSE "uccf1759"
      [] p = ""  -> hex
      [] OTHER   -> ""

EvalNbgvJsonWithPrefix(shape, r, rawH, offset, pubRel, commit, hasMeta, prefixOpt) ==
    LET found     == prefixOpt # "invalid-empty"
        prefix    == IF prefixOpt = "u" THEN "u" ELSE "g"
        inPre     == RecipeUsesHeightInPre(r)
        hPos      == IF ~found THEN PosNone ELSE HeightPosFor(shape, r)
        verH      == IF hPos = PosNone THEN 0 ELSE rawH
        posH      == IF hPos \notin {PosBuild, PosRevision} \/ verH = 0 THEN 0 ELSE verH + offset
        clampedH  == IF posH < 0 THEN 0 ELSE posH
        semCore   == IF ~found THEN <<0, 0, 0>>
                     ELSE IF shape = "two-part" /\ ~inPre THEN <<1, 2, clampedH>>
                     ELSE IF shape = "two-part" THEN <<1, 2, 0>> ELSE <<1, 2, 3>>
        simpleVer == IF ~found \/ (shape = "two-part" /\ (inPre \/ posH = -1)) THEN
                         IF found THEN <<1, 2>> ELSE <<0, 0>>
                     ELSE semCore
        dotNetVer == IF ~found THEN <<0, 0, 0, CommitRev(commit)>>
                     ELSE CASE shape = "two-part"   -> IF inPre \/ posH = -1 THEN <<1, 2>> ELSE <<1, 2, posH, CommitRev(commit)>>
                            [] shape = "three-part" -> IF inPre \/ posH = -1 THEN <<1, 2, 3>> ELSE <<1, 2, 3, posH>>
                            [] OTHER                -> <<1, 2, 3, 4>>
        rawHex    == CommitRawHex(commit)
        preTokens == IF ~found THEN <<>> ELSE PrereleaseTokens(r, verH + offset)
        sv1Base   == PadSemVer1Tokens(preTokens)
        metaSeq   == IF hasMeta THEN <<StrV("meta")>> ELSE <<>>
    IN  [versionFileFound    |-> found,
         versionMajor        |-> IF found THEN 1 ELSE 0,
         versionMinor        |-> IF found THEN 2 ELSE 0,
         buildNumber         |-> IF ~found \/ (shape = "two-part" /\ inPre) THEN 0
                                 ELSE IF shape = "two-part" THEN clampedH ELSE 3,
         versionRevision     |-> IF ~found THEN CommitRev(commit)
                                 ELSE CASE shape = "two-part" /\ ~inPre   -> IF posH = -1 THEN -1 ELSE CommitRev(commit)
                                        [] shape = "three-part" /\ ~inPre -> posH
                                        [] shape = "four-part"            -> 4
                                        [] OTHER                          -> -1,
         version             |-> dotNetVer,
         simpleVersion       |-> simpleVer,
         versionHeight       |-> verH,
         versionHeightOffset |-> IF found THEN offset ELSE 0,
         heightPosition      |-> hPos,
         publicRelease       |-> (pubRel /\ found),
         prereleaseNoHyphen  |-> preTokens,
         buildMetadata       |-> metaSeq,
         commitPrefix        |-> prefix,
         commitShort         |-> rawHex,
         semVer1             |-> [core |-> semCore,
                                  pre  |-> IF pubRel /\ found THEN sv1Base ELSE Append(sv1Base, StrV(rawHex)),
                                  build |-> <<>>],
         semVer2             |-> [core  |-> semCore,
                                  pre   |-> IF pubRel /\ found THEN preTokens
                                            ELSE Append(preTokens, StrV(PrefixedCommitId(prefix, rawHex))),
                                  build |-> metaSeq],
         infoVersion         |-> [core  |-> IF hPos = PosRevision THEN PadToFourComponents(dotNetVer) ELSE semCore,
                                  pre   |-> preTokens,
                                  build |-> <<StrV(rawHex)>> \o metaSeq]]

EvalNbgvJson(shape, r, rawH, offset, pubRel, commit, hasMeta) ==
    EvalNbgvJsonWithPrefix(shape, r, rawH, offset, pubRel, commit, hasMeta, "null-default-g")

EvalNuGetField(p, nugetSemVer, nugetPrecision) ==
    LET pTok == StrV(PrefixedCommitId(p.commitPrefix, p.commitShort))
    IN  [core  |-> IF nugetPrecision = "revision" THEN PadToFourComponents(p.version) ELSE p.semVer2.core,
         pre   |-> IF nugetSemVer = 2 THEN p.semVer2.pre
                   ELSE IF p.publicRelease THEN PadSemVer1Tokens(p.prereleaseNoHyphen)
                   ELSE Append(PadSemVer1Tokens(p.prereleaseNoHyphen), pTok),
         build |-> IF nugetSemVer = 2 THEN p.semVer2.build ELSE <<>>]

ResolveEpoch(ep) == IF ep = "two" THEN 2 ELSE 0

\* ---------------------------------------------------------------------------
\* Target Domain Projection
\* ---------------------------------------------------------------------------
MapStrictPhase(t) ==
    IF t.kind # "str" THEN ""
    ELSE CASE t.s = "alpha" -> "a" [] t.s = "beta" -> "b" [] t.s = "rc" -> "rc" [] OTHER -> ""

ParseStrictPep440Pre(ts) ==
    IF ts = <<>> THEN [ok |-> TRUE, pre |-> NoPre, dev |-> -1]
    ELSE IF Len(ts) = 2 /\ ts[1] = StrV("dev") /\ ts[2].kind = "int" /\ ts[2].n >= 0 THEN
        [ok |-> TRUE, pre |-> NoPre, dev |-> ts[2].n]
    ELSE IF Len(ts) = 2 /\ MapStrictPhase(ts[1]) # "" /\ ts[2].kind = "int" /\ ts[2].n >= 0 THEN
        [ok |-> TRUE, pre |-> SomePre(MapStrictPhase(ts[1]), ts[2].n), dev |-> -1]
    ELSE IF Len(ts) = 4 /\ MapStrictPhase(ts[1]) # "" /\ ts[2].kind = "int" /\ ts[2].n >= 0 /\
            ts[3] = StrV("dev") /\ ts[4].kind = "int" /\ ts[4].n >= 0 THEN
        [ok |-> TRUE, pre |-> SomePre(MapStrictPhase(ts[1]), ts[2].n), dev |-> ts[4].n]
    ELSE [ok |-> FALSE, pre |-> NoPre, dev |-> -1]

ProjectVersionFromComponents(
    found, targetRel, preTokens, buildMeta, pubRel,
    verH, offset, hPos, prefix, rawHex, epochCfg, policy
) ==
    LET base      == ParseStrictPep440Pre(preTokens)
        posH      == IF hPos \notin {PosBuild, PosRevision} \/ verH = 0 THEN 0 ELSE verH + offset
        commitTag == PrefixedCommitId(prefix, rawHex)
        carried   == hPos \in {PosBuild, PosPrerelease, PosNone} \/ (hPos = PosRevision /\ Len(targetRel) = 4)
        nonPubDev == IF carried THEN 0 ELSE posH
        rawLocal  == IF pubRel THEN buildMeta ELSE <<StrV(commitTag)>> \o buildMeta
        normLocal == [i \in 1..Len(rawLocal) |-> CanonicalPep440LocalSeg(rawLocal[i])]
    IN  IF ~found THEN Reject("version-file-not-found")
        ELSE IF posH < 0 THEN Reject("negative-effective-height")
        ELSE IF ~base.ok THEN Reject("unsupported-or-non-injective-prerelease")
        ELSE IF policy = "official-release" /\ ~pubRel THEN Reject("official-release-requires-public-release")
        ELSE IF policy \in {"public-index", "official-release"} /\ buildMeta # <<>> THEN
            Reject("public-index-forbids-build-metadata")
        ELSE IF pubRel THEN
            [ok |-> TRUE, reason |-> "", epoch |-> ResolveEpoch(epochCfg),
             release |-> targetRel, pre |-> base.pre, post |-> -1, dev |-> base.dev, local |-> normLocal]
        ELSE IF ~IsAlphabeticPrefix(prefix) \/ commitTag = "" THEN Reject("non-alphabetic-commit-prefix")
        ELSE IF base.dev >= 0 THEN Reject("dev-slot-saturated")
        ELSE [ok |-> TRUE, reason |-> "", epoch |-> ResolveEpoch(epochCfg),
              release |-> targetRel, pre |-> base.pre, post |-> -1, dev |-> nonPubDev,
              local |-> IF policy = "public-index" THEN <<>> ELSE normLocal]

TargetProject(p, epochCfg, policy) ==
    ProjectVersionFromComponents(
        p.versionFileFound, p.semVer2.core, p.prereleaseNoHyphen, p.semVer2.build,
        p.publicRelease, p.versionHeight, p.versionHeightOffset, p.heightPosition,
        p.commitPrefix, p.commitShort, epochCfg, policy)

PermissivePreFold(ts) ==
    IF Len(ts) = 1 /\ ts[1] = StrV("alpha") THEN [ok |-> TRUE, pre |-> SomePre("a", 0), dev |-> -1]
    ELSE IF Len(ts) = 2 /\ ts[1].s \in {"pre", "preview"} /\ ts[2].kind = "int" THEN
        [ok |-> TRUE, pre |-> SomePre("rc", ts[2].n), dev |-> -1]
    ELSE IF Len(ts) = 3 /\ ts[1] = StrV("beta") /\ ts[2].kind = "int" /\ ts[3].kind = "int" THEN
        [ok |-> TRUE, pre |-> SomePre("b", ts[2].n), dev |-> ts[3].n]
    ELSE ParseStrictPep440Pre(ts)
=============================================================================
