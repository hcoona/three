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

Source packaging requires V3 to materialize the frozen NBGV version and source
witness in an isolated staging directory before evaluating the gemspec.
Generated files are not committed; a direct unstamped `gem build` fails.
Installed consumers need neither Git, NBGV nor .NET. The
[V3 Ruby contract](../three-workflow-delivery-v3/docs/hcoona-release-smoke-ruby-lld.md)
owns build, qualification and destination behavior. Source presence does not
claim registry availability or completed publication.
