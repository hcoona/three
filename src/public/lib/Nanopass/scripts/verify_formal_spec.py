# /// script
# requires-python = ">=3.14"
# dependencies = [
#   "z3-solver==5.4.1.0",
# ]
# ///
"""Bounded relational Z3 formal specification for Nanopass.

Models four core algebraic and semantic domains over finite bounded universes,
comparing upstream Racket/Chez Nanopass semantics with Hcoona.Nanopass's
static C# 15 / Roslyn compile-time refinements:

  - Domain 1 (Language Set-Algebra & Reachability):
    Bounded over 4 elements / 4 nonterminals. Proves extend/prune round-trip
    under disjointness (D1-T1), order non-commutativity (D1-T2),
    least-fixed-point reachability preservation under prune (D1-T3), and
    diff-languages exactness (D1-T4).

  - Domain 2 (Nonterminal Subtype Coercion & Diamond Synthesis):
    Bounded over 4-nonterminal directed inclusion graphs with exact
    shortest-path distance dist(u, v) in {0..4} and capped shortest-path
    multiplicity min_paths(u, v) in {0, 1, 2}. Proves 1-hop self-inclusion
    NP0105 vs union-safe mutual cycles (D2-T1), unequal-length shortest-path
    uniqueness vs equal-length diamond NP1003 (D2-T2), and target-subtype
    preservation during pass synthesis on equal-length diamonds (D2-T3).

  - Domain 3 (Pass Synthesis Exhaustiveness, Guarded Clauses & Fixed Point):
    Bounded over 2-level ASTs, 3 productions, all runtime guard valuations,
    and 3-nonterminal field hierarchies. Independently defines compile-time
    diagnostics (NP1001/NP1003) and runtime step semantics, proving
    exhaustiveness soundness without runtime traps (D3-T1), exact iff
    equivalence between NP1001 and runtime unhandled-form traps (D3-T2),
    ExtraReturn no-xval? arity conservation (D3-T3), and recursive nested-field
    subtype fallback soundness and completeness (D3-T4).

  - Domain 4 (5-Tier S-Expression Parse Dispatch & Unparse Round-Trip):
    Explicitly composes unparse_raw : AstNode -> SExp with 5-tier
    parse_root : SExp -> ParseOutcome, proving 5-phase OrderRules partition
    and deterministic parse dispatch (D4-T1), exact equivalence between
    universal parse_root(unparse_raw(node)) == Ok(node) and 5-tier NoShadowing
    (D4-T2), and non-invertibility of tag-erasing [Pretty] formats vs raw=#t
    injectivity (D4-T3).

Upstream references (pinned):
  - Keep & Dybvig, "A Nanopass Framework for Commercial Compiler Development",
    ICFP 2013, https://doi.org/10.1145/2500365.2500618
  - https://github.com/nanopass/nanopass-framework-racket @
    2af6a308fe6a294ad8d37b7865d07bedaf87ec15
  - https://github.com/nanopass/nanopass-framework-scheme @
    c14d392487886df9275c1d4a3f0f61e1b6c4f2ef
"""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from typing import TYPE_CHECKING

from z3 import (
    And,
    Bool,
    BoolRef,
    Bools,
    BoolVal,
    Datatype,
    DatatypeSortRef,
    ExprRef,
    Function,
    If,
    Implies,
    Int,
    IntSort,
    IntVal,
    ModelRef,
    Not,
    Or,
    Solver,
    Sum,
    is_true,
    sat,
    unsat,
)

if TYPE_CHECKING:
    from collections.abc import Callable

UNIVERSE_SIZE = 4
GRAPH_NODES = 4
AMBIGUOUS_PATHS = 2
MAX_PRODS = 3
GUARD_MASKS = 8
ATOM_COUNT = 3
UNARY_PROD = 1

SOURCES: dict[str, dict[str, str]] = {
    "icfp2013": {
        "citation": (
            "Andrew W. Keep and R. Kent Dybvig, 'A Nanopass Framework for "
            "Commercial Compiler Development', ICFP 2013, pp. 343-350"
        ),
        "url": "https://doi.org/10.1145/2500365.2500618",
    },
    "nanopass_framework_racket": {
        "repository": "https://github.com/nanopass/nanopass-framework-racket",
        "commit": "2af6a308fe6a294ad8d37b7865d07bedaf87ec15",
        "files": (
            "private/language.rkt, private/meta-parser.rkt, "
            "private/parser.rkt, private/pass.rkt, private/records.rkt, "
            "private/unparser.rkt"
        ),
    },
    "nanopass_framework_scheme": {
        "repository": "https://github.com/nanopass/nanopass-framework-scheme",
        "commit": "c14d392487886df9275c1d4a3f0f61e1b6c4f2ef",
        "files": (
            "nanopass/language.ss, nanopass/parser.ss, nanopass/pass.ss, "
            "nanopass/records.ss, nanopass/unparser.ss"
        ),
    },
}

BOUNDS: dict[str, object] = {
    "domain_1_universe_size": UNIVERSE_SIZE,
    "domain_2_max_nonterminals": GRAPH_NODES,
    "domain_3_max_productions": MAX_PRODS,
    "domain_3_ast_depth": 2,
    "domain_4_symbols": ["sym_x", "kw_neg", "num_42"],
    "domain_4_dispatch_tiers": 5,
}


@dataclass(frozen=True)
class TheoremResult:
    """Outcome of a non-vacuous Z3 theorem check."""

    theorem_id: str
    domain: str
    title: str
    passed: bool
    detail: str


@dataclass(frozen=True)
class VectorWitness:
    """Solver-decoded differential vector and satisfiability status."""

    vector_id: str
    domain: str
    scenario: str
    passed: bool
    payload: dict[str, object]


@dataclass(frozen=True)
class VectorSpec:
    """Input specification for a solver-decoded differential vector."""

    vector_id: str
    domain: str
    scenario: str
    z3_backing: str
    constraints: list[BoolRef]
    decoder: Callable[[ModelRef], dict[str, object]]
    divergence_note: str | None = None


def _prove_non_vacuous(
    theorem_id: str,
    title: str,
    axioms: list[BoolRef],
    claim: BoolRef,
    witness: list[BoolRef] | tuple[BoolRef, ...] = (),
) -> TheoremResult:
    """Check non-vacuous witness sat and universal axioms => claim validity."""
    domain = theorem_id.split("-", 1)[0]
    inhabited_solver = Solver()
    inhabited_solver.add(*axioms, *witness, claim)
    if inhabited_solver.check() != sat:
        return TheoremResult(
            theorem_id=theorem_id,
            domain=domain,
            title=title,
            passed=False,
            detail="Axioms/witness are unsatisfiable (vacuous theorem).",
        )

    counter_solver = Solver()
    counter_solver.add(*axioms)
    counter_solver.add(Not(claim))
    if counter_solver.check() == unsat:
        return TheoremResult(
            theorem_id=theorem_id,
            domain=domain,
            title=title,
            passed=True,
            detail="non-vacuous unsat",
        )
    return TheoremResult(
        theorem_id=theorem_id,
        domain=domain,
        title=title,
        passed=False,
        detail=f"Counterexample found: {counter_solver.model()}",
    )


def _decode_vector(spec: VectorSpec) -> VectorWitness:
    """Solve scenario constraints in Z3 and decode expected outcomes."""
    solver = Solver()
    solver.add(*spec.constraints)
    if solver.check() != sat:
        return VectorWitness(
            vector_id=spec.vector_id,
            domain=spec.domain,
            scenario=spec.scenario,
            passed=False,
            payload={
                "id": spec.vector_id,
                "domain": spec.domain,
                "error": "unsat",
            },
        )
    decoded = spec.decoder(solver.model())
    entry: dict[str, object] = {
        "id": spec.vector_id,
        "domain": spec.domain,
        "scenario": spec.scenario,
        "z3_backing": spec.z3_backing,
        **decoded,
    }
    if spec.divergence_note is not None:
        entry["divergence_note"] = spec.divergence_note
    return VectorWitness(
        vector_id=spec.vector_id,
        domain=spec.domain,
        scenario=spec.scenario,
        passed=True,
        payload=entry,
    )


def _eval_bool(model: ModelRef, expr: BoolRef) -> bool:
    return is_true(model.eval(expr, model_completion=True))


def _eval_int(model: ModelRef, expr: ExprRef) -> int:
    return int(model.eval(expr, model_completion=True).as_long())


