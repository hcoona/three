# Phi Failure Detector

A formally specified, numerically stable implementation of the
$\varphi$ accrual failure detector for distributed systems in .NET.

## Overview

Instead of treating node health as a fixed binary timeout, the $\varphi$ accrual failure
detector continuously outputs a suspicion value $\varphi(t) = -\log_{10} P_{\text{model}}(T_{\text{now}} - T_{\text{last}})$
based on the fitted arrival distribution over recent heartbeat intervals (`Exponential`
matches $10^{-\varphi}$ exactly; `Normal` uses Page's 1977 cubic logistic approximation
adopted by Akka and Apache Cassandra, which preserves their operational threshold scale
such as `8.0`–`16.0` while growing slightly faster than the exact Gaussian `erfc` tail
at extreme $z$-scores, e.g., $\varphi = 16$ corresponds to a true Gaussian tail near
$10^{-12.2}$):

- $\varphi = 1.0 \implies 10^{-1}$ ($10\%$) modeled survival tail probability
- $\varphi = 3.0 \implies 10^{-3}$ ($0.1\%$) modeled survival tail probability
- $\varphi = 8.0 \implies 10^{-8}$ modeled survival tail (default production threshold)
- $\varphi = 16.0 \implies 10^{-16}$ modeled survival tail (conservative WAN / heavy-GC environments)

## Features

- **Clean Domain API (`v3.0`)**: Immutable `PhiAccrualFailureDetectorOptions` record with
  `TimeSpan` durations, optional constructor `TimeProvider` injection, zero-allocation
  atomic `PhiSnapshot` (`Evaluate()`), clean `HeartbeatStatistics` (`SampleCount`, `Mean`,
  `StandardDeviation`), and explicit `Reset()` across peer reboots.
- **Linearizable Concurrency**: Single-point atomic `(state, clock)` snapshot acquisition
  prevents stale pre-heartbeat timestamps or torn multi-field observations under concurrent
  `RecordHeartbeat()`, `Evaluate()`, `GetPhi()`, `IsAvailable()`, and `Reset()` calls.
- **Deep-Tail Numerical Stability**: Base-10 Normal survival evaluation uses a smooth
  softplus formulation of Page's cubic logistic normal-tail approximation, remaining
  finite and monotonic at extreme $z$-scores ($y \ge 25$) where `Math.Exp(-z)` underflows
  IEEE-754 `double` to `0.0`.
- **Two-Stack Chan Sliding Window**: $O(1)$ amortized pairwise-variance queue in `double`
  seconds avoids subtracting squared sums, preventing 64-bit integer tick-squaring
  overflow, low-jitter cancellation, and post-outlier variance collapse upon eviction.
- **Formal Specification & Verification**: Backed by Z3 real-arithmetic / IEEE-754
  `Float64` proofs ([`scripts/verify_formal_spec.py`](scripts/verify_formal_spec.py)), a
  TLC-checked TLA+ state-machine & concurrency model
  ([`docs/models/PhiAccrualFailureDetector.tla`](docs/models/PhiAccrualFailureDetector.tla)),
  and `CsCheck` property-based tests.
- **Multi-Framework & AOT Ready**: Targets `netstandard2.0`, `netstandard2.1`, `net462`,
  `net8.0`, and `net9.0` with zero reflection and native AOT compatibility on modern .NET.

## Installation

```bash
dotnet add package PhiFailureDetector --prerelease
```

## Quick Start

```csharp
using System;
using PhiFailureDetector;

var detector = new PhiAccrualFailureDetector(
    new PhiAccrualFailureDetectorOptions
    {
        Threshold = 8.0,
        MaxSampleSize = 1000,
        MinStandardDeviation = TimeSpan.FromMilliseconds(100),
        AcceptableHeartbeatPause = TimeSpan.FromSeconds(3),
        InitialHeartbeatEstimate = TimeSpan.FromSeconds(1),
        Distribution = PhiDistribution.Normal,
    },
    TimeProvider.System);

// Record heartbeats as they arrive from the monitored peer
detector.RecordHeartbeat();

// Atomically evaluate availability, phi, elapsed silence, and window statistics
PhiSnapshot snapshot = detector.Evaluate();
if (!snapshot.IsAvailable)
{
    Console.WriteLine(
        $"Peer suspected: phi={snapshot.Phi:F2}, elapsed={snapshot.Elapsed}, stats={snapshot.Statistics}");
}

// Fast scalar checks when full diagnostics are not needed
bool available = detector.IsAvailable();
double suspicion = detector.GetPhi();

// Clear history when the peer reboots or starts a new incarnation epoch
detector.Reset();
```

