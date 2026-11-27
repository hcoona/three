# /// script
# requires-python = ">=3.14"
# dependencies = [
#   "z3-solver==5.4.1.0",
# ]
# ///
"""Machine-width 32-bit BitVec Z3 formal specification for CircularList.

Proves machine-integer two's-complement (`BitVecSort(32)`) arithmetic safety and
ring-indexing equivalence across the full positive `.NET` `Int32` capacity
domain `1 <= Capacity <= 2_147_483_647` (`0x7FFFFFFF`), complementing `CsCheck`
stateful property testing, `TLA+/TLC` protocol/reentrancy model checking, and
`PAL-CS` / Pulse-F* deductive heap verification:

  - BV-T1 (GetPhysicalIndex Non-Overflow & Modular Exactness):
    Extracts a concrete signed-overflow counterexample against legacy
    `(startIndex + logicalIndex) % capacity`, then proves over `BitVec(32)`
    that `tailLength = capacity - startIndex` and the branch
    `logicalIndex < tailLength ? startIndex + logicalIndex`
    `: logicalIndex - tailLength` never overflow or underflow signed `Int32`,
    stay within `[0, capacity)`, and equal exact mathematical
    `(startIndex + logicalIndex) mod capacity`.

  - BV-T2 (Bijectivity & Contiguous/Wrapped Partition Order):
    Proves over `BitVec(32)` that `GetPhysicalIndex` is injective on
    `[0, count)` (and bijective onto `[0, capacity)` when `count == capacity`),
    monotonically maps `[0, count)` to `[startIndex, startIndex + count)` when
    `count <= capacity - startIndex`, and partitions `[0, count)` into
    `[startIndex, capacity)` followed by `[0, count - (capacity - startIndex))`
    when wrapped.

  - BV-T3 (AddCore Non-Full Append & Full Rollover Exactness):
    Proves over `BitVec(32)` that both non-full append (`count < capacity`) and
    full rollover (`count == capacity`) execute without signed `Int32`
    overflow/underflow, preserve the representation invariant, preserve existing
    logical elements (shifting by one on rollover), and place the new element at
    the newest logical slot while evicting physical `startIndex` (`logical 0`).

  - BV-T4 (Dual-Segment & CopyTo Bounds Non-Overflow):
    Extracts a concrete signed-overflow counterexample against legacy
    `startIndex + count <= capacity`, then proves over `BitVec(32)` that
    `count <= capacity - startIndex`, `GetSegments()` / `Clear()` slice bounds,
    and `CopyTo(T[] array, int arrayIndex)` destination offset
    `arrayIndex + tailLength` never overflow signed `Int32` and stay strictly
    within source `[0, capacity]` and destination `[0, array.Length]`.
"""

from __future__ import annotations

import argparse
import json
from dataclasses import asdict, dataclass
from typing import TYPE_CHECKING

from z3 import (
    ULT,
    And,
    BitVec,
    BitVecRef,
    BitVecVal,
    BoolRef,
    BVAddNoOverflow,
    BVAddNoUnderflow,
    BVSubNoOverflow,
    BVSubNoUnderflow,
    If,
    Implies,
    Not,
    SolverFor,
    ZeroExt,
    sat,
    unsat,
)

if TYPE_CHECKING:
    from collections.abc import Callable

BV32 = 32
ZERO = BitVecVal(0, BV32)
ONE = BitVecVal(1, BV32)
INT32_MAX = BitVecVal(0x7FFFFFFF, BV32)


@dataclass(frozen=True)
class TheoremResult:
    """Verification outcome for a single Z3 theorem."""

    theorem_id: str
    title: str
    witness_sat: bool
    universal_unsat: bool
    counterexample_to_legacy: dict[str, int] | None = None


@dataclass(frozen=True)
class BoundaryVector:
    """Concrete 32-bit boundary vector extracted or validated by Z3."""

    vector_id: str
    description: str
    capacity: int
    start_index: int
    count: int
    logical_index: int
    expected_physical_index: int


def _valid_ring_state(
    cap: BitVecRef,
    start: BitVecRef,
    count: BitVecRef,
) -> BoolRef:
    """Representation invariant over signed 32-bit `Int32` values."""
    return And(
        cap >= ONE,
        cap <= INT32_MAX,
        start >= ZERO,
        start < cap,
        count >= ZERO,
        count <= cap,
    )


