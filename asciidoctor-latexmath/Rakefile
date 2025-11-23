# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require "digest"
require "fileutils"
require "rake/clean"
require "rubygems/specification"
require "tmpdir"

RELEASE_GEMSPEC_PATH = File.expand_path("asciidoctor-latexmath.gemspec", __dir__)
RELEASE_PKG_DIR = File.expand_path("pkg", __dir__)

begin
  require "standard/rake"
rescue LoadError
  warn "StandardRB not available; install dependencies with bundle install" if $PROGRAM_NAME == __FILE__
end

CLEAN.include("sample.html", "generated_images")

if File.exist?("asciidoctor-latexmath.gemspec")
  begin
    require "bundler/gem_tasks"
  rescue LoadError
    warn <<~MSG
      Bundler is required to build gem packages.
      Install it with: gem install bundler
    MSG
  end
else
  warn "Skipping Bundler gem tasks because asciidoctor-latexmath.gemspec is missing."
end

def bundler_available?
  return true if ENV["BUNDLE_GEMFILE"]

  @bundler_available ||= begin
    system("bundle", "exec", "ruby", "-v", out: File::NULL, err: File::NULL)
  rescue Errno::ENOENT
    false
  end
end

SAMPLE_RENDER_CMD = if bundler_available?
  "bundle exec asciidoctor -r ./lib/asciidoctor-latexmath.rb -a pdflatex=xelatex sample.adoc"
else
  "asciidoctor -r ./lib/asciidoctor-latexmath.rb -a pdflatex=xelatex sample.adoc"
end

desc "Render sample.adoc with the local extension"
task :sample do
  sh SAMPLE_RENDER_CMD
end

desc "Run StandardRB lint"
task :lint do
  sh "bundle exec standardrb"
end

task default: :sample

namespace :release do
  def self.load_spec
    @load_spec ||= begin
      spec = Gem::Specification.load(RELEASE_GEMSPEC_PATH)
      raise "Unable to load gemspec at #{RELEASE_GEMSPEC_PATH}" unless spec
      spec
    end
  end

  def self.source_date_epoch
    from_env = ENV["SOURCE_DATE_EPOCH"]&.strip
    return from_env unless from_env.nil? || from_env.empty?

    epoch = begin
      `git log -1 --format=%ct`.strip
    rescue
      ""
    end

    raise "Unable to derive SOURCE_DATE_EPOCH; set it explicitly." if epoch.empty?

    epoch
  end

  def self.build_gem(output_path, epoch)
    env = {"SOURCE_DATE_EPOCH" => epoch}
    sh env, Gem.ruby, "-S", "gem", "build", RELEASE_GEMSPEC_PATH, "--output", output_path
  end

  desc "Build the gem twice and assert identical SHA256 digests"
  task :verify do
    spec = load_spec
    epoch = source_date_epoch

    Dir.mktmpdir("latexmath-gem-build-") do |tmp|
      first = File.join(tmp, spec.file_name)
      second = File.join(tmp, "second-#{spec.file_name}")

      build_gem(first, epoch)
      build_gem(second, epoch)

      digest_one = Digest::SHA256.file(first).hexdigest
      digest_two = Digest::SHA256.file(second).hexdigest

      unless digest_one == digest_two
        raise <<~MSG
          Gem build is not reproducible.
          first:  #{digest_one}
          second: #{digest_two}
        MSG
      end

      FileUtils.mkdir_p(RELEASE_PKG_DIR)
      final_path = File.join(RELEASE_PKG_DIR, spec.file_name)
      FileUtils.mv(first, final_path, force: true)

      puts "Reproducible gem build verified (SHA256 #{digest_one}). Artifact copied to #{final_path}."
    end
  end
end
