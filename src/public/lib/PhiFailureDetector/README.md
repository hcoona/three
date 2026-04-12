# Phi Failure Detector

A high-performance implementation of the phi accrual failure detector for distributed systems in .NET.

## Overview

The Phi Failure Detector is an adaptive failure detection algorithm that provides more accurate and flexible failure detection compared to traditional timeout-based approaches. It uses statistical analysis of heartbeat intervals to calculate a phi value that represents the suspicion level of a node failure.

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
using PhiFailureDetector;

// Create a failure detector with exponential distribution model
var detector = new PhiFailureDetector(
    capacity: 100,                           // Number of samples to keep
    initialHeartbeatInterval: 1000,          // Initial interval in milliseconds
    timeProvider: TimeProvider.System,       // Time provider
    phiFunc: PhiFailureDetector.Exponential  // Distribution model
);

// Record heartbeats when they arrive
detector.Report();

// Later, check the current phi value (suspicion level)
double phi = detector.Phi();

// A phi value above your threshold indicates suspected failure
// Common thresholds: 3.0 (99.9% confidence), 8.0 (99.999% confidence)
bool isSuspected = phi > 8.0;
```

## Advanced Usage

### Using Normal Distribution Model

```csharp
// For more regular heartbeat patterns, use normal distribution
var detector = new PhiFailureDetector(
    capacity: 100,
    initialHeartbeatInterval: 1000,
    timeProvider: TimeProvider.System,
    phiFunc: PhiFailureDetector.Normal
);
```

### Custom Time Provider

```csharp
// Use a custom time provider for testing or specific scenarios
var customTimeProvider = new MockTimeProvider();
var detector = new PhiFailureDetector(
    capacity: 100,
    initialHeartbeatInterval: 1000,
    timeProvider: customTimeProvider,
    phiFunc: PhiFailureDetector.Exponential
);
```

### Monitoring Node Health

```csharp
// Example of monitoring a distributed node
class NodeMonitor
{
    private readonly PhiFailureDetector detector;
    private readonly double failureThreshold = 8.0; // 99.999% confidence

    public NodeMonitor()
    {
        detector = new PhiFailureDetector(
            capacity: 200,
            initialHeartbeatInterval: 5000, // 5 seconds
            timeProvider: TimeProvider.System,
            phiFunc: PhiFailureDetector.Exponential
        );
    }

    public void OnHeartbeatReceived()
    {
        detector.Report();
    }

    public bool IsNodeSuspected()
    {
        return detector.Phi() > failureThreshold;
    }

    public double GetSuspicionLevel()
    {
        return detector.Phi();
    }
}
```

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

- The detector maintains a sliding window of heartbeat intervals for statistical analysis
- Memory usage is proportional to the configured capacity
- Calculation complexity is O(1) for both reporting heartbeats and calculating phi values
- Thread-safe operations allow concurrent access from multiple threads

## Algorithm Reference

Based on the paper: "The φ accrual failure detector" by N. Hayashibara, X. Defago, R. Yared, and T. Katayama, published in Proceedings of the 23rd IEEE International Symposium on Reliable Distributed Systems, 2004.

The implementation provides two distribution models:

1. **Exponential Distribution**: Suitable for Poisson processes (random message intervals)
2. **Normal Distribution**: Suitable for regular heartbeat patterns with typical jitter

## License

LGPL-3.0-or-later WITH LGPL-3.0-linking-exception. See [LICENSE](https://github.com/hcoona/three/blob/main/LICENSE.LGPLv3-linking-exception.txt) for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request to the [hcoona/three repository](https://github.com/hcoona/three).

## Support

If you encounter any issues or have questions, please [open an issue](https://github.com/hcoona/three/issues) on GitHub.