def _verify_domain_1() -> tuple[list[TheoremResult], list[VectorWitness]]:
    theorems: list[TheoremResult] = []
    vectors: list[VectorWitness] = []
    elems = range(UNIVERSE_SIZE)

    base = [Bool(f"d1_base_{i}") for i in elems]
    added = [Bool(f"d1_add_{i}") for i in elems]
    extended = [Or(base[i], added[i]) for i in elems]
    roundtrip = [And(extended[i], Not(added[i])) for i in elems]
    disjoint = And(*[Not(And(base[i], added[i])) for i in elems])
    roundtrip_eq = And(*[roundtrip[i] == base[i] for i in elems])
    theorems.append(
        _prove_non_vacuous(
            "D1-T1",
            "Disjoint extend-then-prune restores exact base set",
            [disjoint],
            roundtrip_eq,
            witness=[Or(*base), Or(*added)],
        )
    )

    op_a = [Bool(f"d1_opa_{i}") for i in elems]
    add_then_del = [And(Or(base[i], op_a[i]), Not(op_a[i])) for i in elems]
    del_then_add = [Or(And(base[i], Not(op_a[i])), op_a[i]) for i in elems]
    theorems.append(
        _prove_non_vacuous(
            "D1-T2",
            "Ordered clause algebra is non-commutative when + and - overlap",
            [Or(*op_a)],
            Or(*[add_then_del[i] != del_then_add[i] for i in elems]),
            witness=[Or(*base)],
        )
    )

    nt_ids = range(GRAPH_NODES)
    ref = [[Bool(f"d1_ref_{u}_{v}") for v in nt_ids] for u in nt_ids]
    r0 = [i == 0 for i in nt_ids]
    r1 = [Or(r0[v], *[And(r0[u], ref[u][v]) for u in nt_ids]) for v in nt_ids]
    r2 = [Or(r1[v], *[And(r1[u], ref[u][v]) for u in nt_ids]) for v in nt_ids]
    reach = [
        Or(r2[v], *[And(r2[u], ref[u][v]) for u in nt_ids]) for v in nt_ids
    ]
    pruned = [[And(reach[u], ref[u][v]) for v in nt_ids] for u in nt_ids]
    p1 = [
        Or(r0[v], *[And(r0[u], pruned[u][v]) for u in nt_ids]) for v in nt_ids
    ]
    p2 = [
        Or(p1[v], *[And(p1[u], pruned[u][v]) for u in nt_ids]) for v in nt_ids
    ]
    p_reach = [
        Or(p2[v], *[And(p2[u], pruned[u][v]) for u in nt_ids]) for v in nt_ids
    ]
    theorems.append(
        _prove_non_vacuous(
            "D1-T3",
            "prune-language preserves exact entry-reachable cone and edges",
            [],
            And(
                *[p_reach[v] == reach[v] for v in nt_ids],
                *[
                    Implies(reach[u], pruned[u][v] == ref[u][v])
                    for u in nt_ids
                    for v in nt_ids
                ],
            ),
            witness=[ref[0][1], Not(ref[0][2]), ref[2][3]],
        )
    )

    lang_0 = [Bool(f"d1_l0_{i}") for i in elems]
    lang_1 = [Bool(f"d1_l1_{i}") for i in elems]
    removed = [And(lang_0[i], Not(lang_1[i])) for i in elems]
    added_diff = [And(lang_1[i], Not(lang_0[i])) for i in elems]
    rebuilt = [
        Or(And(lang_0[i], Not(removed[i])), added_diff[i]) for i in elems
    ]
    theorems.append(
        _prove_non_vacuous(
            "D1-T4",
            "diff-languages delta (- removed, + added) reconstructs L1 from L0",
            [],
            And(
                *[rebuilt[i] == lang_1[i] for i in elems],
                *[Not(And(removed[i], added_diff[i])) for i in elems],
            ),
            witness=[Or(*[lang_0[i] != lang_1[i] for i in elems])],
        )
    )

    mv_t1, mv_t2 = Int("d1_mv_term1"), Int("d1_mv_term2")
    dup_mv_conflict = mv_t1 == mv_t2
    mv_x_in, mv_y_in = Bool("d1_mv_x_in"), Bool("d1_mv_y_in")
    mv_x_rm, mv_y_rm = Bool("d1_mv_x_rm"), Bool("d1_mv_y_rm")
    term_kept = Or(And(mv_x_in, Not(mv_x_rm)), And(mv_y_in, Not(mv_y_rm)))
    inc_t1, inc_t2 = Int("d1_inc_1"), Int("d1_inc_2")
    dup_inc = inc_t1 == inc_t2

    vectors.extend(
        _decode_vector(spec)
        for spec in (
            VectorSpec(
                "VEC-D1-01",
                "D1",
                "Extend with (neg e) then remove (neg e) restores base",
                "D1-T1",
                [base[0], Not(base[1]), Not(added[0]), added[1], disjoint],
                lambda m: {
                    "decoded_witness": {
                        "roundtrip_eq": _eval_bool(m, roundtrip_eq)
                    },
                    "upstream_expected": {"valid": _eval_bool(m, roundtrip_eq)},
                    "csharp_expected": {
                        "diagnostics": []
                        if _eval_bool(m, roundtrip_eq)
                        else ["NP0100"]
                    },
                },
            ),
            VectorSpec(
                "VEC-D1-02",
                "D1",
                "Duplicate metavariable prefix across two terminals",
                "D1-T1",
                [mv_t1 == 1, mv_t2 == 1],
                lambda m: {
                    "decoded_witness": {
                        "duplicate_metavar": _eval_bool(m, dup_mv_conflict)
                    },
                    "upstream_expected": {
                        "valid": not _eval_bool(m, dup_mv_conflict)
                    },
                    "csharp_expected": {
                        "diagnostics": ["NP0104"]
                        if _eval_bool(m, dup_mv_conflict)
                        else []
                    },
                },
            ),
            VectorSpec(
                "VEC-D1-03",
                "D1",
                "Prune eliminates unreachable orphan nonterminal cycle",
                "D1-T3",
                [
                    ref[0][1],
                    Not(ref[0][2]),
                    Not(ref[0][3]),
                    Not(ref[1][2]),
                    Not(ref[1][3]),
                    ref[2][3],
                    ref[3][2],
                ],
                lambda m: {
                    "decoded_witness": {
                        "reachable": [
                            v for v in nt_ids if _eval_bool(m, reach[v])
                        ],
                        "pruned": [
                            v for v in nt_ids if not _eval_bool(m, reach[v])
                        ],
                    },
                    "upstream_expected": {
                        "valid": True,
                        "pruned_nonterminals": [
                            v for v in nt_ids if not _eval_bool(m, reach[v])
                        ],
                    },
                    "csharp_expected": {"diagnostics": []},
                },
            ),
            VectorSpec(
                "VEC-D1-04",
                "D1",
                "Removing ['y'] from ['x', 'y'] retains terminal with ['x']",
                "D1-T1",
                [mv_x_in, mv_y_in, Not(mv_x_rm), mv_y_rm],
                lambda m: {
                    "decoded_witness": {
                        "terminal_retained": _eval_bool(m, term_kept)
                    },
                    "upstream_expected": {
                        "valid": True,
                        "terminal_retained": _eval_bool(m, term_kept),
                    },
                    "csharp_expected": {
                        "diagnostics": [],
                        "terminal_retained": _eval_bool(m, term_kept),
                    },
                },
            ),
            VectorSpec(
                "VEC-D1-05",
                "D1",
                "Duplicate nonterminal inclusions (b1, b2 -> Body) in Expr",
                "D1-T1",
                [inc_t1 == 1, inc_t2 == 1],
                lambda m: {
                    "decoded_witness": {
                        "dup_inclusion": _eval_bool(m, dup_inc)
                    },
                    "upstream_expected": {"valid": not _eval_bool(m, dup_inc)},
                    "csharp_expected": {
                        "diagnostics": ["NP0104"]
                        if _eval_bool(m, dup_inc)
                        else []
                    },
                },
            ),
        )
    )
    return theorems, vectors


def _min2(expr: ExprRef) -> ExprRef:
    return If(expr >= AMBIGUOUS_PATHS, IntVal(AMBIGUOUS_PATHS), expr)


def _build_shortest_path_relations(
    prefix: str,
) -> tuple[list[list[BoolRef]], list[list[ExprRef]], list[list[ExprRef]]]:
    nodes = range(GRAPH_NODES)
    edge = [[Bool(f"{prefix}_edge_{u}_{v}") for v in nodes] for u in nodes]
    ways1 = [
        [If(And(u != v, edge[u][v]), IntVal(1), IntVal(0)) for v in nodes]
        for u in nodes
    ]
    ways2 = [
        [
            _min2(
                Sum(
                    [
                        If(edge[u][w], ways1[w][v], IntVal(0))
                        for w in nodes
                        if w not in {u, v}
                    ]
                )
            )
            if u != v
            else IntVal(0)
            for v in nodes
        ]
        for u in nodes
    ]
    ways3 = [
        [
            _min2(
                Sum(
                    [
                        If(
                            And(edge[u][w1], edge[w1][w2], edge[w2][v]),
                            IntVal(1),
                            IntVal(0),
                        )
                        for w1 in nodes
                        for w2 in nodes
                        if len({u, w1, w2, v}) == GRAPH_NODES
                    ]
                )
            )
            if u != v
            else IntVal(0)
            for v in nodes
        ]
        for u in nodes
    ]
    dist: list[list[ExprRef]] = []
    min_paths: list[list[ExprRef]] = []
    for u in nodes:
        dist_row: list[ExprRef] = []
        mp_row: list[ExprRef] = []
        for v in nodes:
            if u == v:
                dist_row.append(IntVal(0))
                mp_row.append(IntVal(1))
            else:
                dist_row.append(
                    If(
                        ways1[u][v] > 0,
                        IntVal(1),
                        If(
                            ways2[u][v] > 0,
                            IntVal(2),
                            If(
                                ways3[u][v] > 0,
                                IntVal(3),
                                IntVal(GRAPH_NODES),
                            ),
                        ),
                    )
                )
                mp_row.append(
                    If(
                        ways1[u][v] > 0,
                        ways1[u][v],
                        If(ways2[u][v] > 0, ways2[u][v], ways3[u][v]),
                    )
                )
        dist.append(dist_row)
        min_paths.append(mp_row)
    return edge, dist, min_paths