def _remediated_phys_index(
    cap: BitVecRef,
    start: BitVecRef,
    idx: BitVecRef,
) -> tuple[BitVecRef, BoolRef]:
    """Return `(physical_index, no_signed_overflow)` for `GetPhysicalIndex`."""
    tail_len = cap - start
    tail_ok = And(
        BVSubNoOverflow(cap, start),
        BVSubNoUnderflow(cap, start, signed=True),
    )
    in_first = idx < tail_len
    first_expr = start + idx
    first_ok = And(
        BVAddNoOverflow(start, idx, signed=True),
        BVAddNoUnderflow(start, idx),
    )
    second_expr = idx - tail_len
    second_ok = And(
        BVSubNoOverflow(idx, tail_len),
        BVSubNoUnderflow(idx, tail_len, signed=True),
    )
    phys = If(in_first, first_expr, second_expr)
    safe = And(tail_ok, If(in_first, first_ok, second_ok))
    return phys, safe


def _as_signed_int32(val: int) -> int:
    return val - (1 << 32) if val >= (1 << 31) else val


def _prove_non_vacuous(
    theorem_id: str,
    title: str,
    premises: BoolRef,
    obligations: list[BoolRef],
    legacy_counterexample_builder: Callable[[], dict[str, int]] | None = None,
) -> TheoremResult:
    """Check `premises` is satisfiable and every `premises => ob` is valid."""
    witness_solver = SolverFor("QF_BV")
    witness_solver.add(premises)
    witness_ok = witness_solver.check() == sat
    if not witness_ok:
        msg = f"{theorem_id} ({title}) failed: vacuous premises"
        raise RuntimeError(msg)

    for index, obligation in enumerate(obligations):
        proof_solver = SolverFor("QF_BV")
        proof_solver.add(premises)
        proof_solver.add(Not(obligation))
        if proof_solver.check() != unsat:
            msg = (
                f"{theorem_id} ({title}) obligation #{index + 1} failed: "
                f"counterexample {proof_solver.model()}"
            )
            raise RuntimeError(msg)

    legacy_cex = (
        legacy_counterexample_builder()
        if legacy_counterexample_builder is not None
        else None
    )
    return TheoremResult(
        theorem_id=theorem_id,
        title=title,
        witness_sat=True,
        universal_unsat=True,
        counterexample_to_legacy=legacy_cex,
    )


def check_bv_t1() -> TheoremResult:
    """BV-T1: GetPhysicalIndex non-overflow and exact modular equivalence."""
    cap = BitVec("cap", BV32)
    start = BitVec("start", BV32)
    count = BitVec("count", BV32)
    idx = BitVec("idx", BV32)

    premises = And(
        _valid_ring_state(cap, start, count),
        idx >= ZERO,
        idx < count,
    )
    phys, safe = _remediated_phys_index(cap, start, idx)

    # Widen non-negative 32-bit values by 1 bit (`BitVec(33)`) where
    # `0 <= start + idx < 2 * cap < 2^32` cannot overflow, and verify the
    # unique Euclidean division decomposition `start + idx = q * cap + phys`
    # with `q in {0, 1}` and `0 <= phys < cap` (avoiding 64-bit hardware
    # divider bit-blasting while proving exact `(start + idx) mod cap`).
    cap_33 = ZeroExt(1, cap)
    sum_33 = ZeroExt(1, start) + ZeroExt(1, idx)
    phys_33 = ZeroExt(1, phys)
    quotient_term_33 = If(ULT(sum_33, cap_33), BitVecVal(0, BV32 + 1), cap_33)

    obligations = [
        safe,
        And(phys >= ZERO, phys < cap),
        sum_33 == quotient_term_33 + phys_33,
    ]

    def legacy_cex() -> dict[str, int]:
        s = SolverFor("QF_BV")
        s.add(premises)
        s.add(Not(BVAddNoOverflow(start, idx, signed=True)))
        if s.check() != sat:
            msg = "Expected signed overflow witness for legacy start + idx"
            raise RuntimeError(msg)
        m = s.model()
        c_v = _as_signed_int32(m.eval(cap).as_long())
        st_v = _as_signed_int32(m.eval(start).as_long())
        ct_v = _as_signed_int32(m.eval(count).as_long())
        i_v = _as_signed_int32(m.eval(idx).as_long())
        wrapped_sum = _as_signed_int32((st_v + i_v) & 0xFFFFFFFF)
        return {
            "capacity": c_v,
            "start_index": st_v,
            "count": ct_v,
            "logical_index": i_v,
            "legacy_signed_sum": wrapped_sum,
        }

    return _prove_non_vacuous(
        "BV-T1",
        "GetPhysicalIndex signed-32-bit non-overflow and modular exactness",
        premises,
        obligations,
        legacy_cex,
    )


