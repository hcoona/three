-------------------------- MODULE VersionProjection --------------------------
EXTENDS NbgvPep440Rules

\* Single-point state space (2,160 distinct states; zero `SinglePointAnchor` filter!).
\* Relational laws quantify the comparison point directly within each state.
VARIABLES shape, offset, recipe, height, pub, commit
vars == <<shape, offset, recipe, height, pub, commit>>

Json == EvalNbgvJson(shape, recipe, height, offset, pub, commit, FALSE)
OtherCommit(c) == IF c = "alpha-sha" THEN "digit-sha" ELSE "alpha-sha"

EffectiveCarryHeight(j) ==
    IF j.heightPosition = PosPrerelease THEN j.versionHeight + j.versionHeightOffset
    ELSE IF j.versionHeight = 0 THEN 0
    ELSE j.versionHeight + j.versionHeightOffset

Init ==
    /\ shape \in CoreShapes /\ offset \in HeightOffsets
    /\ recipe \in PreRecipes /\ height \in RawHeights
    /\ ValidNbgvHeight(shape, recipe, height, offset)
    /\ pub \in BOOLEAN /\ commit \in Commits

Spec == Init /\ [][UNCHANGED vars]_vars

\* 1. Strict weak order axioms (irreflexivity, asymmetry, Eq-totality, mixed `int` vs `str`
\*    SemVer prerelease & PEP 440 `+local` polarity, and cross-commit 3-point transitivity).
StrictWeakOrderLaw ==
    LET p1      == TargetProject(Json, "unset", "local")
        svInt   == [core |-> <<1, 2, 3>>, pre |-> <<IntV(1)>>, build |-> <<>>]
        svStr   == [core |-> <<1, 2, 3>>, pre |-> <<StrV("alpha")>>, build |-> <<>>]
        locStrA == [FinalPep440(0, <<1, 2, 3>>) EXCEPT !.local = <<StrV("g0481029")>>]
        locStrB == [FinalPep440(0, <<1, 2, 3>>) EXCEPT !.local = <<StrV("gccf1759")>>]
        locIntV == [FinalPep440(0, <<1, 2, 3>>) EXCEPT !.local = <<IntV(481029)>>]
    IN  /\ ~SemVerLess(Json.semVer2, Json.semVer2) /\ SemVerEq(Json.semVer2, Json.semVer2)
        /\ SemVerLess(svInt, svStr) /\ ~SemVerLess(svStr, svInt)
        /\ Pep440Less(locStrA, locStrB) /\ Pep440Less(locStrB, locIntV) /\ Pep440Less(locStrA, locIntV)
        /\ (p1.ok => ~Pep440Less(p1, p1) /\ Pep440Eq(p1, p1))
        /\ \A r2 \in AdmittedRecipes \cup {"num-1"}, h2 \in RawHeights, pub2 \in BOOLEAN, c2 \in Commits :
               ValidNbgvHeight(shape, r2, h2, offset) =>
                   LET j2 == EvalNbgvJson(shape, r2, h2, offset, pub2, c2, FALSE)
                       p2 == TargetProject(j2, "unset", "local")
                   IN  /\ (SemVerLess(Json.semVer2, j2.semVer2) => ~SemVerLess(j2.semVer2, Json.semVer2))
                       /\ ((~SemVerLess(Json.semVer2, j2.semVer2) /\ ~SemVerLess(j2.semVer2, Json.semVer2))
                              <=> SemVerEq(Json.semVer2, j2.semVer2))
                       /\ ((p1.ok /\ p2.ok) =>
                             /\ (Pep440Less(p1, p2) => ~Pep440Less(p2, p1))
                             /\ ((~Pep440Less(p1, p2) /\ ~Pep440Less(p2, p1)) <=> Pep440Eq(p1, p2)))
        /\ ((recipe = "none" /\ height = 0 /\ pub /\ commit = "alpha-sha") =>
              LET repSet == {"none", "beta-1", "beta-h", "num-1"}
                  validQuads == { <<r, h, b, c>> \in repSet \X RawHeights \X BOOLEAN \X Commits :
                                    ValidNbgvHeight(shape, r, h, offset) }
                  pts == ({ LET j == EvalNbgvJson(shape, q[1], q[2], offset, q[3], q[4], FALSE)
                            IN  [sv |-> j.semVer2, p |-> TargetProject(j, "unset", "local")] :
                              q \in validQuads }
                          \cup {[sv |-> svInt, p |-> locStrA],
                                [sv |-> svStr, p |-> locStrB],
                                [sv |-> svStr, p |-> locIntV]})
              IN  \A x \in pts, y \in pts :
                      /\ (SemVerLess(x.sv, y.sv) =>
                            \A z \in pts : SemVerLess(y.sv, z.sv) => SemVerLess(x.sv, z.sv))
                      /\ ((x.p.ok /\ y.p.ok /\ Pep440Less(x.p, y.p)) =>
                            \A z \in pts : (z.p.ok /\ Pep440Less(y.p, z.p)) => Pep440Less(x.p, z.p)))

