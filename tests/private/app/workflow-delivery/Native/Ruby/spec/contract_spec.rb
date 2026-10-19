# frozen_string_literal: true
require_relative "spec_helper"
require "rubygems/package"

module NativeHelpers
  # Contract examples for the native facts the C# application consumes from the locked Ruby,
  # RubyGems and Bundler: the exact specification field shape and the runtime identity. A
  # Ruby, RubyGems or Bundler pin change that alters one of these facts fails here.
  RSpec.describe "Native Ruby pin contracts" do
    let(:native) { facts_native(NativeHelpers::CONTEXTS.first) }

    def exited(result)
      expect(@receipts.last.fetch("termination")).to eq("exited")
      result
    end

    def full_fixture(directory)
      write(directory / "lib/fixture_full.rb", "VALUE = :native_fixture\n")
      write(directory / "lib/fixture_full/extra.rb", "EXTRA = true\n")
      write(directory / "ext/fixture_full/extconf.rb", "raise 'extensions are never built here'\n")
      write(directory / "fixture_full.gemspec", <<~RUBY)
        Gem::Specification.new do |spec|
          spec.name = "fixture_full"
          spec.version = "2.0.0.pre.1"
          spec.summary = "Native field-shape fixture"
          spec.authors = ["Fixture"]
          spec.license = "MIT"
          spec.platform = Gem::Platform::RUBY
          spec.require_paths = ["lib", "ext"]
          spec.files = ["ext/fixture_full/extconf.rb", "lib/fixture_full.rb", "lib/fixture_full/extra.rb"]
          spec.extensions = ["ext/fixture_full/extconf.rb"]
          spec.metadata = { "github_repo" => "https://example.invalid/repo", "allowed_push_host" => "https://example.invalid" }
          spec.add_runtime_dependency "fixture_b", ">= 1.0", "< 3"
          spec.add_development_dependency "fixture_dev", "= 9.9.9"
        end
      RUBY
    end

    FULL = {
      "name" => "fixture_full", "version" => "2.0.0.pre.1", "platform" => "ruby",
      "require_paths" => ["lib", "ext"],
      "files" => ["ext/fixture_full/extconf.rb", "lib/fixture_full.rb", "lib/fixture_full/extra.rb"],
      "extensions" => ["ext/fixture_full/extconf.rb"],
      "metadata" => {"github_repo" => "https://example.invalid/repo", "allowed_push_host" => "https://example.invalid"},
      "runtime_dependencies" => [{"name" => "fixture_b", "requirement" => ">= 1.0, < 3"}]
    }.freeze
    # RubyGems prefixes a gem with extensions with its platform extension directory, which the
    # smoke recipe's exact ["lib"] check rejects.
    EXTENSION_PATH = %r{/extensions/[^/]+/[^/]+/fixture_full-2\.0\.0\.pre\.1\z}
    MINIMAL = {
      "name" => "fixture_a", "version" => "1.2.3", "platform" => "ruby", "require_paths" => ["lib"],
      "files" => ["lib/fixture_a.rb"], "extensions" => [], "metadata" => {}, "runtime_dependencies" => []
    }.freeze

    # Every consumed field, with its exact native type: development dependencies are not runtime
    # edges, the requirement is RubyGems' own normalized string, and an extension is reported
    # (the application rejects it) without being built.
    it "gemspec_facts_have_the_exact_native_field_shape" do
      source = full_fixture(@scratch / "full source")
      result = answer(exited(facts(native, "gemspec", source)))
      expect(result["require_paths"].first).to match(EXTENSION_PATH)
      expect(result.merge("require_paths" => result["require_paths"].drop(1))).to eq(FULL.merge("gemspec" => source.to_s))
      expect(result.keys).to eq(FULL.keys + ["gemspec"])
    end

    # An undeclared extension, metadata and dependency set is an empty value, not an absent field.
    it "gemspec_facts_report_empty_declarations_as_empty_values" do
      source = gem_fixture(@scratch / "minimal source", "fixture_a")
      result = answer(exited(facts(native, "gemspec", source)))
      expect(result).to eq(MINIMAL.merge("gemspec" => source.to_s))
    end

    # The built archive answers the same fields plus its verified contents; an archive whose
    # data no longer matches its recorded checksum fails verification and returns no facts.
    it "archive_facts_match_the_gemspec_and_reject_a_truncated_archive" do
      source = full_fixture(@scratch / "full source")
      built = exited(command(native, source.dirname, ["-S", "gem", "build", source]))
      expect(built.returncode).to eq(0), built.stderr
      archive = source.dirname / "fixture_full-2.0.0.pre.1.gem"
      result = answer(exited(facts(native, "archive", archive)))
      expect(result["require_paths"].first).to match(EXTENSION_PATH)
      expect(result.merge("require_paths" => result["require_paths"].drop(1))).to eq(FULL.merge("archive" => archive.to_s, "contents" => FULL.fetch("files")))
      tampered = @scratch / "tampered.gem"
      entries = []
      archive.open("rb") { |io| Gem::Package::TarReader.new(io) { |tar| tar.each { |entry| entries << [entry.full_name, entry.read] } } }
      expect(entries.map(&:first)).to include("data.tar.gz", "checksums.yaml.gz")
      tampered.open("wb") do |io|
        Gem::Package::TarWriter.new(io) do |tar|
          entries.each do |name, data|
            # A wrong recorded digest keeps both payloads readable, so only verification fails.
            data = Gem::Util.gzip(Gem::Util.gunzip(data).gsub(/[0-9a-f]{64}/) { |digest| digest.tr("0-9a-f", "1-9a-f0") }) if name == "checksums.yaml.gz"
            tar.add_file_simple(name, 0o444, data.bytesize) { |stream| stream.write(data) }
          end
        end
      end
      failed = exited(facts(native, "archive", tampered))
      expect(failed.returncode).not_to eq(0)
      expect(failed.stdout).to eq("")
    end

    # The runtime that executes the helpers is the committed locked Ruby, and it reports the
    # RubyGems version Release preparation records.
    it "runtime_identity_is_the_locked_ruby" do
      locked = (ROOT / "mise.lock").read.scan(/^\[\[tools\.ruby\]\]\nversion = "([^"]+)"/).flatten.uniq
      requested = (ROOT / "mise.toml").read[/^ruby\s*=\s*"([^"]+)"\s*$/, 1]
      expect(locked.size).to eq(1)
      expect(requested).to eq(locked.first)
      result = exited(command(native, @scratch, ["-rjson", "-e", "puts JSON.generate({ruby: RUBY_VERSION, rubygems: Gem::VERSION})"]))
      expect(result.returncode).to eq(0), result.stderr
      identity = JSON.parse(result.stdout)
      expect(identity.fetch("ruby")).to eq(locked.first)
      expect(Gem::Version.correct?(identity.fetch("rubygems"))).to be(true)
    end

    # Bundler appears in several committed locks; every one must name a Bundler the helper's
    # lock interface supports, and the carrier's contexts must cover each of them, so a
    # Bundler bump in one lock cannot leave the helper or the probe behind.
    it "every_committed_bundler_is_a_supported_and_tested_context" do
      supported = (HELPERS / "facts.rb").read.scan(/when "(\d+\.\d+\.\d+)" then resolved\.for/).flatten
      locks = Dir.glob("**/Gemfile.lock", base: ROOT.to_s).reject { |path| path.split("/").intersect?(%w[node_modules vendor .git]) }
      expect(locks).not_to be_empty
      versions = locks.to_h { |lock| [lock, Bundler::LockfileParser.new((ROOT / lock).read).bundler_version.to_s] }
      expect(versions.values.uniq - supported).to eq([]), versions.inspect
      expect(CONTEXTS.map(&:version).sort).to eq(supported.sort)
    end
  end
end
