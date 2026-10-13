# Ruby Release Smoke

This pure Ruby gem provides `HcoonaReleaseSmokeRuby.project_id` and `VERSION`
for Workflow Delivery v3 acceptance. It has no runtime dependencies or native
extension. Initial build and consumer validation uses the repository-pinned
Ruby on Ubuntu.

```ruby
require "hcoona_release_smoke_ruby"

HcoonaReleaseSmokeRuby.project_id # => "hcoona-release-smoke-ruby"
HcoonaReleaseSmokeRuby::VERSION   # Matches the installed gem specification.
```

Source packaging requires the owning build integration to materialize the NBGV
version in an isolated staging directory before evaluating the gemspec.
Generated files are not committed; a direct unstamped `gem build` fails.
The gemspec includes an existing source witness only when present; the current
native builder neither creates nor requires that file. Native metadata and
archive inspection remain separate operations. Installed consumers need neither
Git, NBGV nor .NET. The
[ordinary Ruby integration](../../../../../../../src/private/app/workflow-delivery/README.md#ordinary-ruby-release)
owns current build, qualification and destination behavior. The
[historical Ruby contract](../../../../../../../src/private/app/workflow-delivery/docs/hcoona-release-smoke-ruby-lld.md)
retains the closed campaign's original five-file package interpretation; it does
not prescribe current packaging or grant another operation. Source presence
does not establish registry availability or a publication result.