\* 2. Exact admission biconditional across all channels, heights, and zero-epoch equivalence.
AdmissionAndChannelLaw ==
    /\ \A hasMeta \in BOOLEAN, ep \in EpochConfigs, pol \in ChannelPolicies :
           LET j       == EvalNbgvJson(shape, recipe, height, offset, pub, commit, hasMeta)
               p       == TargetProject(j, ep, pol)
               devBase == recipe \in {"dev-h", "beta-1-dev-h"}
               negPosH == HeightPosFor(shape, recipe) \in {PosBuild, PosRevision} /\ height > 0 /\ (height + offset < 0)
               negPreH == RecipeUsesHeightInPre(recipe) /\ (height + offset < 0)
           IN  /\ (p.ok <=>
                     /\ recipe \in AdmittedRecipes /\ ~negPosH /\ ~negPreH
                     /\ (pol = "official-release" => pub)
                     /\ (pol \in {"public-index", "official-release"} => ~hasMeta)
                     /\ (~pub => ~devBase))
               /\ (p.ok => Pep440Valid(p))
               /\ ((p.ok /\ pol \in {"public-index", "official-release"}) => IsPublicPep440(p))
    /\ \A pol \in ChannelPolicies :
           LET pUnset == TargetProject(Json, "unset", pol)
               pZero  == TargetProject(Json, "zero", pol)
               pTwo   == TargetProject(Json, "two", pol)
           IN  pUnset = pZero /\ (pZero.ok => Pep440Less(pZero, pTwo))

\* 3. Prerelease polarity preservation (`pre < final`, and `1.2.0 == 1.2` on 2-part).
PreReleasePolarityLaw ==
    \A ep \in EpochConfigs, pol \in ChannelPolicies :
        LET p      == TargetProject(Json, ep, pol)
            final1 == FinalPep440(ResolveEpoch(ep), Json.semVer2.core)
            simple == FinalPep440(ResolveEpoch(ep), Json.simpleVersion)
        IN  p.ok =>
                /\ p.post = -1 /\ Pep440Eq(final1, simple)
                /\ IF Json.semVer2.pre # <<>> THEN Pep440Less(p, final1) ELSE Pep440Eq(p, final1)

\* 4. Canonical SemVer 2.0 <-> PEP 440 order isomorphism & pre-stage `.dev` ordering.
CanonicalOrderIsomorphismLaw ==
    /\ ((pub /\ recipe \in CanonicalHomomorphicRecipes) =>
          \A r2 \in CanonicalHomomorphicRecipes, h2 \in RawHeights, ep \in EpochConfigs :
              ValidNbgvHeight(shape, r2, h2, offset) =>
                  LET j2 == EvalNbgvJson(shape, r2, h2, offset, TRUE, commit, FALSE)
                      p1 == TargetProject(Json, ep, "official-release")
                      p2 == TargetProject(j2, ep, "official-release")
                  IN  (p1.ok /\ p2.ok) =>
                          /\ (SemVerLess(Json.semVer2, j2.semVer2) <=> Pep440Less(p1, p2))
                          /\ (SemVerEq(Json.semVer2, j2.semVer2) <=> Pep440Eq(p1, p2)))
    /\ ((pub /\ shape = "three-part" /\ recipe = "dev-h") =>
          \A h2 \in RawHeights : ValidNbgvHeight(shape, "alpha-0", h2, offset) =>
              LET p1 == TargetProject(Json, "unset", "official-release")
                  p2 == TargetProject(EvalNbgvJson(shape, "alpha-0", h2, offset, TRUE, commit, FALSE),
                                      "unset", "official-release")
              IN  (p1.ok /\ p2.ok) => Pep440Less(p1, p2))
    /\ ((pub /\ recipe = "beta-1-dev-h") =>
          \A h2 \in RawHeights : ValidNbgvHeight(shape, "beta-1", h2, offset) =>
              LET p1 == TargetProject(Json, "unset", "official-release")
                  p2 == TargetProject(EvalNbgvJson(shape, "beta-1", h2, offset, TRUE, commit, FALSE),
                                      "unset", "official-release")
              IN  (p1.ok /\ p2.ok) => Pep440Less(p1, p2))