def check_bv_t2() -> TheoremResult:
    """BV-T2: Bijectivity and contiguous/wrapped partition order."""
    cap = BitVec("cap", BV32)
    start = BitVec("start", BV32)
    count = BitVec("count", BV32)
    i = BitVec("i", BV32)
    j = BitVec("j", BV32)
    p = BitVec("p", BV32)

    premises = And(
        _valid_ring_state(cap, start, count),
        count >= ONE,
        i >= ZERO,
        i < count,
        j >= ZERO,
        j < count,
        p >= ZERO,
        p < cap,
    )
    phys_i, safe_i = _remediated_phys_index(cap, start, i)
    phys_j, safe_j = _remediated_phys_index(cap, start, j)

    tail_len = cap - start
    injective = Implies(i != j, phys_i != phys_j)
    unwrapped_order = Implies(
        And(count <= tail_len, i < j),
        And(phys_i >= start, phys_i < phys_j),
    )
    wrapped_partition = Implies(
        count > tail_len,
        And(
            Implies(i < tail_len, And(phys_i >= start, phys_i < cap)),
            Implies(
                i >= tail_len, And(phys_i >= ZERO, phys_i < count - tail_len)
            ),
        ),
    )
    # Inverse witness when full (`count == cap`): every physical `p` has a
    # unique logical preimage `inv_p` in `[0, cap)`.
    inv_p = If(p >= start, p - start, p + (cap - start))
    phys_inv_p, safe_inv = _remediated_phys_index(cap, start, inv_p)
    surjective_when_full = Implies(
        count == cap,
        And(
            safe_inv,
            inv_p >= ZERO,
            inv_p < cap,
            phys_inv_p == p,
        ),
    )

    obligations = [
        And(safe_i, safe_j),
        injective,
        unwrapped_order,
        wrapped_partition,
        surjective_when_full,
    ]
    return _prove_non_vacuous(
        "BV-T2",
        "GetPhysicalIndex injectivity, full surjectivity, and segment order",
        premises,
        obligations,
    )


def check_bv_t3() -> TheoremResult:
    """BV-T3: AddCore non-full append and full rollover transition safety."""
    cap = BitVec("cap", BV32)
    start = BitVec("start", BV32)
    count = BitVec("count", BV32)
    k = BitVec("k", BV32)

    premises = _valid_ring_state(cap, start, count)
    tail_len = cap - start

    # Branch A: count < cap (non-full append)
    write_idx = If(count < tail_len, start + count, count - tail_len)
    write_safe = And(
        BVSubNoOverflow(cap, start),
        BVSubNoUnderflow(cap, start, signed=True),
        If(
            count < tail_len,
            And(
                BVAddNoOverflow(start, count, signed=True),
                BVAddNoUnderflow(start, count),
            ),
            And(
                BVSubNoOverflow(count, tail_len),
                BVSubNoUnderflow(count, tail_len, signed=True),
            ),
        ),
        BVAddNoOverflow(count, ONE, signed=True),
    )
    count_a = count + ONE
    start_a = start
    phys_old_k, _ = _remediated_phys_index(cap, start, k)
    phys_new_a_k, _ = _remediated_phys_index(cap, start_a, k)
    phys_new_a_last, safe_a_last = _remediated_phys_index(cap, start_a, count)

    non_full_ok = Implies(
        count < cap,
        And(
            write_safe,
            _valid_ring_state(cap, start_a, count_a),
            write_idx >= ZERO,
            write_idx < cap,
            safe_a_last,
            phys_new_a_last == write_idx,
            Implies(
                And(k >= ZERO, k < count),
                And(phys_new_a_k == phys_old_k, phys_old_k != write_idx),
            ),
        ),
    )

    # Branch B: count == cap (full rollover)
    last_slot_safe = And(
        BVSubNoOverflow(cap, ONE),
        BVSubNoUnderflow(cap, ONE, signed=True),
    )
    inc_start_safe = Implies(
        start != cap - ONE,
        BVAddNoOverflow(start, ONE, signed=True),
    )
    start_b = If(start == cap - ONE, ZERO, start + ONE)
    count_b = cap
    phys_old_zero, _ = _remediated_phys_index(cap, start, ZERO)
    phys_old_kp1, _ = _remediated_phys_index(cap, start, k + ONE)
    phys_new_b_k, _ = _remediated_phys_index(cap, start_b, k)
    phys_new_b_last, safe_b_last = _remediated_phys_index(
        cap,
        start_b,
        cap - ONE,
    )

    full_ok = Implies(
        count == cap,
        And(
            last_slot_safe,
            inc_start_safe,
            _valid_ring_state(cap, start_b, count_b),
            phys_old_zero == start,
            safe_b_last,
            phys_new_b_last == start,
            Implies(
                And(k >= ZERO, k < cap - ONE),
                And(
                    BVAddNoOverflow(k, ONE, signed=True),
                    phys_new_b_k == phys_old_kp1,
                    phys_new_b_k != start,
                ),
            ),
        ),
    )

    return _prove_non_vacuous(
        "BV-T3",
        "AddCore non-full append and full rollover exactness without overflow",
        premises,
        [non_full_ok, full_ok],
    )