def _verify_domain_2() -> tuple[list[TheoremResult], list[VectorWitness]]:
    theorems: list[TheoremResult] = []
    vectors: list[VectorWitness] = []
    nodes = range(GRAPH_NODES)
    edge, dist, min_paths = _build_shortest_path_relations("d2")

    has_self_loop = Or(*[edge[u][u] for u in nodes])
    has_multi_cycle = Or(
        *[
            And(dist[u][v] < GRAPH_NODES, dist[v][u] < GRAPH_NODES)
            for u in nodes
            for v in nodes
            if u < v
        ]
    )
    upstream_acyclic = And(Not(has_self_loop), Not(has_multi_cycle))
    csharp_union_valid = Not(has_self_loop)

    theorems.append(
        _prove_non_vacuous(
            "D2-T1",
            "C# union rule accepts DAGs and >=2-node cycles without NP0105",
            [],
            And(
                Implies(upstream_acyclic, csharp_union_valid),
                And(csharp_union_valid, Not(upstream_acyclic))
                == And(Not(has_self_loop), has_multi_cycle),
            ),
            witness=[csharp_union_valid, has_multi_cycle],
        )
    )

    unequal_unique = And(
        *[
            Implies(edge[u][v], And(dist[u][v] == 1, min_paths[u][v] == 1))
            for u in nodes
            for v in nodes
            if u != v
        ]
    )
    equal_ambiguous = And(
        *[
            Implies(
                And(
                    Not(edge[u][v]),
                    edge[u][w1],
                    edge[w1][v],
                    edge[u][w2],
                    edge[w2][v],
                ),
                And(
                    dist[u][v] == AMBIGUOUS_PATHS,
                    min_paths[u][v] == AMBIGUOUS_PATHS,
                ),
            )
            for u in nodes
            for v in nodes
            for w1 in nodes
            for w2 in nodes
            if len({u, v, w1, w2}) == GRAPH_NODES
        ]
    )
    theorems.append(
        _prove_non_vacuous(
            "D2-T2",
            "Shortest-path coercion resolves unequal and flags equal diamonds",
            [Not(has_self_loop)],
            And(unequal_unique, equal_ambiguous),
            witness=[edge[0][3], edge[0][1], edge[1][3]],
        )
    )

    diamond_axioms = [
        Not(has_self_loop),
        edge[0][1],
        edge[0][2],
        edge[1][3],
        edge[2][3],
        Not(edge[0][3]),
        Not(edge[1][2]),
        Not(edge[2][1]),
        *[Not(edge[3][v]) for v in nodes],
        *[Not(edge[v][0]) for v in nodes],
    ]
    stepwise_unique = And(
        min_paths[0][1] == 1,
        min_paths[0][2] == 1,
        min_paths[1][3] == 1,
        min_paths[2][3] == 1,
    )
    theorems.append(
        _prove_non_vacuous(
            "D2-T3",
            "Subtype-preserving synthesis resolves equal diamonds per hop",
            diamond_axioms,
            And(min_paths[0][3] == AMBIGUOUS_PATHS, stepwise_unique),
        )
    )

    vectors.extend(
        _decode_vector(spec)
        for spec in (
            VectorSpec(
                "VEC-D2-01",
                "D2",
                "Unequal-length diamond (D -> A depth 1, D -> B -> A depth 2)",
                "D2-T2",
                [
                    Not(has_self_loop),
                    edge[0][3],
                    edge[0][1],
                    edge[1][3],
                    Not(edge[0][2]),
                ],
                lambda m: {
                    "decoded_witness": {
                        "dist_0_3": _eval_int(m, dist[0][3]),
                        "min_paths_0_3": _eval_int(m, min_paths[0][3]),
                    },
                    "upstream_expected": {
                        "valid": True,
                        "selected_depth": _eval_int(m, dist[0][3]),
                    },
                    "csharp_expected": {
                        "diagnostics": ["NP1003"]
                        if _eval_int(m, min_paths[0][3]) >= AMBIGUOUS_PATHS
                        else []
                    },
                },
            ),
            VectorSpec(
                "VEC-D2-02",
                "D2",
                "Direct 1-hop nonterminal self-inclusion (Expr ::= e | x)",
                "D2-T1",
                [edge[0][0]],
                lambda m: {
                    "decoded_witness": {
                        "self_loop": _eval_bool(m, has_self_loop)
                    },
                    "upstream_expected": {
                        "valid": _eval_bool(m, upstream_acyclic)
                    },
                    "csharp_expected": {
                        "diagnostics": ["NP0105"]
                        if _eval_bool(m, has_self_loop)
                        else []
                    },
                },
            ),
            VectorSpec(
                "VEC-D2-03",
                "D2",
                "2-node mutual nonterminal inclusion cycle (Expr <-> Form)",
                "D2-T1",
                [Not(has_self_loop), edge[0][1], edge[1][0]],
                lambda m: {
                    "decoded_witness": {
                        "self_loop": _eval_bool(m, has_self_loop),
                        "multi_cycle": _eval_bool(m, has_multi_cycle),
                    },
                    "upstream_expected": {
                        "valid": _eval_bool(m, upstream_acyclic)
                    },
                    "csharp_expected": {
                        "diagnostics": ["NP0105"]
                        if _eval_bool(m, has_self_loop)
                        else []
                    },
                },
                divergence_note=(
                    "Intentional C# generalization: C# 15 nominal unions allow "
                    "multi-node mutual nonterminal cycles; only 1-hop "
                    "self-inclusion triggers NP0105."
                ),
            ),
            VectorSpec(
                "VEC-D2-04",
                "D2",
                "Equal-length diamond (D -> {B, C} -> A) leaf-to-D coercion",
                "D2-T2",
                diamond_axioms,
                lambda m: {
                    "decoded_witness": {
                        "dist_0_3": _eval_int(m, dist[0][3]),
                        "min_paths_0_3": _eval_int(m, min_paths[0][3]),
                    },
                    "upstream_expected": {
                        "valid": _eval_bool(m, upstream_acyclic),
                        "coercion": "declaration_order_first_branch",
                    },
                    "csharp_expected": {
                        "diagnostics": ["NP1003"]
                        if _eval_int(m, min_paths[0][3]) >= AMBIGUOUS_PATHS
                        else []
                    },
                },
                divergence_note=(
                    "Intentional C# soundness tightening: equal-length target "
                    "diamond coercion (min_paths == 2) emits compile-time "
                    "NP1003 instead of declaration-order tie-breaking."
                ),
            ),
            VectorSpec(
                "VEC-D2-05",
                "D2",
                "Equal-length diamond (D -> {B, C} -> A) identity pass",
                "D2-T3",
                diamond_axioms,
                lambda m: {
                    "decoded_witness": {
                        "stepwise_unique": _eval_bool(m, stepwise_unique)
                    },
                    "upstream_expected": {
                        "valid": _eval_bool(m, stepwise_unique)
                    },
                    "csharp_expected": {
                        "diagnostics": []
                        if _eval_bool(m, stepwise_unique)
                        else ["NP1003"]
                    },
                },
            ),
            VectorSpec(
                "VEC-D2-06",
                "D2",
                "Transitive 2-step subtype chain (Top -> Mid -> Atom)",
                "D2-T3",
                [
                    upstream_acyclic,
                    edge[0][1],
                    edge[1][2],
                    Not(edge[0][2]),
                    Not(edge[0][3]),
                    Not(edge[1][3]),
                ],
                lambda m: {
                    "decoded_witness": {
                        "dist_0_2": _eval_int(m, dist[0][2]),
                        "min_paths_0_2": _eval_int(m, min_paths[0][2]),
                    },
                    "upstream_expected": {
                        "valid": _eval_int(m, min_paths[0][2]) == 1
                    },
                    "csharp_expected": {
                        "diagnostics": []
                        if _eval_int(m, min_paths[0][2]) == 1
                        else ["NP1003"]
                    },
                },
            ),
        )
    )
    return theorems, vectors