\* Universal positive `.dev`-slot saturation theorem over ALL admitted `"dev-h"` and
\* `"beta-1-dev-h"` states: at fixed `(epoch, release, pre)` and `dev = -1 \/ dev >= effH`,
\* NO valid PEP 440 suffix sorts strictly below `pubDevH`!
DevSlotCandidatesAboveOrEqual ==
    (pub /\ recipe \in {"dev-h", "beta-1-dev-h"} /\ height + offset >= 0) =>
        LET pubDevH == TargetProject(Json, "unset", "official-release")
            effH    == height + offset
            cands   == {
                [ok |-> TRUE, reason |-> "", epoch |-> 0, release |-> Json.semVer2.core,
                 pre |-> pr, post |-> po, dev |-> dv, local |-> loc] :
                    pr \in IF recipe = "dev-h" THEN {NoPre, SomePre("a", 0)}
                           ELSE {SomePre("b", 1), SomePre("b", 2), SomePre("rc", 0)},
                    po \in {-1, 0},
                    dv \in {-1, effH, effH + 1},
                    loc \in {<<>>, <<StrV("gccf1759")>>}
            }
        IN  ~\E v \in cands : Pep440Less(v, pubDevH)

\* 5. Unconditional promotion monotonicity (`nonPublic(H) < public(H)` & `public(eff1) < nonPublic(eff2)`),
\*    paired with universal `.dev`-slot saturation across both `"dev-h"` and `"beta-1-dev-h"`.
UnconditionalPromotionMonotonicityLaw ==
    /\ DevSlotCandidatesAboveOrEqual
    /\ \A pol \in {"local", "public-index"} :
        /\ (~pub =>
              LET pNonPub == TargetProject(Json, "unset", pol)
                  pPub    == TargetProject(EvalNbgvJson(shape, recipe, height, offset, TRUE, commit, FALSE),
                                           "unset", "official-release")
              IN  (pNonPub.ok /\ pPub.ok) => Pep440Less(pNonPub, pPub))
        /\ ((pub /\ Json.heightPosition \in {PosBuild, PosPrerelease}) =>
              \A h2 \in RawHeights, c2 \in Commits :
                  ValidNbgvHeight(shape, recipe, h2, offset) =>
                      LET j2   == EvalNbgvJson(shape, recipe, h2, offset, FALSE, c2, FALSE)
                          pPub == TargetProject(Json, "unset", "official-release")
                          pNxt == TargetProject(j2, "unset", pol)
                      IN  (EffectiveCarryHeight(Json) < EffectiveCarryHeight(j2) /\ pPub.ok /\ pNxt.ok) =>
                              Pep440Less(pPub, pNxt))

\* 6. Causal height monotonicity on `PosRevision` (`1.2.3`, `1.2.3-beta.1`) despite SHA order.
RevisionHeightMonotonicityLaw ==
    (shape = "three-part" /\ recipe \in {"none", "beta-1"} /\ ~pub) =>
        \A h2 \in RawHeights, c2 \in Commits, pol \in {"local", "public-index"} :
            ValidNbgvHeight(shape, recipe, h2, offset) =>
                LET adj1 == IF height = 0 THEN 0 ELSE height + offset
                    adj2 == IF h2 = 0 THEN 0 ELSE h2 + offset
                    p1   == TargetProject(Json, "unset", pol)
                    p2   == TargetProject(EvalNbgvJson(shape, recipe, h2, offset, FALSE, c2, FALSE), "unset", pol)
                IN  (adj1 < adj2 /\ p1.ok /\ p2.ok) => Pep440Less(p1, p2)

\* ---------------------------------------------------------------------------
\* Deliberately Refuted Domain Conjectures (TLC exit 12 witnesses)
\* ---------------------------------------------------------------------------
PublicIndexDistinguishesParallelCommits ==
    ~pub =>
        LET p1 == TargetProject(Json, "unset", "public-index")
            p2 == TargetProject(EvalNbgvJson(shape, recipe, height, offset, FALSE, OtherCommit(commit), FALSE),
                                "unset", "public-index")
        IN  (p1.ok /\ p2.ok) => ~Pep440Eq(p1, p2)

PermissiveFoldingPreservesSemVerOrder ==
    pub =>
        \A r2 \in PreRecipes, h2 \in RawHeights :
            ValidNbgvHeight(shape, r2, h2, offset) =>
                LET j2 == EvalNbgvJson(shape, r2, h2, offset, TRUE, commit, FALSE)
                    b1 == PermissivePreFold(Json.prereleaseNoHyphen)
                    b2 == PermissivePreFold(j2.prereleaseNoHyphen)
                    p1 == [FinalPep440(0, Json.semVer2.core) EXCEPT !.pre = b1.pre, !.dev = b1.dev]
                    p2 == [FinalPep440(0, j2.semVer2.core)   EXCEPT !.pre = b2.pre, !.dev = b2.dev]
                IN  (b1.ok /\ b2.ok /\ SemVerLess(Json.semVer2, j2.semVer2)) => Pep440Less(p1, p2)

\* Deliberate witness negation of `DevSlotCandidatesAboveOrEqual` (`TLC exit 12`):
ExistsPep440SlotBelowDevH ==
    (shape = "three-part" /\ recipe = "dev-h" /\ offset = 0 /\ height = 2 /\ pub) =>
        ~DevSlotCandidatesAboveOrEqual
=============================================================================