def check_bv_t4() -> TheoremResult:
    """BV-T4: GetSegments, Clear, AsSpan, and CopyTo bounds non-overflow."""
    cap = BitVec("cap", BV32)
    start = BitVec("start", BV32)
    count = BitVec("count", BV32)
    arr_len = BitVec("arr_len", BV32)
    arr_idx = BitVec("arr_idx", BV32)

    premises = And(
        _valid_ring_state(cap, start, count),
        arr_len >= ZERO,
        arr_len <= INT32_MAX,
        arr_idx >= ZERO,
        arr_idx <= arr_len,
        arr_len - arr_idx >= count,
    )

    tail_len = cap - start
    tail_safe = And(
        BVSubNoOverflow(cap, start),
        BVSubNoUnderflow(cap, start, signed=True),
        BVSubNoOverflow(arr_len, arr_idx),
        BVSubNoUnderflow(arr_len, arr_idx, signed=True),
    )
    unwrapped = count <= tail_len

    # Segment bounds in source backing array `[0, cap]`
    first_off = If(count == ZERO, ZERO, start)
    first_len = If(count == ZERO, ZERO, If(unwrapped, count, tail_len))
    second_off = ZERO
    second_len = If(unwrapped, ZERO, count - tail_len)

    seg_safe = And(
        tail_safe,
        Implies(
            Not(unwrapped),
            And(
                BVSubNoOverflow(count, tail_len),
                BVSubNoUnderflow(count, tail_len, signed=True),
            ),
        ),
        BVAddNoOverflow(first_off, first_len, signed=True),
        BVAddNoOverflow(first_len, second_len, signed=True),
        first_off >= ZERO,
        first_len >= ZERO,
        first_off + first_len <= cap,
        second_off == ZERO,
        second_len >= ZERO,
        second_len <= start,
        first_len + second_len == count,
    )

    # CopyTo destination bounds `[0, arr_len]` including `arr_idx + tail_len`
    copy_safe = If(
        unwrapped,
        And(
            BVAddNoOverflow(arr_idx, count, signed=True),
            arr_idx + count <= arr_len,
        ),
        And(
            BVAddNoOverflow(arr_idx, tail_len, signed=True),
            BVAddNoOverflow(arr_idx + tail_len, second_len, signed=True),
            arr_idx + tail_len >= ZERO,
            arr_idx + tail_len <= arr_len,
            (arr_idx + tail_len) + second_len <= arr_len,
            (arr_idx + tail_len) + second_len == arr_idx + count,
        ),
    )

    # Unsigned `(uint)index >= (uint)count` equivalence for range checking
    probe = BitVec("probe", BV32)
    uint_range_equiv = ULT(probe, count) == And(probe >= ZERO, probe < count)

    def legacy_cex() -> dict[str, int]:
        s = SolverFor("QF_BV")
        s.add(_valid_ring_state(cap, start, count))
        # Legacy `startIndex + count <= Capacity` wraps negative on Int32!
        s.add(Not(BVAddNoOverflow(start, count, signed=True)))
        s.add(start + count <= cap)
        if s.check() != sat:
            msg = "Expected overflow witness for legacy start + count <= cap"
            raise RuntimeError(msg)
        m = s.model()
        c_v = _as_signed_int32(m.eval(cap).as_long())
        st_v = _as_signed_int32(m.eval(start).as_long())
        ct_v = _as_signed_int32(m.eval(count).as_long())
        wrapped = _as_signed_int32((st_v + ct_v) & 0xFFFFFFFF)
        return {
            "capacity": c_v,
            "start_index": st_v,
            "count": ct_v,
            "legacy_signed_start_plus_count": wrapped,
        }

    return _prove_non_vacuous(
        "BV-T4",
        "Dual-segment partition and CopyTo destination arithmetic non-overflow",
        premises,
        [seg_safe, copy_safe, uint_range_equiv],
        legacy_cex,
    )


