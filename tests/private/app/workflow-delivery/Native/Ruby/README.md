# Native Ruby helper tests

This bundle-only RSpec carrier exercises the actual `facts.rb` and `source.rb`
subprocesses. It uses the committed native wire descriptions and
`json_schemer` (Draft 2020-12, including schema validation). It requires no C#
build, product gemspec, registry access or credentials. Source tests substitute
only the public RubyGems Source methods in `spec/support/public_source_answers.rb`;
RubyGems owns tuple/specification/URI behavior and the actual helper main remains
in the child process. All credential strings are inert fixture values.

The main `Gemfile.lock` selects Bundler 2.7.2. The conventional nested
`contexts/bundler-2.4/Gemfile.lock` selects Bundler 2.4.20 and reuses the main
Gemfile declarations. Existing native inventory/preparation must prepare both
bundles in its owned environment before running the main carrier. The specs read
Bundler versions and the public RSpec fixture version from those locks, and use
native Bundler setup to obtain the prepared gem paths. There is no caller-side
package install roster or installation during tests. Ruby is selected by the
root mise configuration.

From this directory, with native tools and both locked bundles prepared in the
owned environment:

```sh
bundle exec rspec --dry-run
bundle exec rspec --format documentation --format json --out "$TEST_RESULT_PATH"
```

Set `TEST_RESULT_PATH` to an absolute owned result path. The first command
must discover the declared suite rather than an empty successful run. Ordinary
execution uses the existing `ruby/rspec-v1` project owner; root integration owns its selection and required result.

Each example uses fresh temporary state outside the checkout, closed child
configuration, EOF stdin and a 30-second deadline. Its fixture inputs and child
diagnostics are removed after execution. The ordinary group result retains the
original RSpec formatter report, including native failure details, and the outer
command's stdout/stderr and terminal outcome. No publication or hosted-destination
qualification is established by this suite.
