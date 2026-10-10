# frozen_string_literal: true

require_relative "lib/hcoona_release_smoke_ruby/version"

Gem::Specification.new do |spec|
  spec.name = "hcoona-release-smoke-ruby"
  spec.version = HcoonaReleaseSmokeRuby::VERSION
  spec.summary = "Minimal Ruby marker for Workflow Delivery v3 acceptance."
  spec.description = "A dependency-free smoke gem for original-package delivery and clean installation."
  spec.authors = ["Shuai Zhang"]
  spec.license = "MIT"
  spec.homepage = "https://github.com/hcoona/three"
  spec.metadata = { "github_repo" => "https://github.com/hcoona/three" }
  spec.required_ruby_version = ">= 4.0"
  spec.platform = Gem::Platform::RUBY
  spec.require_paths = ["lib"]
  package_files = [
    "LICENSE",
    "README.md",
    "lib/hcoona_release_smoke_ruby.rb",
    "lib/hcoona_release_smoke_ruby/version.rb"
  ]
  legacy_provenance = "lib/hcoona_release_smoke_ruby/_workflow_delivery_provenance.json"
  package_files << legacy_provenance if File.file?(legacy_provenance)
  spec.files = package_files
end
