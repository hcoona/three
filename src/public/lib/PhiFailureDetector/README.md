# Phi Failure Detector

A high-performance implementation of the phi accrual failure detector for distributed systems in .NET.

## Overview

The Phi Failure Detector is an adaptive failure detection algorithm that provides more accurate and flexible failure detection compared to traditional timeout-based approaches. It uses statistical analysis of heartbeat intervals to calculate a phi value that represents the suspicion level of a node failure, applicable to cluster node failure detection, periodic heartbeat monitoring, network partition detection, and load-balancer health routing.

## Features

- **Adaptive Detection**: Automatically adjusts to network conditions and system load
- **Statistical Analysis**: Uses historical heartbeat data for accurate failure prediction
- **Configurable Thresholds**: Customizable phi values for different reliability requirements
- **High Performance**: Optimized for low-latency distributed systems
- **Multi-Framework Support**: Compatible with .NET Standard 2.0+, .NET Framework 4.6.2+, .NET 8.0, and .NET 9.0
- **Multiple Distribution Models**: Supports both exponential and normal distribution models
- **Thread-Safe**: Designed for concurrent usage in distributed systems

## Installation

```bash
dotnet add package PhiFailureDetector
```

## Quick Start

```csharp
using System;
using Detector = PhiFailureDetector.PhiFailureDetector;

var timeProvider = TimeProvider.System;
var detector = new Detector(
    capacity: 100,
    initialHeartbeatInterval: timeProvider.TimestampFrequency / 10, // 100 ms in timestamp ticks
    timeProvider: timeProvider,
    phiFunc: Detector.Exponential
);

// Record heartbeats when they arrive
detector.Report();

// Later, check the current phi value (suspicion level)
double phi = detector.Phi();

// A phi value above your threshold indicates suspected failure
bool isSuspected = phi > 8.0;
```

The detector uses high-resolution timestamps from `TimeProvider.GetTimestamp()`.
Set `initialHeartbeatInterval` in the same timestamp ticks, using the provider's
`TimestampFrequency` to convert a duration. This also applies to custom time
providers used for tests or simulations. Choose a conservatively long startup
interval for your cluster to trade slower initial detection for fewer false
positives and avoid flapping before real samples accumulate.

## Architecture Overview

1. **Monitoring:** Record heartbeat intervals in a bounded arrival window. Use
   a throttler alongside the window when you need to limit the sampling rate.
2. **Interpretation:** Choose `Exponential` for Poisson gossip arrivals and
   `Normal` (Gaussian) for regular heartbeats with jitter. See
   [CASSANDRA-2597](https://issues.apache.org/jira/browse/CASSANDRA-2597) for
   why the distribution matters.
3. **Action:** The caller compares the phi value with its threshold and decides
   how to respond, such as marking a peer as suspected.

## Phi Value Interpretation

The phi value represents the suspicion level of node failure:

- **0-3**: Low suspicion - Node is likely healthy
- **3-8**: Medium suspicion - Some concern about node health
- **8+**: High suspicion - Node is likely failed (99.999% confidence)

Common threshold values:

- `3.0`: 99.9% confidence of failure
- `8.0`: 99.999% confidence of failure (recommended for production)
- `16.0`: 99.99999% confidence of failure (very conservative)

## Performance Considerations

- Memory usage is proportional to the configured capacity
- Calculation complexity is O(1) for both reporting heartbeats and calculating phi values
- Thread-safe operations allow concurrent access from multiple threads

## Algorithm Reference

Based on ["The φ accrual failure detector"](https://ieeexplore.ieee.org/abstract/document/1353004/)
by N. Hayashibara, X. Defago, R. Yared, and T. Katayama, published in Proceedings
of the 23rd IEEE International Symposium on Reliable Distributed Systems, 2004
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