def _verify_domain_3() -> tuple[list[TheoremResult], list[VectorWitness]]:
    theorems: list[TheoremResult] = []
    vectors: list[VectorWitness] = []
    prods = range(MAX_PRODS)

    in_has = [Bool(f"d3_in_has_{p}") for p in prods]
    target_has = [Bool(f"d3_target_has_{p}") for p in prods]
    has_uncond = [Bool(f"d3_uncond_{p}") for p in prods]
    has_guarded = [Bool(f"d3_guarded_{p}") for p in prods]
    ret_paths = [Int(f"d3_ret_paths_{p}") for p in prods]
    expr_gfp = Bool("d3_expr_gfp")

    def prod_synth_ok(prod_idx: int, self_ok: BoolRef) -> BoolRef:
        return (
            target_has[0]
            if prod_idx == 0
            else And(target_has[prod_idx], self_ok)
        )

    def expr_step(self_ok: BoolRef) -> BoolRef:
        return And(
            *[
                Implies(in_has[p], Or(has_uncond[p], prod_synth_ok(p, self_ok)))
                for p in prods
            ]
        )

    axioms = [
        in_has[0],
        *[
            And(ret_paths[p] >= 0, ret_paths[p] <= AMBIGUOUS_PATHS)
            for p in prods
        ],
        expr_gfp == expr_step(expr_gfp),
        *[
            Implies(Implies(cand, expr_step(cand)), Implies(cand, expr_gfp))
            for cand in (BoolVal(val=False), BoolVal(val=True))
        ],
    ]
    synth_ok = [prod_synth_ok(p, expr_gfp) for p in prods]
    explicit = [Or(has_uncond[p], has_guarded[p]) for p in prods]
    np1001_unhandled = [
        And(
            in_has[p],
            Not(has_uncond[p]),
            Not(has_guarded[p]),
            Not(synth_ok[p]),
        )
        for p in prods
    ]
    np1001_guarded = [
        And(in_has[p], Not(has_uncond[p]), has_guarded[p], Not(synth_ok[p]))
        for p in prods
    ]
    np1001 = [Or(np1001_unhandled[p], np1001_guarded[p]) for p in prods]
    np1003 = [
        And(in_has[p], explicit[p], ret_paths[p] == AMBIGUOUS_PATHS)
        for p in prods
    ]
    bad_ret = [And(in_has[p], explicit[p], ret_paths[p] == 0) for p in prods]
    compile_ok = And(
        *[Not(Or(np1001[p], np1003[p], bad_ret[p])) for p in prods]
    )

    def step_ok(
        prod_idx: int, children_ok: BoolRef, g_vals: list[BoolRef]
    ) -> BoolRef:
        taken_guard = And(has_guarded[prod_idx], g_vals[prod_idx])
        return Or(
            And(taken_guard, ret_paths[prod_idx] == 1),
            And(
                Not(taken_guard), has_uncond[prod_idx], ret_paths[prod_idx] == 1
            ),
            And(
                Not(taken_guard),
                Not(has_uncond[prod_idx]),
                target_has[prod_idx],
                children_ok,
            ),
        )

    true_lit = BoolVal(val=True)
    safe_terms: list[BoolRef] = []
    for mask in range(GUARD_MASKS):
        g_vals = [BoolVal(bool((mask >> p) & 1)) for p in prods]
        leaf_ok = [step_ok(c, true_lit, g_vals) for c in prods]
        for r in prods:
            for c1 in prods:
                for c2 in prods:
                    kids_ok = (
                        true_lit
                        if r == 0
                        else (
                            leaf_ok[c1]
                            if r == UNARY_PROD
                            else And(leaf_ok[c1], leaf_ok[c2])
                        )
                    )
                    safe_terms.append(
                        Implies(
                            And(in_has[r], in_has[c1], in_has[c2]),
                            step_ok(r, kids_ok, g_vals),
                        )
                    )
    runtime_safe = And(*safe_terms)

    theorems.append(
        _prove_non_vacuous(
            "D3-T1",
            "Compile-time acceptance guarantees trap-free runtime execution",
            [*axioms, compile_ok],
            runtime_safe,
            witness=[in_has[1], target_has[0], target_has[1]],
        )
    )

    valid_rets = And(*[ret_paths[p] == 1 for p in prods])
    exhaust_diag = Or(*[np1001[p] for p in prods])
    theorems.append(
        _prove_non_vacuous(
            "D3-T2",
            "NP1001 fires iff some AST and guard valuation traps",
            [*axioms, valid_rets],
            exhaust_diag == Not(runtime_safe),
            witness=[in_has[1], target_has[0], Not(target_has[1])],
        )
    )

    extra_theorem, extra_vec = _verify_domain_3_extra_return()
    nested_theorem, nested_vecs = _verify_domain_3_nested_subtype()
    theorems.extend((extra_theorem, nested_theorem))

    base_d3 = [*axioms, valid_rets, target_has[0], in_has[1], Not(in_has[2])]
    vectors.extend(
        _decode_vector(spec)
        for spec in (
            VectorSpec(
                "VEC-D3-01",
                "D3",
                "Homomorphic auto-generation across unchanged productions",
                "D3-T1",
                [
                    *base_d3,
                    target_has[1],
                    Not(has_guarded[1]),
                    Not(has_uncond[1]),
                ],
                lambda m: {
                    "decoded_witness": {
                        "expr_gfp": _eval_bool(m, expr_gfp),
                        "compile_ok": _eval_bool(m, compile_ok),
                        "runtime_safe": _eval_bool(m, runtime_safe),
                    },
                    "upstream_expected": {
                        "compile_valid": True,
                        "runtime_safe": _eval_bool(m, runtime_safe),
                    },
                    "csharp_expected": {
                        "diagnostics": []
                        if _eval_bool(m, compile_ok)
                        else ["NP1001"]
                    },
                },
            ),
            VectorSpec(
                "VEC-D3-02",
                "D3",
                "Guarded clause on removed production without fallback",
                "D3-T2",
                [
                    *base_d3,
                    Not(target_has[1]),
                    has_guarded[1],
                    Not(has_uncond[1]),
                ],
                lambda m: {
                    "decoded_witness": {
                        "np1001_guarded": _eval_bool(m, np1001_guarded[1]),
                        "runtime_safe": _eval_bool(m, runtime_safe),
                    },
                    "upstream_expected": {
                        "compile_valid": True,
                        "runtime_safe": _eval_bool(m, runtime_safe),
                    },
                    "csharp_expected": {
                        "diagnostics": ["NP1001"]
                        if _eval_bool(m, np1001_guarded[1])
                        else []
                    },
                },
                divergence_note=(
                    "Intentional C# compile-time exhaustiveness refinement: "
                    "reports NP1001 at compile time instead of trapping when "
                    "the guard evaluates to false."
                ),
            ),
            VectorSpec(
                "VEC-D3-03",
                "D3",
                "Production removed in target language with no explicit clause",
                "D3-T2",
                [
                    *base_d3,
                    Not(target_has[1]),
                    Not(has_guarded[1]),
                    Not(has_uncond[1]),
                ],
                lambda m: {
                    "decoded_witness": {
                        "np1001": _eval_bool(m, np1001_unhandled[1]),
                        "runtime_safe": _eval_bool(m, runtime_safe),
                    },
                    "upstream_expected": {
                        "compile_valid": True,
                        "runtime_safe": _eval_bool(m, runtime_safe),
                    },
                    "csharp_expected": {
                        "diagnostics": ["NP1001"]
                        if _eval_bool(m, np1001_unhandled[1])
                        else []
                    },
                },
                divergence_note=(
                    "Intentional C# compile-time exhaustiveness refinement: "
                    "reports NP1001 at compile time instead of generating a "
                    "runtime unrecognized-form trap."
                ),
            ),
            VectorSpec(
                "VEC-D3-04",
                "D3",
                "Guarded clause on production retained in target language",
                "D3-T1",
                [*base_d3, target_has[1], has_guarded[1], Not(has_uncond[1])],
                lambda m: {
                    "decoded_witness": {
                        "expr_gfp": _eval_bool(m, expr_gfp),
                        "compile_ok": _eval_bool(m, compile_ok),
                        "runtime_safe": _eval_bool(m, runtime_safe),
                    },
                    "upstream_expected": {
                        "compile_valid": True,
                        "runtime_safe": _eval_bool(m, runtime_safe),
                    },
                    "csharp_expected": {
                        "diagnostics": []
                        if _eval_bool(m, compile_ok)
                        else ["NP1001"]
                    },
                },
            ),
        )
    )
    vectors.append(extra_vec)
    vectors.extend(nested_vecs)
    return theorems, vectors


def _verify_domain_3_extra_return() -> tuple[TheoremResult, VectorWitness]:
    extra_arity = Int("d3_extra_arity")
    multi_bind = Bool("d3_multi_bind")
    sub_removed = Bool("d3_sub_removed")
    tuple_covers_removed = Bool("d3_tuple_covers_removed")

    no_xval_uses_explicit = extra_arity == 0
    hypothetical_dropped = If(multi_bind, IntVal(0), extra_arity)
    actual_dropped = If(
        multi_bind,
        IntVal(0),
        If(no_xval_uses_explicit, extra_arity, IntVal(0)),
    )
    explicit_sub_complete = Or(Not(sub_removed), tuple_covers_removed)
    fallback_sub_complete = Not(sub_removed)

    upstream_compile_valid = BoolVal(val=True)
    upstream_runtime_safe = If(
        Or(multi_bind, no_xval_uses_explicit),
        explicit_sub_complete,
        fallback_sub_complete,
    )
    csharp_compile_ok = If(
        Or(multi_bind, no_xval_uses_explicit),
        explicit_sub_complete,
        fallback_sub_complete,
    )

    theorem = _prove_non_vacuous(
        "D3-T3",
        "no-xval? fallback isolates tuple clauses and prevents value loss",
        [extra_arity >= 0],
        And(
            actual_dropped == 0,
            And(Not(multi_bind), extra_arity > 0) == (hypothetical_dropped > 0),
            csharp_compile_ok == upstream_runtime_safe,
            Implies(
                And(
                    Not(multi_bind),
                    extra_arity > 0,
                    sub_removed,
                    tuple_covers_removed,
                ),
                And(
                    upstream_compile_valid,
                    Not(upstream_runtime_safe),
                    Not(csharp_compile_ok),
                ),
            ),
        ),
        witness=[
            extra_arity == 1,
            Not(multi_bind),
            sub_removed,
            tuple_covers_removed,
        ],
    )
    vector = _decode_vector(
        VectorSpec(
            "VEC-D3-05",
            "D3",
            "Auto-generated parent recursing into unbound [ExtraReturn]",
            "D3-T3",
            [
                extra_arity == 1,
                Not(multi_bind),
                sub_removed,
                tuple_covers_removed,
            ],
            lambda m: {
                "decoded_witness": {
                    "csharp_compile_ok": _eval_bool(m, csharp_compile_ok),
                    "upstream_compile_valid": _eval_bool(
                        m, upstream_compile_valid
                    ),
                    "upstream_runtime_safe": _eval_bool(
                        m, upstream_runtime_safe
                    ),
                    "dropped_if_unsafely_used": _eval_int(
                        m, hypothetical_dropped
                    ),
                },
                "upstream_expected": {
                    "compile_valid": _eval_bool(m, upstream_compile_valid),
                    "runtime_safe": _eval_bool(m, upstream_runtime_safe),
                },
                "csharp_expected": {
                    "diagnostics": []
                    if _eval_bool(m, csharp_compile_ok)
                    else ["NP1001"]
                },
            },
            divergence_note=(
                "Intentional C# compile-time exhaustiveness refinement: "
                "upstream no-xval? skips the tuple processor and synthesizes "
                "a separate 0-extra-return Sub -> Sub processor that compiles "
                "and traps at runtime on removed Sub productions, whereas C# "
                "reports NP1001 at compile time."
            ),
        )
    )
    return theorem, vector