## Choosing a Distribution and Options

| Option                     | Default        | Guidance                                                                                                                                                                                                                          |
| -------------------------- | -------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Threshold`                | `8.0`          | Suspicion cutoff (`IsAvailable()` returns `GetPhi() < Threshold`). Use `8.0`–`12.0` on LANs; `12.0`–`16.0` on WANs or noisy clouds.                                                                                               |
| `Distribution`             | `Normal`       | Use `Normal` for periodic heartbeats with Gaussian jitter (Hayashibara et al. / Akka / Pekko). Use `Exponential` for memoryless/Poisson gossip arrivals ([CASSANDRA-2597](https://issues.apache.org/jira/browse/CASSANDRA-2597)). |
| `AcceptableHeartbeatPause` | `00:00:00`     | Tolerated transient stall (for example GC pauses or brief packet drops) before suspicion begins to climb steeply.                                                                                                                 |
| `InitialHeartbeatEstimate` | `00:00:01`     | Conservative interval estimate used after the very first heartbeat (`Bootstrapping` state) before a real inter-arrival interval is observed.                                                                                      |
| `MinStandardDeviation`     | `00:00:00.100` | Floor on $\sigma$ in `Normal` mode so near-zero jitter on localhost/LAN never triggers hair-trigger false positives.                                                                                                              |
| `MaxSampleSize`            | `1000`         | Sliding window capacity bounding memory to $O(W)$ and letting the detector adapt when heartbeat cadence changes.                                                                                                                  |

## Lifecycle Semantics

Each `PhiAccrualFailureDetector` instance tracks a single monitored peer through three
states observable via `PhiSnapshot snapshot = detector.Evaluate()`:

1. **Idle** (`snapshot.IsMonitoring == false`, `snapshot.Elapsed is null`,
   `snapshot.Statistics.SampleCount == 0`): No heartbeat has been observed yet (or
   `Reset()` was called). `GetPhi()` returns `0.0` and `IsAvailable()` returns `true`.
2. **Bootstrapping** (`snapshot.IsMonitoring == true`,
   `snapshot.Statistics.SampleCount == 0`): Exactly one heartbeat has been recorded.
   Suspicion is evaluated against a synthetic prior anchored at `InitialHeartbeatEstimate`
   so a node that crashes immediately after its first heartbeat still accrues suspicion
   monotonically without `0 / 0 = NaN` degeneracy.
3. **Active** (`snapshot.IsMonitoring == true`, `snapshot.Statistics.SampleCount >= 1`):
   Suspicion is evaluated from the empirical sliding window of inter-arrival intervals.

## Formal Verification

From the repository root (with [MISE](https://mise.jdx.dev/) configured):

```bash
# Run Z3 real-arithmetic & IEEE-754 Float64 formal proofs
mise run phi-failure-detector:verify-formal-spec

# Model-check the TLA+ lifecycle & concurrency specification with TLC
mise run tla:tlc src/public/lib/PhiFailureDetector/docs/models/PhiAccrualFailureDetector.tla \
  -config src/public/lib/PhiFailureDetector/docs/models/PhiAccrualFailureDetector.cfg
```

## Algorithm Reference

Based on ["The φ accrual failure detector"](https://ieeexplore.ieee.org/abstract/document/1353004/)
by N. Hayashibara, X. Défago, R. Yared, and T. Katayama, _Proceedings of the 23rd IEEE
International Symposium on Reliable Distributed Systems (SRDS 2004)_, pp. 66–78
([open PDF](https://dspace.jaist.ac.jp/dspace/bitstream/10119/4784/1/IS-RR-2004-010.pdf)).

## Releasing

Workflow Delivery releases the `PhiFailureDetector` package through the manually
dispatched `.github/workflows/workflow-delivery-v3-nuget-release.yml` caller with `unit:
phi-failure-detector`, as declared by `workflow-delivery.release-unit.yml`. Use
`registry: github-packages` for GitHub Packages (Buddy) from any branch, or `registry:
nugetorg` for nuget.org (Official) from `main` only. The package version is the NBGV
projection of this directory's `version.json` at the selected revision. Preparation
packs every target framework, checks the native package contents and consumes the
original package in a clean project before any publication.

## License

LGPL-3.0-or-later WITH LGPL-3.0-linking-exception. See [LICENSE](https://github.com/hcoona/three/blob/main/LICENSES/LGPL-3.0-linking-exception.txt) for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request to the [hcoona/three repository](https://github.com/hcoona/three).

## Support

If you encounter any issues or have questions, please [open an issue](https://github.com/hcoona/three/issues) on GitHub.
