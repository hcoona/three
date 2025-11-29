# Contract: Cache Key & Disk Cache

## Purpose
Guarantee deterministic reuse of previously rendered artifacts when and only when all semantically relevant inputs are unchanged.

## Key Composition (Ordered Fields)
1. ext_version (extension version)
2. content_hash (SHA256 of normalized LaTeX source per Normalization-E)
3. format (svg|pdf|png)
4. preamble_hash (SHA256 of effective preamble text or '-')
5. ppi (png only else '-')
6. entry_type (block|inline)

Excluded (by spec FR-011): engine name, converter tool name, any tool / engine version, pipeline signature digest (概念合并), timeout, artifacts dir, cachedir path.

Hash Function: `SHA256(fields.join("\n"))` — stable join with newline separator; no trailing newline.

## API
```ruby
class CacheKey
  FIELDS_ORDER = %i[ext_version content_hash format preamble_hash ppi entry_type].freeze
  def initialize(ext_version:, content_hash:, format:, preamble_hash:, ppi:, entry_type:); end
  def digest; end # => hex string
end

class DiskCache
  # @return [CacheEntry,nil]
  def fetch(key_digest); end
  # Atomically store cache entry + artifact copy
  def store(key_digest, cache_entry, source_file); end
  # Advisory lock for expensive renders
  def with_lock(key_digest); yield; end
end
```

## CacheEntry Serialization
`metadata.json` structure:
```json
{
  "version": 1,
  "key": "<digest>",
  "format": "svg",
  "engine": "pdflatex", // stored for diagnostics only (not part of key)
  "content_hash": "...",
  "preamble_hash": "...",
  "ppi": 300,
  "entry_type": "block",
  "tool_presence": {"pdflatex": true, "dvisvgm": false},
  "created_at": "2025-10-02T12:34:56Z",
  "checksum": "sha256:...",
  "size_bytes": 1234
}
```

## Atomic Write Protocol
1. Render to temp path: `<cachedir>/tmp-<pid>-<rand>.out`.
2. Verify checksum.
3. Write `metadata.json.tmp`.
4. `File.rename(temp, final)`; `File.rename(metadata.tmp, metadata)`.
5. If conflict (file exists): discard temp (another process won race) after verifying identical checksum.

## Conflict Handling
- If same explicit target basename chosen for differing signatures, raise `TargetConflictError` (integration with ConflictRegistry) BEFORE storing.

## Tests
| Spec | Purpose |
|------|---------|
| `spec/cache/key_uniqueness_spec.rb` | Changing any included field modifies digest |
| `spec/cache/hit_miss_spec.rb` | Hit path bypasses renderer execution (stub) |
| `spec/cache/concurrency_spec.rb` | Simulated parallel store leads to single final file |
| `spec/cache/corruption_spec.rb` | Corrupted checksum triggers re-render |
| `spec/cache/engine_switch_stability_spec.rb` | Switching engine/tool does NOT alter digest |

## Non-Goals
- Eviction policies (TTL / size) – deferred (FR-039)
- Cross-version migration tooling – future
- Recording tool / engine versions (I2)

## Migration Note (I5)
`pipeline_signature_digest` & `tool_versions` removed before implementation (no stored historical data). If upgrading from a prototype storing these, legacy cache entries simply become cold misses.