def _gfp_5_constraints(
    vars_5: tuple[BoolRef, BoolRef, BoolRef, BoolRef, BoolRef],
    step_fn: Callable[
        [BoolRef, BoolRef, BoolRef, BoolRef, BoolRef],
        tuple[BoolRef, BoolRef, BoolRef, BoolRef, BoolRef],
    ],
) -> list[BoolRef]:
    lattice_5 = [
        tuple(BoolVal(val=bool((i >> bit) & 1)) for bit in range(5))
        for i in range(32)
    ]
    fp = step_fn(*vars_5)
    return [
        *[vars_5[k] == fp[k] for k in range(5)],
        *[
            Implies(
                And(*[Implies(c[k], step_fn(*c)[k]) for k in range(5)]),
                And(*[Implies(c[k], vars_5[k]) for k in range(5)]),
            )
            for c in lattice_5
        ],
    ]


def _verify_domain_3_nested_subtype() -> tuple[
    TheoremResult, list[VectorWitness]
]:
    (
        src_a_inc_b,
        src_a_wrap,
        src_b_keep,
        src_b_gone,
        src_b_loop,
        dst_a_inc_b,
        dst_a_direct_keep,
        dst_a_wrap,
        dst_alt_wrap,
        dst_b_keep,
        dst_b_keep_ambig,
        dst_b_gone,
        dst_b_loop,
        dst_root_inc_b,
        dst_root_inc_alt,
        dst_root_direct_keep,
        dst_root_gone,
        dst_root_loop,
    ) = Bools(
        "d3_src_a_inc_b d3_src_a_wrap d3_src_b_keep d3_src_b_gone "
        "d3_src_b_loop d3_dst_a_inc_b d3_dst_a_direct_keep d3_dst_a_wrap "
        "d3_dst_alt_wrap d3_dst_b_keep d3_dst_b_keep_ambig d3_dst_b_gone "
        "d3_dst_b_loop d3_dst_root_inc_b d3_dst_root_inc_alt "
        "d3_dst_root_direct_keep d3_dst_root_gone d3_dst_root_loop"
    )
    (
        outer_exists,
        outer_bindable,
        outer_tuple,
        b_proc_exists,
        b_proc_bindable,
        b_proc_tuple,
        b_proc_covers_gone,
        anon_rw_b_gone,
    ) = Bools(
        "d3_outer_exists d3_outer_bindable d3_outer_tuple d3_b_proc_exists "
        "d3_b_proc_bindable d3_b_proc_tuple d3_b_proc_covers_gone "
        "d3_anon_rw_b_gone"
    )
    valid_outer = And(outer_exists, outer_bindable, Not(outer_tuple))
    valid_b_proc = And(b_proc_exists, b_proc_bindable, Not(b_proc_tuple))
    dst_r_reaches_b = Or(dst_root_inc_b, dst_a_inc_b)
    b_proc_for_a = And(valid_b_proc, dst_a_inc_b)
    b_proc_for_r = And(valid_b_proc, dst_r_reaches_b)

    e_a_to_a, e_a_to_r, e_b_to_a, e_b_to_b, e_b_to_r = Bools(
        "d3_e_a_to_a d3_e_a_to_r d3_e_b_to_a d3_e_b_to_b d3_e_b_to_r"
    )
    v_a_to_a, v_a_to_r, v_b_to_a, v_b_to_b, v_b_to_r = Bools(
        "d3_v_a_to_a d3_v_a_to_r d3_v_b_to_a d3_v_b_to_b d3_v_b_to_r"
    )
    s_a_to_a, s_a_to_r, s_b_to_a, s_b_to_b, s_b_to_r = Bools(
        "d3_s_a_to_a d3_s_a_to_r d3_s_b_to_a d3_s_b_to_b d3_s_b_to_r"
    )
    e_vars = (e_a_to_a, e_a_to_r, e_b_to_a, e_b_to_b, e_b_to_r)
    v_vars = (v_a_to_a, v_a_to_r, v_b_to_a, v_b_to_b, v_b_to_r)
    s_vars = (s_a_to_a, s_a_to_r, s_b_to_a, s_b_to_b, s_b_to_r)

    def step_exist(
        x_a_to_a: BoolRef,
        x_a_to_r: BoolRef,
        x_b_to_a: BoolRef,
        x_b_to_b: BoolRef,
        x_b_to_r: BoolRef,
    ) -> tuple[BoolRef, BoolRef, BoolRef, BoolRef, BoolRef]:
        b_field = Or(valid_b_proc, x_b_to_b)
        return (
            And(
                Implies(
                    src_a_inc_b,
                    Or(b_proc_for_a, And(dst_a_inc_b, x_b_to_b), x_b_to_a),
                ),
                Implies(src_a_wrap, And(dst_a_wrap, b_field)),
            ),
            And(
                Implies(
                    src_a_inc_b,
                    Or(b_proc_for_r, And(dst_r_reaches_b, x_b_to_b), x_b_to_r),
                ),
                Implies(
                    src_a_wrap,
                    Or(
                        And(dst_a_wrap, b_field),
                        And(dst_alt_wrap, dst_root_inc_alt, x_b_to_r),
                    ),
                ),
            ),
            And(
                Implies(
                    src_b_keep,
                    Or(dst_a_direct_keep, And(dst_a_inc_b, dst_b_keep)),
                ),
                Implies(
                    src_b_gone,
                    And(dst_a_inc_b, Or(anon_rw_b_gone, dst_b_gone)),
                ),
                Implies(src_b_loop, And(dst_a_inc_b, dst_b_loop, x_a_to_a)),
            ),
            And(
                Implies(src_b_keep, dst_b_keep),
                Implies(src_b_gone, Or(anon_rw_b_gone, dst_b_gone)),
                Implies(src_b_loop, And(dst_b_loop, x_a_to_a)),
            ),
            And(
                Implies(
                    src_b_keep,
                    Or(
                        dst_root_direct_keep,
                        dst_a_direct_keep,
                        And(dst_b_keep, dst_r_reaches_b),
                    ),
                ),
                Implies(
                    src_b_gone,
                    Or(
                        dst_root_gone,
                        And(dst_r_reaches_b, Or(anon_rw_b_gone, dst_b_gone)),
                    ),
                ),
                Implies(
                    src_b_loop,
                    Or(
                        And(dst_root_loop, x_a_to_r),
                        And(dst_b_loop, dst_r_reaches_b, x_a_to_a),
                    ),
                ),
            ),
        )

    w_b_exist = And(dst_a_wrap, Or(valid_b_proc, e_b_to_b))
    w_r_exist = And(dst_alt_wrap, dst_root_inc_alt, e_b_to_r)
    loop_r_exist = And(dst_root_loop, e_a_to_r)
    loop_b_exist = And(dst_b_loop, dst_r_reaches_b, e_a_to_a)
    true_lit = BoolVal(val=True)
    choose_inc_b_to_a = And(dst_a_inc_b, Or(v_b_to_b, Not(v_b_to_a)))
    choose_inc_b_to_r = And(dst_r_reaches_b, Or(v_b_to_b, Not(v_b_to_r)))

    def make_step_stage2(
        *,
        procedural: bool,
    ) -> Callable[
        [BoolRef, BoolRef, BoolRef, BoolRef, BoolRef],
        tuple[BoolRef, BoolRef, BoolRef, BoolRef, BoolRef],
    ]:
        def step(
            x_a_to_a: BoolRef,
            x_a_to_r: BoolRef,
            x_b_to_a: BoolRef,
            x_b_to_b: BoolRef,
            x_b_to_r: BoolRef,
        ) -> tuple[BoolRef, BoolRef, BoolRef, BoolRef, BoolRef]:
            b_f = Or(valid_b_proc, x_b_to_b)
            inc_a_to_a = (
                If(choose_inc_b_to_a, x_b_to_b, x_b_to_a)
                if procedural
                else Or(And(dst_a_inc_b, x_b_to_b), x_b_to_a)
            )
            inc_a_to_r = (
                If(choose_inc_b_to_r, x_b_to_b, x_b_to_r)
                if procedural
                else Or(And(dst_r_reaches_b, x_b_to_b), x_b_to_r)
            )
            return (
                And(
                    Implies(src_a_inc_b, Or(b_proc_for_a, inc_a_to_a)),
                    Implies(src_a_wrap, And(dst_a_wrap, b_f)),
                ),
                And(
                    Implies(src_a_inc_b, Or(b_proc_for_r, inc_a_to_r)),
                    Implies(
                        src_a_wrap,
                        If(w_b_exist, b_f, And(w_r_exist, x_b_to_r)),
                    ),
                ),
                And(
                    Implies(
                        src_b_keep,
                        If(
                            dst_a_direct_keep,
                            true_lit,
                            And(dst_a_inc_b, dst_b_keep, Not(dst_b_keep_ambig)),
                        ),
                    ),
                    Implies(
                        src_b_gone,
                        And(dst_a_inc_b, Or(anon_rw_b_gone, dst_b_gone)),
                    ),
                    Implies(src_b_loop, And(dst_a_inc_b, dst_b_loop, x_a_to_a)),
                ),
                And(
                    Implies(src_b_keep, And(dst_b_keep, Not(dst_b_keep_ambig))),
                    Implies(src_b_gone, Or(anon_rw_b_gone, dst_b_gone)),
                    Implies(src_b_loop, And(dst_b_loop, x_a_to_a)),
                ),
                And(
                    Implies(
                        src_b_keep,
                        If(
                            Or(dst_root_direct_keep, dst_a_direct_keep),
                            true_lit,
                            And(
                                dst_b_keep,
                                dst_r_reaches_b,
                                Not(dst_b_keep_ambig),
                            ),
                        ),
                    ),
                    Implies(
                        src_b_gone,
                        Or(
                            dst_root_gone,
                            And(
                                dst_r_reaches_b, Or(anon_rw_b_gone, dst_b_gone)
                            ),
                        ),
                    ),
                    Implies(
                        src_b_loop,
                        If(loop_r_exist, x_a_to_r, And(loop_b_exist, x_a_to_a)),
                    ),
                ),
            )

        return step

    gfp_axioms = [
        *_gfp_5_constraints(e_vars, step_exist),
        *_gfp_5_constraints(v_vars, make_step_stage2(procedural=False)),
        *_gfp_5_constraints(s_vars, make_step_stage2(procedural=True)),
    ]

    choose_sub_a = And(Not(valid_outer), Or(v_a_to_a, Not(v_a_to_r)))
    synth_p_root = Or(valid_outer, If(choose_sub_a, s_a_to_a, s_a_to_r))
    ideal_p_root = Or(valid_outer, s_a_to_a, s_a_to_r)
    buggy_exist_inc_a_to_a = And(
        Implies(
            src_a_inc_b,
            Or(
                b_proc_for_a,
                If(And(dst_a_inc_b, e_b_to_b), v_b_to_b, v_b_to_a),
            ),
        ),
        Implies(src_a_wrap, And(dst_a_wrap, Or(valid_b_proc, v_b_to_b))),
    )
    ambig_inner_scenario = And(
        src_a_inc_b,
        dst_a_inc_b,
        Not(dst_root_inc_b),
        src_b_keep,
        dst_b_keep,
        dst_b_keep_ambig,
        dst_a_direct_keep,
        dst_root_direct_keep,
        Not(src_a_wrap),
        Not(src_b_gone),
        Not(src_b_loop),
        Not(valid_outer),
        Not(b_proc_exists),
    )
    both_wrappers_scenario = And(
        src_a_inc_b,
        src_a_wrap,
        src_b_keep,
        Not(src_b_gone),
        Not(src_b_loop),
        Not(dst_a_inc_b),
        Not(dst_a_direct_keep),
        dst_a_wrap,
        dst_alt_wrap,
        dst_b_keep,
        Not(dst_b_keep_ambig),
        Not(dst_b_gone),
        Not(dst_root_inc_b),
        dst_root_inc_alt,
        dst_root_direct_keep,
        Not(dst_root_gone),
        Not(dst_root_loop),
        Not(outer_exists),
        Not(b_proc_exists),
        Not(anon_rw_b_gone),
    )

    theorem = _prove_non_vacuous(
        "D3-T4",
        "Two-stage GFP memo equals procedural synthesis & preserves fallback",
        gfp_axioms,
        And(
            *[v_vars[k] == s_vars[k] for k in range(5)],
            synth_p_root == ideal_p_root,
            Implies(And(Not(valid_outer), s_a_to_a), choose_sub_a),
            Implies(
                ambig_inner_scenario,
                And(
                    e_b_to_b,
                    Not(v_b_to_b),
                    v_b_to_a,
                    v_b_to_r,
                    v_a_to_a,
                    v_a_to_r,
                    s_a_to_a,
                    choose_sub_a,
                    Not(buggy_exist_inc_a_to_a),
                ),
            ),
            Implies(
                both_wrappers_scenario,
                And(
                    w_b_exist,
                    w_r_exist,
                    Not(v_a_to_a),
                    v_a_to_r,
                    s_a_to_r,
                    Not(choose_sub_a),
                    synth_p_root,
                ),
            ),
            Implies(
                And(
                    b_proc_exists,
                    b_proc_tuple,
                    b_proc_covers_gone,
                    src_b_gone,
                    Not(dst_b_gone),
                    Not(anon_rw_b_gone),
                ),
                Not(e_b_to_b),
            ),
        ),
        witness=[ambig_inner_scenario],
    )
    vec_06 = _decode_vector(
        VectorSpec(
            "VEC-D3-06",
            "D3",
            "Nested upward-migrated field falls back to A -> Root",
            "D3-T4",
            [
                *gfp_axioms,
                Not(src_a_inc_b),
                src_a_wrap,
                src_b_keep,
                src_b_gone,
                Not(src_b_loop),
                Not(dst_a_inc_b),
                Not(dst_a_direct_keep),
                dst_a_wrap,
                dst_alt_wrap,
                dst_b_keep,
                Not(dst_b_keep_ambig),
                Not(dst_b_gone),
                dst_root_inc_b,
                dst_root_inc_alt,
                Not(dst_root_direct_keep),
                dst_root_gone,
                Not(dst_root_loop),
                Not(outer_exists),
                Not(b_proc_exists),
                Not(anon_rw_b_gone),
            ],
            lambda m: {
                "decoded_witness": {
                    "can_lower_a_to_a": _eval_bool(m, v_a_to_a),
                    "can_lower_a_to_root": _eval_bool(m, v_a_to_r),
                    "fixed_synth_ok": _eval_bool(m, synth_p_root),
                },
                "upstream_expected": {
                    "compile_valid": _eval_bool(m, ideal_p_root)
                },
                "csharp_expected": {
                    "diagnostics": []
                    if _eval_bool(m, synth_p_root)
                    else ["NP1001"]
                },
            },
        )
    )
    vec_07 = _decode_vector(
        VectorSpec(
            "VEC-D3-07",
            "D3",
            "Ambiguous inner inclusion falls back to outer while preserving A",
            "D3-T4",
            [*gfp_axioms, ambig_inner_scenario],
            lambda m: {
                "decoded_witness": {
                    "inner_existential": _eval_bool(m, e_b_to_b),
                    "v_a_to_a": _eval_bool(m, v_a_to_a),
                    "v_a_to_r": _eval_bool(m, v_a_to_r),
                    "v_b_to_a": _eval_bool(m, v_b_to_a),
                    "v_b_to_b": _eval_bool(m, v_b_to_b),
                    "v_b_to_r": _eval_bool(m, v_b_to_r),
                    "choose_sub_a": _eval_bool(m, choose_sub_a),
                    "synth_p_root": _eval_bool(m, synth_p_root),
                },
                "upstream_expected": {
                    "compile_valid": _eval_bool(m, ideal_p_root)
                },
                "csharp_expected": {
                    "diagnostics": []
                    if _eval_bool(m, synth_p_root)
                    else ["NP1001"]
                },
            },
        )
    )
    vec_08 = _decode_vector(
        VectorSpec(
            "VEC-D3-08",
            "D3",
            "Both wrapper candidates feasible prefers lower-cost A.Wrap(b)",
            "D3-T4",
            [*gfp_axioms, both_wrappers_scenario],
            lambda m: {
                "decoded_witness": {
                    "w_b_exist": _eval_bool(m, w_b_exist),
                    "w_r_exist": _eval_bool(m, w_r_exist),
                    "v_a_to_a": _eval_bool(m, v_a_to_a),
                    "v_a_to_r": _eval_bool(m, v_a_to_r),
                    "choose_sub_a": _eval_bool(m, choose_sub_a),
                    "synth_p_root": _eval_bool(m, synth_p_root),
                },
                "upstream_expected": {
                    "compile_valid": _eval_bool(m, ideal_p_root)
                },
                "csharp_expected": {
                    "diagnostics": []
                    if _eval_bool(m, synth_p_root)
                    else ["NP1001"]
                },
            },
        )
    )
    return theorem, [vec_06, vec_07, vec_08]


