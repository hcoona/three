# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

lib = File.expand_path("lib", __dir__)
$LOAD_PATH.unshift(lib) unless $LOAD_PATH.include?(lib)
require "asciidoctor/latexmath/version"
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
    raise "Failed to resolve asciidoctor-latexmath version with NBGV#{": #{message}" unless message.empty?}"
  end

  version = JSON.parse(stdout).fetch("SemVer2")
  Asciidoctor::Latexmath::Version.rubygems_version(version)
end

gem_name = "asciidoctor-latexmath"

gemspec = Gem::Specification.new do |spec|
  spec.name = gem_name
  spec.version = nbgv_gemspec_version.call(__dir__)
  spec.authors = ["Shuai Zhang"]
  spec.email = ["zhangshuai.ustc@gmail.com"]

  spec.summary = "Offline latexmath rendering for Asciidoctor."
  spec.description = "Render latexmath blocks and inline macros to PDF/SVG/PNG assets using your local LaTeX toolchain."
  spec.homepage = "https://github.com/hcoona/three/blob/main/src/public/lib/asciidoctor-latexmath/README.md"
  spec.license = "LGPL-3.0-or-later WITH LGPL-3.0-linking-exception"

  spec.required_ruby_version = Gem::Requirement.new(">= 3.2")

  spec.metadata = {
    "homepage_uri" => spec.homepage,
    "source_code_uri" => "https://github.com/hcoona/three",
    "github_repo" => "https://github.com/hcoona/three",
    "bug_tracker_uri" => "https://github.com/hcoona/three/issues",
    "documentation_uri" => "https://github.com/hcoona/three/blob/main/src/public/lib/asciidoctor-latexmath/README.md"
  }

  spec.files = Dir["lib/**/*", "README.md", "LICENSE"]
  spec.require_paths = ["lib"]

  spec.add_runtime_dependency "asciidoctor", ">= 2.0", "< 3.0"

  spec.add_development_dependency "bundler", "~> 2.4"
  spec.add_development_dependency "rake", "~> 13.0"
  spec.add_development_dependency "rspec", "~> 3.13"
end

gemspec
