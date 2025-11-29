# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

lib = File.expand_path("lib", __dir__)
$LOAD_PATH.unshift(lib) unless $LOAD_PATH.include?(lib)
require "asciidoctor/latexmath/version"

gem_name = "asciidoctor-latexmath"

gemspec = Gem::Specification.new do |spec|
  spec.name = gem_name
  spec.version = Asciidoctor::Latexmath::VERSION
  spec.authors = ["Shuai Zhang"]
  spec.email = ["zhangshuai.ustc@gmail.com"]

  spec.summary = "Offline latexmath rendering for Asciidoctor."
  spec.description = "Render latexmath blocks and inline macros to PDF/SVG/PNG assets using your local LaTeX toolchain."
  spec.homepage = "https://github.com/hcoona/asciidoctor-latexmath#readme"
  spec.license = "LGPL-3.0-or-later WITH LGPL-3.0-linking-exception"

  spec.required_ruby_version = Gem::Requirement.new(">= 3.2")

  spec.metadata = {
    "homepage_uri" => spec.homepage,
    "source_code_uri" => "https://github.com/hcoona/asciidoctor-latexmath",
    "bug_tracker_uri" => "https://github.com/hcoona/asciidoctor-latexmath/issues",
    "documentation_uri" => "https://github.com/hcoona/asciidoctor-latexmath/blob/main/README.md"
  }

  spec.files = Dir["lib/**/*", "README.md", "LICENSE"]
  spec.require_paths = ["lib"]

  spec.add_runtime_dependency "asciidoctor", ">= 2.0", "< 3.0"

  spec.add_development_dependency "bundler", "~> 2.4"
  spec.add_development_dependency "rake", "~> 13.0"
  spec.add_development_dependency "rspec", "~> 3.13"
end

gemspec