def _build_domain_4_sorts() -> tuple[
    DatatypeSortRef, DatatypeSortRef, DatatypeSortRef
]:
    sexp_dt = Datatype("SExp")
    sexp_dt.declare("Atom", ("atom_id", IntSort()))
    sexp_dt.declare("List2", ("head_id", IntSort()), ("arg_id", IntSort()))
    sexp_dt.declare(
        "List3", ("a0", IntSort()), ("a1", IntSort()), ("a2", IntSort())
    )
    sexp_dt.declare(
        "List4",
        ("b0", IntSort()),
        ("b1", IntSort()),
        ("b2", IntSort()),
        ("b3", IntSort()),
    )
    sexp_sort = sexp_dt.create()

    node_dt = Datatype("AstNode")
    node_dt.declare("RootSym", ("sym_id", IntSort()))
    node_dt.declare("RootNeg", ("arg_sym", IntSort()))
    node_dt.declare(
        "RootPair", ("left_sym", IntSort()), ("right_sym", IntSort())
    )
    node_dt.declare("WrapSubNum")
    node_dt.declare("WrapSubSym", ("sub_sym", IntSort()))
    node_dt.declare("WrapSubNeg")
    ast_sort = node_dt.create()

    out_dt = Datatype("ParseOutcome")
    out_dt.declare("Ok", ("node", ast_sort))
    out_dt.declare("Err")
    out_sort = out_dt.create()
    return sexp_sort, ast_sort, out_sort


