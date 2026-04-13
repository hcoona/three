# Phi Failure Detector

This is a C# implementation of the [Phi Accrual Failure Detector](http://ieeexplore.ieee.org/abstract/document/1353004/) (PDF version available in [this research paper](https://dspace.jaist.ac.jp/dspace/bitstream/10119/4784/1/IS-RR-2004-010.pdf)).

## Getting Started

The interval time unit is determined by the TimeProvider used. By default, it uses the high-resolution timestamp from `TimeProvider.GetTimestamp()`.

We use a very large initial interval since the "right" average depends on the cluster size and it's better to err high (false negatives, which will be corrected by waiting a bit longer) than low (false positives, which cause "flapping").

Choose φ function according to your detecting model: use exponential function for heartbeat, use normal (Gaussian) function for random gossip.

```csharp
var failureDetector = new PhiFailureDetector(
    capacity: 100, // Store at most 100 heartbeat points
    initialHeartbeatInterval: TimeSpan.FromSeconds(2).Ticks,
    timeProvider: TimeProvider.System, // Or your custom TimeProvider
    phiFunc: PhiFailureDetector.Exponential
);

communicationService[peerId].onHeartBeat += (ignored1, ignored2) => {
    failureDetector.Report();
};
communicationService.watch(peerId, () => failureDetector.Phi() > threshold);
```

## Architecture Overview

The Phi Failure Detector consists of 3 components:

1. **Monitoring**
2. **Interpretation**
3. **Action**

### Monitoring

Arrival window. Usually used with a throttler.

### Interpretation

The phi function should be designed according to the heartbeat distribution.

See [CASSANDRA-2597](https://issues.apache.org/jira/browse/CASSANDRA-2597) for more details.

## Features

- **High-precision timing**: Uses `TimeProvider` for accurate timestamp generation
- **Configurable capacity**: Adjustable window size for heartbeat history
- **Flexible phi functions**: Support for both exponential and normal distribution models
- **Time provider abstraction**: Compatible with custom time providers for testing and simulation

## Usage Scenarios

- **Distributed systems**: Detect node failures in cluster environments
- **Heartbeat monitoring**: Monitor service availability through periodic signals
- **Network partition detection**: Identify communication failures between nodes
- **Load balancer health checks**: Integrate with load balancing systems for traffic routing decisions
