# frozen_string_literal: true

lib = File.expand_path("lib", __dir__)
$LOAD_PATH.unshift(lib) unless $LOAD_PATH.include?(lib)
require "hcoona/release_smoke/rubygems/version"
require "json"
require "open3"

nbgv_gemspec_version = lambda do |project_root|
  nbgv_path = ENV.fetch("THREE_WORKFLOW_RELEASE_NBGV_PATH", "").strip
  nbgv_command = nbgv_path.empty? ? ["dotnet", "tool", "run", "nbgv"] : [nbgv_path]
  stdout, stderr, status = Open3.capture3(
    *nbgv_command,
    "get-version",
    "--format",
    "json",
    chdir: project_root
  )

  unless status.success?
    message = stderr.strip
    raise "Failed to resolve hcoona-release-smoke-rubygems version with NBGV#{": #{message}" unless message.empty?}"
  end

  version = JSON.parse(stdout).fetch("SemVer2")
  Hcoona::ReleaseSmoke::Rubygems::Version.rubygems_version(version)
end

Gem::Specification.new do |spec|
  spec.name = "hcoona-release-smoke-rubygems"
  spec.version = nbgv_gemspec_version.call(__dir__)
  spec.authors = ["Shuai Zhang"]
  spec.email = ["zhangshuai.ustc@gmail.com"]
  spec.summary = "Smoke-test gem for validating the Three RubyGems release workflow."
  spec.description = "Minimal RubyGem used only by the Three monorepo release-smoke workflow."
  spec.homepage = "https://github.com/hcoona/three/blob/main/src/public/lib/hcoona-release-smoke-rubygems/README.md"
  spec.license = "LGPL-3.0-or-later WITH LGPL-3.0-linking-exception"
  spec.required_ruby_version = Gem::Requirement.new(">= 3.2")
  spec.metadata = {
    "homepage_uri" => spec.homepage,
    "source_code_uri" => "https://github.com/hcoona/three",
    "github_repo" => "https://github.com/hcoona/three",
    "bug_tracker_uri" => "https://github.com/hcoona/three/issues",
    "documentation_uri" => spec.homepage
  }
  spec.files = Dir["lib/**/*", "README.md"]
  spec.require_paths = ["lib"]
end