def _verify_domain_4_pretty(
    sexp_sort: DatatypeSortRef,
    sym_x: ExprRef,
    kw_neg: ExprRef,
    num_42: ExprRef,
) -> tuple[TheoremResult, ExprRef, ExprRef, ExprRef, ExprRef]:
    pretty_dt = Datatype("PrettyNode")
    pretty_dt.declare(
        "LetForm",
        ("let_fn", IntSort()),
        ("let_a1", IntSort()),
        ("let_a2", IntSort()),
    )
    pretty_dt.declare(
        "AppForm",
        ("app_fn", IntSort()),
        ("app_a1", IntSort()),
        ("app_a2", IntSort()),
    )
    pretty_sort = pretty_dt.create()

    def unparse_raw_p(item: ExprRef) -> ExprRef:
        return If(
            pretty_sort.is_LetForm(item),
            sexp_sort.List4(
                kw_neg,
                pretty_sort.let_fn(item),
                pretty_sort.let_a1(item),
                pretty_sort.let_a2(item),
            ),
            sexp_sort.List3(
                pretty_sort.app_fn(item),
                pretty_sort.app_a1(item),
                pretty_sort.app_a2(item),
            ),
        )

    def unparse_pretty_p(item: ExprRef) -> ExprRef:
        return If(
            pretty_sort.is_LetForm(item),
            sexp_sort.List3(
                pretty_sort.let_fn(item),
                pretty_sort.let_a1(item),
                pretty_sort.let_a2(item),
            ),
            sexp_sort.List3(
                pretty_sort.app_fn(item),
                pretty_sort.app_a1(item),
                pretty_sort.app_a2(item),
            ),
        )

    pretty_candidates = [
        *[
            pretty_sort.LetForm(IntVal(f), IntVal(a1), IntVal(a2))
            for f in range(ATOM_COUNT)
            for a1 in range(ATOM_COUNT)
            for a2 in range(ATOM_COUNT)
        ],
        *[
            pretty_sort.AppForm(IntVal(f), IntVal(a1), IntVal(a2))
            for f in range(ATOM_COUNT)
            for a1 in range(ATOM_COUNT)
            for a2 in range(ATOM_COUNT)
        ],
    ]
    raw_injective = And(
        *[
            Implies(unparse_raw_p(q1) == unparse_raw_p(q2), q1 == q2)
            for i, q1 in enumerate(pretty_candidates)
            for q2 in pretty_candidates[i + 1 :]
        ]
    )
    let_node = pretty_sort.LetForm(sym_x, num_42, sym_x)
    app_node = pretty_sort.AppForm(sym_x, num_42, sym_x)
    raw_let = unparse_raw_p(let_node)
    raw_app = unparse_raw_p(app_node)
    pretty_let = unparse_pretty_p(let_node)
    pretty_app = unparse_pretty_p(app_node)
    any_parser = Function("d4_any_parser", sexp_sort, pretty_sort)
    theorem = _prove_non_vacuous(
        "D4-T3",
        "Tag-erasing [Pretty] formats collide while raw=#t stays injective",
        [],
        And(
            raw_injective,
            let_node != app_node,
            pretty_let == pretty_app,
            Not(
                And(
                    any_parser(pretty_let) == let_node,
                    any_parser(pretty_app) == app_node,
                )
            ),
        ),
    )
    return theorem, raw_let, raw_app, pretty_let, pretty_app


