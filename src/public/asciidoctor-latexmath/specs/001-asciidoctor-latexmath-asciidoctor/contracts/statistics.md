# Contract: Statistics Output (FR-022 / FR-035)

## Purpose
Provide minimal, stable, machine-parseable single-line summary of rendering session performance when log level ≥ info.

## Format (Exact)
```
latexmath stats: renders=<int> cache_hits=<int> avg_render_ms=<int> avg_hit_ms=<int>
```
- Field order & names FIXED; adding/removing/reordering requires new FR + version bump.
- Values are non-negative integers.
- `avg_render_ms` = arithmetic mean of wall-clock milliseconds for actual renders (excludes cache hits) rounded half-up.
- `avg_hit_ms` = arithmetic mean milliseconds for cache hit fast path (0 if no hits) rounded half-up.

## Emission Rules
| Condition | Emitted? | Notes |
|-----------|----------|-------|
| Log level < info (e.g., quiet) | No | Silent suppression |
| renders + cache_hits == 0 | No | Nothing processed |
| Multiple documents (separate runs) | Yes (one line per run) | Each independent |
| Multiple invocations in same doc (should not happen) | Exactly 1 | Guard with idempotence flag |

## API Sketch
```ruby
class StatsCollector
  def record_render(duration_ms); end
  def record_hit(duration_ms); end
  def to_line # => String or nil per rules
  end
end
```

## Determinism
- Stats NOT part of cache key.
- Stats collection must not mutate rendering behavior.

## Tests
| Spec | Coverage |
|------|----------|
| `spec/statistics/format_spec.rb` | Regex match + field ordering |
| `spec/statistics/suppression_spec.rb` | quiet level suppresses output |
| `spec/statistics/zero_activity_spec.rb` | No output when no events |
| `spec/statistics/rounding_spec.rb` | Rounding half-up behavior |

## Non-Goals
- Percentiles, histograms, memory stats.
- JSON or multi-line output.
- Per-expression detailed tracing (belongs to DEBUG logs).

