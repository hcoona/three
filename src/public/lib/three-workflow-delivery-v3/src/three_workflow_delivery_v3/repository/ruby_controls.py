"""Ruby-owned protected inputs shared by source and operation admission."""

RUBY_ENVELOPE_PATH = (
    ".github/workflow-delivery/requests/hcoona-release-smoke-ruby.json"
)
RUBY_CONFIGURATION_PATHS = (
    (
        ".github/workflow-delivery/configuration/"
        "hcoona-release-smoke-ruby-github-packages.json"
    ),
    (
        ".github/workflow-delivery/configuration/"
        "hcoona-release-smoke-ruby-rubygems.json"
    ),
)
RUBY_GOVERNANCE_PATHS = (
    (
        ".github/workflow-delivery/governance/"
        "hcoona-release-smoke-ruby-github-packages.json"
    ),
    (
        ".github/workflow-delivery/governance/"
        "hcoona-release-smoke-ruby-rubygems.json"
    ),
)
RUBY_CONTROL_PATHS = (
    *RUBY_GOVERNANCE_PATHS,
    RUBY_ENVELOPE_PATH,
    *RUBY_CONFIGURATION_PATHS,
    "src/public/lib/three-workflow-delivery-v3/docs/ruby-operation-protocol.md",
)