def _verify_domain_4() -> tuple[list[TheoremResult], list[VectorWitness]]:
    sym_x, kw_neg, num_42 = IntVal(0), IntVal(1), IntVal(2)
    sexp_sort, ast_sort, out_sort = _build_domain_4_sorts()

    root_sym = Bool("d4_root_sym")
    kw_as_sym = Bool("d4_kw_as_sym")
    root_neg = Bool("d4_root_neg")
    root_pair = Bool("d4_root_pair")
    inc_sub = Bool("d4_inc_sub")
    sub_imp = Bool("d4_sub_imp")
    sub_num = Bool("d4_sub_num")
    sub_sym = Bool("d4_sub_sym")
    sub_neg = Bool("d4_sub_neg")

    def valid_sym(atom_id: ExprRef) -> BoolRef:
        return Or(atom_id == sym_x, And(atom_id == kw_neg, kw_as_sym))

    def valid_arg(atom_id: ExprRef) -> BoolRef:
        return Or(
            And(root_sym, valid_sym(atom_id)),
            And(inc_sub, sub_num, atom_id == num_42),
            And(inc_sub, sub_sym, valid_sym(atom_id)),
        )

    def inhabited(node: ExprRef) -> BoolRef:
        return Or(
            And(
                ast_sort.is_RootSym(node),
                root_sym,
                valid_sym(ast_sort.sym_id(node)),
            ),
            And(
                ast_sort.is_RootNeg(node),
                root_neg,
                valid_arg(ast_sort.arg_sym(node)),
            ),
            And(
                ast_sort.is_RootPair(node),
                root_pair,
                valid_arg(ast_sort.left_sym(node)),
                valid_arg(ast_sort.right_sym(node)),
            ),
            And(ast_sort.is_WrapSubNum(node), inc_sub, sub_num),
            And(
                ast_sort.is_WrapSubSym(node),
                inc_sub,
                sub_sym,
                valid_sym(ast_sort.sub_sym(node)),
            ),
            And(ast_sort.is_WrapSubNeg(node), inc_sub, sub_neg),
        )

    def unparse_raw(node: ExprRef) -> ExprRef:
        return If(
            ast_sort.is_RootSym(node),
            sexp_sort.Atom(ast_sort.sym_id(node)),
            If(
                ast_sort.is_RootNeg(node),
                sexp_sort.List2(kw_neg, ast_sort.arg_sym(node)),
                If(
                    ast_sort.is_RootPair(node),
                    sexp_sort.List2(
                        ast_sort.left_sym(node), ast_sort.right_sym(node)
                    ),
                    If(
                        ast_sort.is_WrapSubNum(node),
                        sexp_sort.Atom(num_42),
                        If(
                            ast_sort.is_WrapSubSym(node),
                            sexp_sort.Atom(ast_sort.sub_sym(node)),
                            sexp_sort.List2(kw_neg, num_42),
                        ),
                    ),
                ),
            ),
        )

    def parse_root(sexp: ExprRef) -> ExprRef:
        sub_out = If(
            And(
                sexp_sort.is_Atom(sexp),
                sub_num,
                sexp_sort.atom_id(sexp) == num_42,
            ),
            out_sort.Ok(ast_sort.WrapSubNum),
            If(
                And(
                    sexp_sort.is_Atom(sexp),
                    sub_sym,
                    valid_sym(sexp_sort.atom_id(sexp)),
                ),
                out_sort.Ok(ast_sort.WrapSubSym(sexp_sort.atom_id(sexp))),
                If(
                    And(
                        sexp_sort.is_List2(sexp),
                        sub_neg,
                        sexp_sort.head_id(sexp) == kw_neg,
                        sexp_sort.arg_id(sexp) == num_42,
                    ),
                    out_sort.Ok(ast_sort.WrapSubNeg),
                    out_sort.Err,
                ),
            ),
        )
        return If(
            And(inc_sub, Not(sub_imp), out_sort.is_Ok(sub_out)),
            sub_out,
            If(
                And(
                    sexp_sort.is_List2(sexp),
                    root_neg,
                    sexp_sort.head_id(sexp) == kw_neg,
                    valid_arg(sexp_sort.arg_id(sexp)),
                ),
                out_sort.Ok(ast_sort.RootNeg(sexp_sort.arg_id(sexp))),
                If(
                    And(
                        sexp_sort.is_Atom(sexp),
                        root_sym,
                        valid_sym(sexp_sort.atom_id(sexp)),
                    ),
                    out_sort.Ok(ast_sort.RootSym(sexp_sort.atom_id(sexp))),
                    If(
                        And(
                            sexp_sort.is_List2(sexp),
                            root_pair,
                            valid_arg(sexp_sort.head_id(sexp)),
                            valid_arg(sexp_sort.arg_id(sexp)),
                        ),
                        out_sort.Ok(
                            ast_sort.RootPair(
                                sexp_sort.head_id(sexp), sexp_sort.arg_id(sexp)
                            )
                        ),
                        If(And(inc_sub, sub_imp), sub_out, out_sort.Err),
                    ),
                ),
            ),
        )

    candidates = (
        [ast_sort.RootSym(IntVal(a)) for a in range(ATOM_COUNT)]
        + [ast_sort.RootNeg(IntVal(a)) for a in range(ATOM_COUNT)]
        + [
            ast_sort.RootPair(IntVal(a), IntVal(b))
            for a in range(ATOM_COUNT)
            for b in range(ATOM_COUNT)
        ]
        + [ast_sort.WrapSubNum]
        + [ast_sort.WrapSubSym(IntVal(a)) for a in range(ATOM_COUNT)]
        + [ast_sort.WrapSubNeg]
    )
    roundtrip = And(
        *[
            Implies(inhabited(n), parse_root(unparse_raw(n)) == out_sort.Ok(n))
            for n in candidates
        ]
    )
    kw_arg = valid_arg(kw_neg)
    num_arg = valid_arg(num_42)
    any_arg = Or(valid_arg(sym_x), kw_arg, num_arg)
    no_shadow = And(
        Not(And(root_neg, root_pair, kw_arg, any_arg)),
        Not(And(inc_sub, sub_sym, root_sym)),
        Not(And(inc_sub, sub_neg, root_neg, num_arg)),
        Not(And(inc_sub, sub_neg, root_pair, kw_arg, num_arg)),
    )

    c_is_nt = Bool("d4_c_nt")
    c_is_term = Bool("d4_c_term")
    c_is_pair = Bool("d4_c_pair")
    c_kw_head = Bool("d4_c_kw")
    c_nt_has_imp = Bool("d4_c_nt_imp")
    valid_clause = (
        Sum(
            [
                If(c_is_nt, IntVal(1), IntVal(0)),
                If(c_is_term, IntVal(1), IntVal(0)),
                If(c_is_pair, IntVal(1), IntVal(0)),
            ]
        )
        == 1
    )
    phases = [
        And(c_is_nt, Not(c_nt_has_imp)),
        And(c_is_pair, c_kw_head),
        c_is_term,
        And(c_is_pair, Not(c_kw_head)),
        And(c_is_nt, c_nt_has_imp),
    ]
    exact_phase_partition = (
        Sum([If(p, IntVal(1), IntVal(0)) for p in phases]) == 1
    )

    sexp_candidates = [
        *[sexp_sort.Atom(IntVal(a)) for a in range(ATOM_COUNT)],
        *[
            sexp_sort.List2(IntVal(a), IntVal(b))
            for a in range(ATOM_COUNT)
            for b in range(ATOM_COUNT)
        ],
    ]

    pretty_theorem, raw_let, raw_app, pretty_let, pretty_app = (
        _verify_domain_4_pretty(sexp_sort, sym_x, kw_neg, num_42)
    )
    sexp_neg_42 = sexp_sort.List2(kw_neg, num_42)
    pair_out = out_sort.Ok(ast_sort.RootPair(kw_neg, num_42))
    sub_neg_out = out_sort.Ok(ast_sort.WrapSubNeg)

    theorems = [
        _prove_non_vacuous(
            "D4-T1",
            "OrderRules 5-phase partition is exact and deterministic",
            [valid_clause],
            And(
                exact_phase_partition,
                *[
                    Or(
                        out_sort.is_Ok(parse_root(sx)),
                        out_sort.is_Err(parse_root(sx)),
                    )
                    for sx in sexp_candidates
                ],
            ),
            witness=[c_is_nt, c_nt_has_imp, root_sym],
        ),
        _prove_non_vacuous(
            "D4-T2",
            "parse_root(unparse_raw(node)) == Ok(node) iff NoShadowing",
            [],
            roundtrip == no_shadow,
            witness=[root_sym, Not(kw_as_sym), root_neg],
        ),
        pretty_theorem,
    ]

    base_sub = [
        root_sym,
        kw_as_sym,
        Not(root_neg),
        root_pair,
        inc_sub,
        sub_num,
        Not(sub_sym),
        sub_neg,
    ]
    vectors = [
        _decode_vector(spec)
        for spec in (
            VectorSpec(
                "VEC-D4-01",
                "D4",
                "Sub with implicit list form demotes to phase 4 behind (e1 e2)",
                "D4-T2",
                [*base_sub, sub_imp],
                lambda m: {
                    "decoded_witness": {
                        "parsed_as_implicit_pair": _eval_bool(
                            m, parse_root(sexp_neg_42) == pair_out
                        )
                    },
                    "upstream_expected": {
                        "dispatched_phase": 3
                        if _eval_bool(m, parse_root(sexp_neg_42) == pair_out)
                        else 4
                    },
                    "csharp_expected": {
                        "diagnostics": [],
                        "dispatched_phase": 3
                        if _eval_bool(m, parse_root(sexp_neg_42) == pair_out)
                        else 4,
                    },
                },
            ),
            VectorSpec(
                "VEC-D4-02",
                "D4",
                "Sub without implicit list form runs at phase 0 before (e1 e2)",
                "D4-T2",
                [*base_sub, Not(sub_imp)],
                lambda m: {
                    "decoded_witness": {
                        "parsed_as_phase0_sub": _eval_bool(
                            m, parse_root(sexp_neg_42) == sub_neg_out
                        )
                    },
                    "upstream_expected": {
                        "dispatched_phase": 0
                        if _eval_bool(m, parse_root(sexp_neg_42) == sub_neg_out)
                        else 3
                    },
                    "csharp_expected": {
                        "diagnostics": [],
                        "dispatched_phase": 0
                        if _eval_bool(m, parse_root(sexp_neg_42) == sub_neg_out)
                        else 3,
                    },
                },
            ),
            VectorSpec(
                "VEC-D4-03",
                "D4",
                "Tag-erasing [Pretty] collides while Unparse(raw=true) holds",
                "D4-T3",
                [raw_let != raw_app, pretty_let == pretty_app],
                lambda m: {
                    "decoded_witness": {
                        "raw_distinct": _eval_bool(m, raw_let != raw_app),
                        "pretty_collides": _eval_bool(
                            m, pretty_let == pretty_app
                        ),
                    },
                    "upstream_expected": {
                        "raw_roundtrip": _eval_bool(m, raw_let != raw_app),
                        "pretty_roundtrip": not _eval_bool(
                            m, pretty_let == pretty_app
                        ),
                    },
                    "csharp_expected": {
                        "diagnostics": [],
                        "raw_roundtrip": _eval_bool(m, raw_let != raw_app),
                        "pretty_roundtrip": not _eval_bool(
                            m, pretty_let == pretty_app
                        ),
                    },
                },
            ),
            VectorSpec(
                "VEC-D4-04",
                "D4",
                "Excluding grammar keywords from symbol satisfies NoShadowing",
                "D4-T2",
                [
                    root_sym,
                    Not(kw_as_sym),
                    root_neg,
                    root_pair,
                    inc_sub,
                    sub_imp,
                    sub_num,
                    Not(sub_sym),
                    Not(sub_neg),
                ],
                lambda m: {
                    "decoded_witness": {
                        "no_shadowing": _eval_bool(m, no_shadow),
                        "roundtrip": _eval_bool(m, roundtrip),
                    },
                    "upstream_expected": {
                        "roundtrip": _eval_bool(m, roundtrip)
                    },
                    "csharp_expected": {
                        "diagnostics": [],
                        "roundtrip": _eval_bool(m, roundtrip),
                    },
                },
            ),
        )
    ]
    return theorems, vectors


def run_all() -> tuple[list[TheoremResult], list[VectorWitness]]:
    """Execute all four domain verifiers and collect theorems and vectors."""
    all_theorems: list[TheoremResult] = []
    all_vectors: list[VectorWitness] = []
    for verifier in (
        _verify_domain_1,
        _verify_domain_2,
        _verify_domain_3,
        _verify_domain_4,
    ):
        domain_theorems, domain_vectors = verifier()
        all_theorems.extend(domain_theorems)
        all_vectors.extend(domain_vectors)
    return all_theorems, all_vectors


def main() -> int:
    """CLI entry point for Nanopass Z3 formal specification verification."""
    parser = argparse.ArgumentParser(
        description="Verify the bounded Z3 formal specification for Nanopass."
    )
    parser.add_argument(
        "--emit-vectors",
        action="store_true",
        help="Emit JSON differential test vectors decoded from Z3 witnesses.",
    )
    args = parser.parse_args()

    theorems, vectors = run_all()
    failed_theorems = [t for t in theorems if not t.passed]
    failed_vectors = [v for v in vectors if not v.passed]
    ok = not failed_theorems and not failed_vectors

    if args.emit_vectors:
        payload = {
            "sources": SOURCES,
            "bounds": BOUNDS,
            "theorem_count": len(theorems),
            "vector_count": len(vectors),
            "all_passed": ok,
            "vectors": [v.payload for v in vectors],
        }
        json.dump(payload, sys.stdout, indent=2)
        sys.stdout.write("\n")
        return 0 if ok else 1

    for item in theorems:
        tag = "PASS" if item.passed else "FAIL"
        print(f"[{tag}] {item.theorem_id}: {item.title} [{item.detail}]")
    for vec in vectors:
        tag = "PASS" if vec.passed else "FAIL"
        print(f"[{tag}] {vec.vector_id}: {vec.scenario}")

    passed_t = len(theorems) - len(failed_theorems)
    passed_v = len(vectors) - len(failed_vectors)
    print(
        f"\nSummary: {passed_t}/{len(theorems)} theorems, "
        f"{passed_v}/{len(vectors)} solver-decoded differential vectors."
    )
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
