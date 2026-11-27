# Release Notes

## Version 3.0.0

### Breaking Changes

- Replaced `PhiFailureDetector`, `IWithStatistics`, `LongIntervalHistoryCollection`, and
  raw tick `PhiFunc` delegates with `PhiAccrualFailureDetector`, immutable
  `PhiAccrualFailureDetectorOptions`, `PhiDistribution`, `PhiSnapshot`, and
  `HeartbeatStatistics`.
- Replaced raw timestamp-tick configuration parameters with `TimeSpan` options
  (`MinStandardDeviation`, `AcceptableHeartbeatPause`, `InitialHeartbeatEstimate`) and
  optional constructor `TimeProvider` injection.
- Replaced separate volatile state reads with atomic `PhiSnapshot Evaluate()` alongside
  verb methods `RecordHeartbeat()`, `GetPhi()`, `IsAvailable()`, and `Reset()`.

### Bug Fixes & Numerical / Concurrency Improvements

- Fixed single-heartbeat bootstrap degeneracy (`0.0 / 0.0 = NaN` in v2) via an explicit
  `Idle -> Bootstrapping -> Active` lifecycle anchored at `InitialHeartbeatEstimate`.
- Fixed 64-bit integer tick-squaring overflow (previously overflowing `long` at `~3.037 s`
  intervals on 1 GHz `TimeProvider.System`), low-jitter variance cancellation, and
  post-outlier eviction variance collapse via a two-stack Chan pairwise-variance sliding
  window in `double` seconds.
- Fixed `PhiDistribution.Exponential` scale mismatch (`(t / mean) * log10(e)` instead of
  the unscaled `t / mean` natural-log ratio, scaling $\phi$ by $\approx 0.4343\times$ relative to v2),
  aligning both `Normal` and `Exponential` modes to $-\log_{10} P(T > t)$.
- Fixed `PhiDistribution.Normal` deep-tail underflow to `+Infinity` (`y >= 25.4` where
  `Math.Exp(-z) == 0.0`) using a unified softplus evaluator and Goldberg's `log1p`
  formulation across all target frameworks.
- Fixed non-linearizable queries by acquiring `(IsMonitoring, Moments, Elapsed)` atomically
  under a single lock (`System.Threading.Lock` on .NET 9+).
- Added TLA+/TLC and Z3 formal specifications plus `CsCheck` property-based verification.

## Version 2.1.1

- Relicense: Re-licensed NuGet package to LGPL-3.0-or-later WITH LGPL-3.0-linking-exception.

## Version 2.0.2

### Features

- Initial release of the Phi Failure Detector implementation
- Support for .NET Standard 2.0, 2.1, .NET Framework 4.6.2, .NET 8.0, and .NET 9.0
- Adaptive failure detection algorithm based on statistical analysis
- Configurable phi thresholds for different reliability requirements
- High-performance implementation optimized for distributed systems
- Comprehensive unit tests and documentation

### API

- `PhiFailureDetector` class for failure detection
- `Heartbeat()` method to record node activity
- `IsAvailable()` method to check node availability
- `Phi()` method to get current suspicion level
- `IWithStatistics` interface for statistical monitoring

### Dependencies

- Microsoft.Bcl.TimeProvider is required only for .NET Standard 2.0, .NET Standard 2.1, and .NET Framework 4.6.2. For .NET 8.0 and .NET 9.0, TimeProvider is built into the runtime and no package reference is needed.

### Documentation

- Complete API documentation
- Usage examples and best practices
- Algorithm explanation and references
