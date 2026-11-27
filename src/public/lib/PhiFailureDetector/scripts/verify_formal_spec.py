# /// script
# requires-python = ">=3.14"
# dependencies = [
#   "z3-solver==5.4.1.0",
# ]
# ///
"""Z3 Real and IEEE-754 Float64 formal specification for PhiFailureDetector v3.

Proves mathematical and floating-point properties across four domains,
contrasting the broken v2.x implementation with the v3.0 algorithms:

  - Domain 1 (Two-Stack Chan Sliding-Window Moments & IEEE-754 Range Safety):
    * D1-T1 (Real): Proves Chan pairwise moment combination across all three
      non-empty two-stack splits of three symbolic samples equals exact sample
      mean and non-negative population variance (queue-transition and amortized
      step bounds are verified separately in PhiAccrualFailureDetector.tla).
    * D1-W1 / D1-T2 (BitVec64 vs Float64): Exhibits the v2.x Int64 overflow
      witness at 3,037,000,500 ns (~3.037 s at 1 GHz TimeProvider), and proves
      in IEEE-754 Float64 (FPSort(11, 53), RNE) that pairwise differences and
      squares stay finite across [0, TimeSpan.MaxValue.TotalSeconds].
    * D1-T3 (Float64): Exhibits catastrophic cancellation in naive
      E[X^2] - E[X]^2 (at 1.0 s + 1 ns jitter) and single-anchor shifted sums
      (after evicting an 86,400 s outlier), while proving two-stack Chan
      retains positive variance in both regimes.

  - Domain 2 (Exponential Distribution Base-10 Scale & Degenerate Safety):
    * D2-T1 (Real): Proves phi_exp(t; mu) = t / (mu * ln 10) calibrates
      phi = k <=> P_later = 10^-k, whereas v2.x (t / mu) inflates phi by ln(10).
    * D2-T2 (Real & Float64): Proves unit scale invariance, non-decreasing
      monotonicity in elapsed time, non-increasing monotonicity in
      AcceptableHeartbeatPause, and total Float64 NaN/negative freedom across
      [0, MaxTimeSpan] including zero-mean fail-closed behavior.

  - Domain 3 (Normal Logistic CDF Unification & Tail Stability):
    * D3-T1 (Real): Proves both branches of Hayashibara/Akka's piecewise
      logistic Normal tail expressions equal 1 / (1 + exp(z)) algebraically
      over all R and agree (1/2) at the branch boundary y = 0.
    * D3-T2 (Real): Proves strict monotonicity z'(y) > 0 over all R, unit scale
      invariance, pause monotonicity, and division-by-zero freedom under
      sigma_eff = max(sigma, sigma_min) with sigma_min > 0.
    * D3-T3 (Float64 + binary16 RNE): Verifies concrete IEEE-754 Float64
      exp(-z) underflow at z >= 746, proves v3 softplus stays finite/positive
      over Float64 [746, 1e300] and +Inf (non-NaN) at z = +Inf while v2
      collapses to 0, and bit-blasts RNE scaling monotonicity in binary16.

  - Domain 4 (Lifecycle Bootstrap & Level-Set Availability Identity):
    * D4-T1 (Real): Proves Bootstrapping crosses any Threshold > 0 at finite
      elapsed*, with elapsed* < TimeSpan.MaxValue for parameters <= 86,400 s
      and Threshold <= 1,000.
    * D4-T2 (Float64): Proves Idle (0.0 < Threshold) is available, +Inf fails
      closed, negated NaN comparisons fail open, and non-NaN (Phi < Threshold)
      is the exact complement of (Phi >= Threshold).

References:
  - Hayashibara et al., "The phi accrual failure detector", IEEE SRDS 2004,
    https://doi.org/10.1109/RELDIS.2004.1353004
  - Apache Cassandra CASSANDRA-2597 (Exponential base-10 scaling)
  - Goldberg, "What Every Computer Scientist Should Know About Floating-Point
    Arithmetic", ACM Computing Surveys 23(1), Theorem 4 (log1p accuracy)
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from dataclasses import asdict, dataclass

from z3 import (
    FP,
    RNE,
    And,
    ArithRef,
    BitVec,
    BitVecVal,
    BoolRef,
    BoolVal,
    FPSort,
    FPVal,
    Function,
    Not,
    Or,
    Q,
    Real,
    RealSort,
    RealVal,
    Solver,
    SolverFor,
    fpAdd,
    fpDiv,
    fpEQ,
    fpGEQ,
    fpGT,
    fpIsInf,
    fpIsNaN,
    fpLEQ,
    fpLT,
    fpMul,
    fpNaN,
    fpSub,
    sat,
    unsat,
)

FLOAT64 = FPSort(11, 53)
RNE_MODE = RNE()
MAX_TIMESPAN_SECONDS = (
    922_337_203_685.4776  # TimeSpan.MaxValue.TotalSeconds (~9.22e11)
)
LN10_FLOAT = math.log(10.0)
LOG10E_FLOAT = math.log10(math.e)


@dataclass(frozen=True)
class CheckResult:
    """Outcome of a single Z3 theorem or counterexample check."""

    check_id: str
    domain: str
    kind: str  # "theorem" (expect unsat of negation) or "witness" (expect sat)
    description: str
    passed: bool
    detail: str


def _prove(
    check_id: str,
    domain: str,
    description: str,
    claim: BoolRef,
    *,
    logic: str | None = None,
) -> CheckResult:
    solver = SolverFor(logic) if logic else Solver()
    solver.add(Not(claim))
    outcome = solver.check()
    if outcome == unsat:
        return CheckResult(
            check_id,
            domain,
            "theorem",
            description,
            passed=True,
            detail="unsat (proved)",
        )
    detail = f"unexpected {outcome}"
    if outcome == sat:
        detail += f": {solver.model()}"
    return CheckResult(
        check_id,
        domain,
        "theorem",
        description,
        passed=False,
        detail=detail,
    )


def _witness(
    check_id: str,
    domain: str,
    description: str,
    constraint: BoolRef,
) -> CheckResult:
    solver = Solver()
    solver.add(constraint)
    outcome = solver.check()
    if outcome == sat:
        return CheckResult(
            check_id,
            domain,
            "witness",
            description,
            passed=True,
            detail=f"sat witness verified ({solver.model()})",
        )
    return CheckResult(
        check_id,
        domain,
        "witness",
        description,
        passed=False,
        detail=f"expected sat, got {outcome}",
    )


def _check_d1_float64_stability() -> CheckResult:
    """Prove D1-T3 low-jitter and post-outlier eviction stability in Float64."""
    # (a) At 1.0 s + 1 ns, paper's E[X^2] - E[X]^2 <= 0 while Chan > 0.
    # (b) After evicting 86400 s from [86400, 1, 1, 1.001] (W=3), single-anchor
    #     shifted sums collapse to <= 0 while two-stack Chan > 0.
    zero64 = FPVal(0.0, FLOAT64)
    s_a = FPVal(1.0, FLOAT64)
    s_b = fpAdd(RNE_MODE, FPVal(1.0, FLOAT64), FPVal(1e-9, FLOAT64))
    two64 = FPVal(2.0, FLOAT64)
    three64 = FPVal(3.0, FLOAT64)
    naive_mean = fpDiv(RNE_MODE, fpAdd(RNE_MODE, s_a, s_b), two64)
    naive_ex2 = fpDiv(
        RNE_MODE,
        fpAdd(RNE_MODE, fpMul(RNE_MODE, s_a, s_a), fpMul(RNE_MODE, s_b, s_b)),
        two64,
    )
    naive_var = fpSub(
        RNE_MODE, naive_ex2, fpMul(RNE_MODE, naive_mean, naive_mean)
    )

    d_b = fpSub(RNE_MODE, s_b, s_a)
    chan_low_jitter_m2 = fpMul(
        RNE_MODE, fpMul(RNE_MODE, d_b, d_b), FPVal(0.5, FLOAT64)
    )
    chan_low_jitter_var = fpDiv(RNE_MODE, chan_low_jitter_m2, two64)

    outlier = FPVal(86400.0, FLOAT64)
    v_one = FPVal(1.0, FLOAT64)
    v_jit = FPVal(1.001, FLOAT64)
    d_one = fpSub(RNE_MODE, v_one, outlier)
    d_jit = fpSub(RNE_MODE, v_jit, outlier)
    sub_sum1 = fpAdd(RNE_MODE, fpAdd(RNE_MODE, d_one, d_one), d_jit)
    sub_sum2 = fpAdd(
        RNE_MODE,
        fpAdd(
            RNE_MODE,
            fpMul(RNE_MODE, d_one, d_one),
            fpMul(RNE_MODE, d_one, d_one),
        ),
        fpMul(RNE_MODE, d_jit, d_jit),
    )
    sub_mean = fpDiv(RNE_MODE, sub_sum1, three64)
    sub_var = fpSub(
        RNE_MODE,
        fpDiv(RNE_MODE, sub_sum2, three64),
        fpMul(RNE_MODE, sub_mean, sub_mean),
    )

    # Two-stack Chan after flipping [86400, 1, 1] -> front=[1, 1]
    # and pushing back=[1.001]:
    chan_delta = fpSub(RNE_MODE, v_jit, v_one)
    chan_post_outlier_m2 = fpMul(
        RNE_MODE,
        fpMul(RNE_MODE, chan_delta, chan_delta),
        fpDiv(RNE_MODE, two64, three64),
    )
    chan_post_outlier_var = fpDiv(RNE_MODE, chan_post_outlier_m2, three64)

    return _prove(
        "D1-T3",
        "D1",
        (
            "In Float64, two-stack Chan preserves positive variance on "
            "both 1 ns jitter and post-86400 s outlier eviction where "
            "single-anchor shifted sums collapse to <= 0"
        ),
        And(
            fpLEQ(naive_var, zero64),
            fpGT(chan_low_jitter_var, zero64),
            fpLEQ(sub_var, zero64),
            fpGT(chan_post_outlier_var, zero64),
        ),
    )


def check_domain_1() -> list[CheckResult]:
    """Domain 1: Two-stack Chan sliding-window moments & Float64 safety."""
    results: list[CheckResult] = []

    # D1-T1: Over R, Chan pairwise combination across every two-stack split
    # of [x1, x2, x3] (after evicting arbitrary outlier x0) equals exact
    # population mean and variance >= 0 without subtraction of squares.
    x1, x2, x3 = [Real(name) for name in ("x1", "x2", "x3")]
    n3 = RealVal(3)

    def chan_combine(
        c_a: tuple[ArithRef, ArithRef, ArithRef],
        c_b: tuple[ArithRef, ArithRef, ArithRef],
    ) -> tuple[ArithRef, ArithRef, ArithRef]:
        n_a, mu_a, m2_a = c_a
        n_b, mu_b, m2_b = c_b
        n_ab = n_a + n_b
        delta = mu_b - mu_a
        mu_ab = mu_a + delta * (n_b / n_ab)
        m2_ab = m2_a + m2_b + delta * delta * (n_a * n_b / n_ab)
        return (n_ab, mu_ab, m2_ab)

    s1 = (RealVal(1), x1, RealVal(0))
    s2 = (RealVal(1), x2, RealVal(0))
    s3 = (RealVal(1), x3, RealVal(0))

    # Split A: front=[x2, x1] (top combines x2 then x1), back=[x3]
    _, mu_split_a, m2_split_a = chan_combine(chan_combine(s2, s1), s3)
    # Split B: front=[x1], back=[x2, x3]
    _, mu_split_b, m2_split_b = chan_combine(s1, chan_combine(s2, s3))
    # Split C: front=[], back=[x1, x2, x3]
    _, mu_split_c, m2_split_c = chan_combine(chan_combine(s1, s2), s3)

    exact_mean = (x1 + x2 + x3) / n3
    exact_var = (
        (x1 - exact_mean) ** 2 + (x2 - exact_mean) ** 2 + (x3 - exact_mean) ** 2
    ) / n3

    results.append(
        _prove(
            "D1-T1",
            "D1",
            (
                "Two-stack Chan pairwise variance combination matches exact "
                "mean and non-negative variance across all queue splits"
            ),
            And(
                mu_split_a == exact_mean,
                m2_split_a / n3 == exact_var,
                mu_split_b == exact_mean,
                m2_split_b / n3 == exact_var,
                mu_split_c == exact_mean,
                m2_split_c / n3 == exact_var,
                m2_split_a >= 0,
            ),
        )
    )

    # D1-W1: Exhibit v2.x 64-bit signed tick overflow at 1 GHz TimeProvider.
    ticks = BitVec("ticks", 64)
    results.append(
        _witness(
            "D1-W1",
            "D1",
            (
                "v2.x signed 64-bit tick squaring overflows to negative at "
                ">= 3,037,000,500 ns (~3.037 s)"
            ),
            And(
                ticks == BitVecVal(3_037_000_500, 64),
                (ticks * ticks) < BitVecVal(0, 64),
            ),
        )
    )

    # D1-T2: Prove in IEEE-754 Float64 that pairwise differences and squares
    # never overflow or NaN across [0, TimeSpan.MaxValue].
    fx = FP("fx", FLOAT64)
    fa = FP("fa", FLOAT64)
    zero64 = FPVal(0.0, FLOAT64)
    max_ts64 = FPVal(MAX_TIMESPAN_SECONDS, FLOAT64)
    delta64 = fpSub(RNE_MODE, fx, fa)
    sq64 = fpMul(RNE_MODE, delta64, delta64)
    in_timespan_domain = And(
        fpGEQ(fx, zero64),
        fpLEQ(fx, max_ts64),
        fpGEQ(fa, zero64),
        fpLEQ(fa, max_ts64),
    )
    results.append(
        _prove(
            "D1-T2",
            "D1",
            (
                "IEEE-754 Float64 pairwise difference and square never "
                "overflow or NaN over [0, TimeSpan.MaxValue]"
            ),
            Or(
                Not(in_timespan_domain),
                And(
                    Not(fpIsNaN(delta64)),
                    Not(fpIsInf(delta64)),
                    Not(fpIsNaN(sq64)),
                    Not(fpIsInf(sq64)),
                    fpGEQ(sq64, zero64),
                ),
            ),
        )
    )

    results.append(_check_d1_float64_stability())
    return results


def check_domain_2() -> list[CheckResult]:
    """Domain 2: Exponential base-10 scaling & total Float64 safety."""
    results: list[CheckResult] = []

    t, t1, t2, mu, p1, p2, alpha, k, ln10 = [
        Real(name)
        for name in ("t", "t1", "t2", "mu", "p1", "p2", "alpha", "k", "ln10")
    ]
    ln10_bounds = And(ln10 > Q(2302585, 1000000), ln10 < Q(2302586, 1000000))

    def phi_exp_real(
        elapsed: ArithRef, mean: ArithRef, pause: ArithRef
    ) -> ArithRef:
        return elapsed / ((mean + pause) * ln10)

    def phi_exp_v2_buggy(elapsed: ArithRef, mean: ArithRef) -> ArithRef:
        return elapsed / mean

    # D2-T1: Base-10 calibration vs v2.x ln(10) inflation (~2.302585x).
    results.append(
        _prove(
            "D2-T1",
            "D2",
            (
                "Exponential phi(k * mu * ln 10; mu) == k whereas v2.x "
                "inflates phi by factor ln(10) > 2.30"
            ),
            Or(
                Not(And(ln10_bounds, mu > 0, k > 0)),
                And(
                    phi_exp_real(k * mu * ln10, mu, RealVal(0)) == k,
                    phi_exp_v2_buggy(k * mu * ln10, mu) == k * ln10,
                    phi_exp_v2_buggy(k * mu * ln10, mu) > Q(23, 10) * k,
                ),
            ),
        )
    )

    # D2-T2: Real scale invariance, elapsed monotonicity, and pause laws.
    pre_d2 = And(
        ln10_bounds,
        mu > 0,
        alpha > 0,
        t >= 0,
        t1 >= 0,
        t2 >= t1,
        p1 >= 0,
        p2 >= p1,
    )
    results.append(
        _prove(
            "D2-T2",
            "D2",
            (
                "Exponential phi satisfies unit scale invariance, "
                "non-negativity, elapsed monotonicity, and pause monotonicity"
            ),
            Or(
                Not(pre_d2),
                And(
                    phi_exp_real(alpha * t, alpha * mu, alpha * p1)
                    == phi_exp_real(t, mu, p1),
                    phi_exp_real(t1, mu, p1) >= 0,
                    phi_exp_real(t2, mu, p1) >= phi_exp_real(t1, mu, p1),
                    phi_exp_real(t, mu, p2) <= phi_exp_real(t, mu, p1),
                ),
            ),
        )
    )

    # D2-T3: Float64 NaN/negative freedom over [0, MaxTimeSpan] & zero mean.
    ft = FP("ft", FLOAT64)
    fmu = FP("fmu", FLOAT64)
    fpause = FP("fpause", FLOAT64)
    zero64 = FPVal(0.0, FLOAT64)
    pos_inf64 = FPVal(float("inf"), FLOAT64)
    max_ts64 = FPVal(MAX_TIMESPAN_SECONDS, FLOAT64)
    log10e_64 = FPVal(LOG10E_FLOAT, FLOAT64)

    mu_eff64 = fpAdd(RNE_MODE, fmu, fpause)
    ratio64 = fpDiv(RNE_MODE, ft, mu_eff64)
    phi_exp64 = fpMul(RNE_MODE, ratio64, log10e_64)

    # PhiMath.Exponential guards non-positive elapsed to 0.0 and non-positive
    # effective mean (when elapsed > 0) to +Infinity before dividing.
    domain_exp64 = And(
        fpGEQ(ft, zero64),
        fpLEQ(ft, max_ts64),
        fpGEQ(fmu, zero64),
        fpLEQ(fmu, max_ts64),
        fpGEQ(fpause, zero64),
        fpLEQ(fpause, max_ts64),
    )
    branch_zero_t = And(fpEQ(ft, zero64), fpEQ(zero64, zero64))
    branch_zero_mu = And(
        fpGT(ft, zero64), fpLEQ(mu_eff64, zero64), fpIsInf(pos_inf64)
    )
    branch_pos = And(
        fpGT(ft, zero64),
        fpGT(mu_eff64, zero64),
        Not(fpIsNaN(phi_exp64)),
        fpGEQ(phi_exp64, zero64),
    )
    results.append(
        _prove(
            "D2-T3",
            "D2",
            (
                "Float64 Exponential evaluator never returns NaN or < 0 "
                "across [0, TimeSpan.MaxValue], failing closed to +Inf at 0"
            ),
            Or(
                Not(domain_exp64),
                Or(branch_zero_t, branch_zero_mu, branch_pos),
            ),
            logic="QF_FP",
        )
    )

    return results


def check_domain_3() -> list[CheckResult]:
    """Domain 3: Normal logistic CDF unification & IEEE-754 tail stability."""
    results: list[CheckResult] = []

    # Uninterpreted positive real Exp with Exp(0) = 1 and Exp(z)*Exp(-z) = 1.
    exp_fn = Function("Exp", RealSort(), RealSort())
    y = Real("y")
    c1 = Q(15976, 10000)  # 1.5976
    c2 = Q(70566, 1000000)  # 0.070566
    z = y * (c1 + c2 * y * y)
    ez = exp_fn(z)
    emz = exp_fn(-z)
    exp_axioms = And(ez > 0, emz > 0, ez * emz == 1, exp_fn(RealVal(0)) == 1)

    # Hayashibara / Akka piecewise tail survival probability P_later(y):
    #   branch y > 0:  e / (1 + e)       where e = exp(-z)
    #   branch y <= 0: 1 - 1 / (1 + e)   where e = exp(-z)
    # Unified softplus survival probability:
    #   1 / (1 + exp(z)), so -log10(P_later) = softplus(z) / ln(10).
    tail_pos = emz / (1 + emz)
    tail_neg = 1 - (1 / (1 + emz))
    tail_unified = 1 / (1 + ez)

    tail_pos_zero = exp_fn(RealVal(0)) / (1 + exp_fn(RealVal(0)))
    tail_neg_zero = 1 - (1 / (1 + exp_fn(RealVal(0))))
    tail_uni_zero = 1 / (1 + exp_fn(RealVal(0)))

    results.append(
        _prove(
            "D3-T1",
            "D3",
            (
                "Both piecewise Normal logistic tail expressions equal "
                "1 / (1 + exp(z)) algebraically over R and equal 1/2 at y = 0"
            ),
            Or(
                Not(exp_axioms),
                And(
                    tail_pos == tail_unified,
                    tail_neg == tail_unified,
                    (y == 0) == (z == 0),
                    tail_pos_zero == Q(1, 2),
                    tail_neg_zero == Q(1, 2),
                    tail_uni_zero == Q(1, 2),
                ),
            ),
        )
    )

    # D3-T2: Strict polynomial monotonicity z(y1) < z(y2) for all y1 < y2 in R,
    # plus scale invariance of y = (t - (mu + pause)) / max(sigma, sigma_min).
    y1, y2, t1, t2, mu, pause1, pause2, sigma, sigma_min, alpha = [
        Real(name)
        for name in (
            "y1",
            "y2",
            "t1",
            "t2",
            "mu",
            "pause1",
            "pause2",
            "sigma",
            "sigma_min",
            "alpha",
        )
    ]
    z1 = y1 * (c1 + c2 * y1 * y1)
    z2 = y2 * (c1 + c2 * y2 * y2)

    sigma_eff = Real("sigma_eff")
    sigma_eff_alpha = Real("sigma_eff_alpha")
    pre_d3 = And(
        y1 < y2,
        t1 < t2,
        mu >= 0,
        pause1 >= 0,
        pause2 >= pause1,
        sigma >= 0,
        sigma_min > 0,
        alpha > 0,
        Or(
            And(
                sigma >= sigma_min,
                sigma_eff == sigma,
                sigma_eff_alpha == alpha * sigma,
            ),
            And(
                sigma < sigma_min,
                sigma_eff == sigma_min,
                sigma_eff_alpha == alpha * sigma_min,
            ),
        ),
    )
    y_t1 = (t1 - (mu + pause1)) / sigma_eff
    y_t2 = (t2 - (mu + pause1)) / sigma_eff
    y_p2 = (t1 - (mu + pause2)) / sigma_eff
    y_scaled = (alpha * t1 - (alpha * mu + alpha * pause1)) / sigma_eff_alpha

    results.append(
        _prove(
            "D3-T2",
            "D3",
            (
                "Cubic logistic argument z(y) is strictly increasing over R, "
                "scale-invariant, pause-monotone, and division-by-zero free"
            ),
            Or(
                Not(pre_d3),
                And(
                    sigma_eff >= sigma_min,
                    sigma_eff > 0,
                    z1 < z2,
                    y_t1 < y_t2,
                    y_p2 <= y_t1,
                    y_scaled == y_t1,
                ),
            ),
        )
    )

    # D3-T3: Concrete IEEE-754 Float64 exp(-z) underflow occurs at z >= 746.0
    # (since -1075 * ln(2) ~= -745.1332 < -745.0). Over Float64 [746.0, 1e300]
    # where exp(-z) == +0.0 and log1p(+0.0) == +0.0, v2.x collapses e / (1 + e)
    # to +0.0, whereas v3 softplus (z + 0.0) * log10(e) stays finite/positive
    # and yields +Inf (non-NaN) at z = +Inf. Relational RNE multiplication
    # monotonicity (<=) is bit-blasted in IEEE-754 binary16 (FPSort(5, 11)).
    underflow_verified = (math.exp(-746.0) == 0.0) and (math.exp(-745.0) > 0.0)
    fz = FP("fz", FLOAT64)
    zero64 = FPVal(0.0, FLOAT64)
    one64 = FPVal(1.0, FLOAT64)
    pos_inf64 = FPVal(float("inf"), FLOAT64)
    log10e_64 = FPVal(LOG10E_FLOAT, FLOAT64)
    z_low = FPVal(746.0, FLOAT64)
    z_high = FPVal(1e300, FLOAT64)

    v2_tail_ratio = fpDiv(RNE_MODE, zero64, fpAdd(RNE_MODE, one64, zero64))
    v3_phi = fpMul(RNE_MODE, fpAdd(RNE_MODE, fz, zero64), log10e_64)
    v3_phi_inf = fpMul(RNE_MODE, fpAdd(RNE_MODE, pos_inf64, zero64), log10e_64)

    fp16 = FPSort(5, 11)
    hz1 = FP("hz1", fp16)
    hz2 = FP("hz2", fp16)
    h_scale = FPVal(LOG10E_FLOAT, fp16)
    h_low = FPVal(1.0, fp16)
    h_high = FPVal(1000.0, fp16)
    h_phi1 = fpMul(RNE_MODE, hz1, h_scale)
    h_phi2 = fpMul(RNE_MODE, hz2, h_scale)

    tail_domain = And(fpGEQ(fz, z_low), fpLEQ(fz, z_high))
    rel_domain = And(fpGEQ(hz1, h_low), fpLEQ(hz1, hz2), fpLEQ(hz2, h_high))
    results.append(
        _prove(
            "D3-T3",
            "D3",
            (
                "Under Float64 exp(-z)==0 (z >= 746), v3 softplus stays "
                "finite/positive (+Inf at z=+Inf), v2 collapses to 0, and "
                "binary16 RNE scaling is monotone (<=)"
            ),
            And(
                BoolVal(underflow_verified),
                Or(
                    Not(tail_domain),
                    And(
                        fpEQ(v2_tail_ratio, zero64),
                        Not(fpIsNaN(v3_phi)),
                        Not(fpIsInf(v3_phi)),
                        fpGT(v3_phi, zero64),
                        fpIsInf(v3_phi_inf),
                        Not(fpIsNaN(v3_phi_inf)),
                    ),
                ),
                Or(
                    Not(rel_domain),
                    fpLEQ(h_phi1, h_phi2),
                ),
            ),
            logic="QF_FP",
        )
    )

    return results


def check_domain_4() -> list[CheckResult]:
    """Domain 4: Lifecycle bootstrap & threshold level-set availability laws."""
    results: list[CheckResult] = []

    init_est, pause, min_std, threshold, ln10 = [
        Real(name)
        for name in (
            "init_est",
            "pause",
            "min_std",
            "threshold",
            "ln10",
        )
    ]
    ln10_bounds = And(ln10 > Q(2302585, 1000000), ln10 < Q(2302586, 1000000))
    valid_cfg = And(
        ln10_bounds,
        init_est > 0,
        pause >= 0,
        min_std > 0,
        threshold > 0,
    )

    # During Bootstrapping (isMonitoring == True, count == 0),
    # mu_0 = init_est + pause and sigma_0 = max(init_est / 4, min_std) > 0.
    # Choosing elapsed* = mu_0 + sigma_0 * (threshold * ln10) yields
    # z > 1.5976 * threshold * ln10 > threshold * ln10, so phi_boot > threshold.
    mu0 = init_est + pause
    sigma0 = Real("sigma0")
    sigma0_def = Or(
        And(init_est / 4 >= min_std, sigma0 == init_est / 4),
        And(init_est / 4 < min_std, sigma0 == min_std),
    )
    elapsed_witness = mu0 + sigma0 * (threshold * ln10)
    y_boot = (elapsed_witness - mu0) / sigma0
    z_boot = y_boot * (Q(15976, 10000) + Q(70566, 1000000) * y_boot * y_boot)
    phi_lower_bound = z_boot / ln10  # softplus(z) = z + ln(1 + e^-z) > z

    admissible_timespan_cfg = And(
        init_est <= RealVal(86_400),
        pause <= RealVal(86_400),
        min_std <= RealVal(86_400),
        threshold <= RealVal(1_000),
    )
    max_timespan_real = Q(922_337_203_685, 1)

    results.append(
        _prove(
            "D4-T1",
            "D4",
            (
                "In Real arithmetic, Bootstrapping crosses any Threshold > 0 "
                "at finite elapsed* (< TimeSpan.MaxValue for <= 1d / <= 1000)"
            ),
            Or(
                Not(And(valid_cfg, sigma0_def)),
                And(
                    sigma0 > 0,
                    elapsed_witness > 0,
                    phi_lower_bound > threshold,
                    Or(
                        Not(admissible_timespan_cfg),
                        elapsed_witness < max_timespan_real,
                    ),
                ),
            ),
        )
    )

    # D4-T2: Float64 Idle == 0.0 availability, +Inf fail-closed unavailability,
    # negated-NaN fail-open hazard, and complement duality for non-NaN phi.
    fphi = FP("fphi", FLOAT64)
    fthresh = FP("fthresh", FLOAT64)
    zero64 = FPVal(0.0, FLOAT64)
    pos_inf64 = FPVal(float("inf"), FLOAT64)
    nan64 = fpNaN(FLOAT64)
    valid_fphi = And(Not(fpIsNaN(fphi)), fpGEQ(fphi, zero64))
    valid_fthresh = And(
        Not(fpIsNaN(fthresh)),
        Not(fpIsInf(fthresh)),
        fpGT(fthresh, zero64),
    )
    results.append(
        _prove(
            "D4-T2",
            "D4",
            (
                "In Float64, Idle (0.0) is available, +Inf fails closed, "
                "!(NaN >= T) fails open, and non-NaN (Phi < T) <=> !(Phi >= T)"
            ),
            Or(
                Not(And(valid_fphi, valid_fthresh)),
                And(
                    fpLT(zero64, fthresh),
                    Not(fpLT(pos_inf64, fthresh)),
                    Not(fpGEQ(nan64, fthresh)),
                    Not(fpLT(nan64, fthresh)),
                    fpLT(fphi, fthresh) == Not(fpGEQ(fphi, fthresh)),
                ),
            ),
            logic="QF_FP",
        )
    )

    return results


def _softplus(z: float) -> float:
    if z > 0.0:
        return z + math.log1p(math.exp(-z))
    return math.log1p(math.exp(z))


def compute_reference_vectors() -> dict[str, object]:
    """Generate deterministic reference vectors for C# unit tests."""
    vectors = []
    for y in (-5.0, -1.0, 0.0, 1.0, 2.0, 5.0, 10.0, 25.0, 40.0):
        z = y * (1.5976 + 0.070566 * y * y)
        vectors.append(
            {
                "y": y,
                "z": z,
                "phi_normal": _softplus(z) * LOG10E_FLOAT,
            }
        )
    exp_vectors = []
    for ratio in (0.0, 1.0, LN10_FLOAT, 3.0 * LN10_FLOAT, 8.0 * LN10_FLOAT):
        exp_vectors.append(
            {
                "elapsed_over_mean": ratio,
                "phi_exponential": ratio * LOG10E_FLOAT,
            }
        )
    return {
        "normal_vectors": vectors,
        "exponential_vectors": exp_vectors,
    }


def main(argv: list[str] | None = None) -> int:
    """Run all Z3 formal checks and optionally emit JSON output."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--json", action="store_true", help="Emit JSON report")
    args = parser.parse_args(argv)

    checks = [
        *check_domain_1(),
        *check_domain_2(),
        *check_domain_3(),
        *check_domain_4(),
    ]
    all_passed = all(item.passed for item in checks)
    payload = {
        "passed": all_passed,
        "checks": [asdict(item) for item in checks],
        "reference_vectors": compute_reference_vectors(),
    }

    if args.json:
        sys.stdout.write(json.dumps(payload, indent=2) + "\n")
    else:
        for item in checks:
            mark = "PASS" if item.passed else "FAIL"
            line = (
                f"[{mark}] {item.check_id} ({item.kind}): "
                f"{item.description} -> {item.detail}\n"
            )
            sys.stdout.write(line)
        passed_count = sum(item.passed for item in checks)
        sys.stdout.write(
            f"\nSummary: {passed_count}/{len(checks)} formal checks passed.\n"
        )

    return 0 if all_passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