def build_boundary_vectors() -> list[BoundaryVector]:
    """Validate canonical 32-bit boundary test vectors against the Z3 spec."""
    candidates = [
        (
            "VEC-BV-01",
            "Degenerate capacity=1 single-slot ring",
            1,
            0,
            1,
            0,
            0,
        ),
        (
            "VEC-BV-02",
            "Signed Int32 overflow boundary (start + index == 0x80000000)",
            0x7FFFFFFF,
            0x40000000,
            0x7FFFFFFF,
            0x40000000,
            1,
        ),
        (
            "VEC-BV-03",
            "Max Int32 capacity last start slot wrap-around to physical 0",
            0x7FFFFFFF,
            0x7FFFFFFE,
            0x7FFFFFFF,
            1,
            0,
        ),
        (
            "VEC-BV-04",
            "Max Int32 capacity newest slot at physical startIndex - 1",
            0x7FFFFFFF,
            0x7FFFFFFE,
            0x7FFFFFFF,
            0x7FFFFFFE,
            0x7FFFFFFD,
        ),
        (
            "VEC-BV-05",
            "Exact tail boundary logicalIndex == tailLength - 1 -> cap - 1",
            0x7FFFFFFF,
            0x50000000,
            0x60000000,
            0x2FFFFFFE,
            0x7FFFFFFE,
        ),
        (
            "VEC-BV-06",
            "Exact wrap boundary logicalIndex == tailLength -> physical 0",
            0x7FFFFFFF,
            0x50000000,
            0x60000000,
            0x2FFFFFFF,
            0,
        ),
    ]
    verified: list[BoundaryVector] = []
    for vec_id, desc, cap_i, st_i, ct_i, idx_i, expected_i in candidates:
        cap_bv = BitVecVal(cap_i, BV32)
        st_bv = BitVecVal(st_i, BV32)
        ct_bv = BitVecVal(ct_i, BV32)
        idx_bv = BitVecVal(idx_i, BV32)
        phys_bv, safe_bv = _remediated_phys_index(cap_bv, st_bv, idx_bv)

        s = SolverFor("QF_BV")
        s.add(
            Not(
                And(
                    _valid_ring_state(cap_bv, st_bv, ct_bv),
                    idx_bv >= ZERO,
                    idx_bv < ct_bv,
                    safe_bv,
                    phys_bv == BitVecVal(expected_i, BV32),
                ),
            ),
        )
        if s.check() != unsat:
            msg = f"Boundary vector {vec_id} failed Z3 validation"
            raise RuntimeError(msg)
        verified.append(
            BoundaryVector(
                vector_id=vec_id,
                description=desc,
                capacity=cap_i,
                start_index=st_i,
                count=ct_i,
                logical_index=idx_i,
                expected_physical_index=expected_i,
            ),
        )
    return verified


def main(argv: list[str] | None = None) -> int:
    """Run all Z3 BitVec(32) proofs and emit human or JSON output."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--json",
        action="store_true",
        help="Emit machine-readable JSON verification report.",
    )
    args = parser.parse_args(argv)

    theorems = [
        check_bv_t1(),
        check_bv_t2(),
        check_bv_t3(),
        check_bv_t4(),
    ]
    vectors = build_boundary_vectors()

    if args.json:
        payload = {
            "bit_width": BV32,
            "theorems": [asdict(t) for t in theorems],
            "boundary_vectors": [asdict(v) for v in vectors],
        }
        print(json.dumps(payload, indent=2))
        return 0

    print("CircularList Z3 BitVec(32) formal specification checks:")
    for t in theorems:
        status = "PASS" if t.witness_sat and t.universal_unsat else "FAIL"
        print(f"  [{status}] {t.theorem_id}: {t.title}")
        if t.counterexample_to_legacy is not None:
            cex = t.counterexample_to_legacy
            print(f"         legacy overflow witness: {cex}")
    print(f"  [PASS] Validated {len(vectors)} canonical 32-bit vectors.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
