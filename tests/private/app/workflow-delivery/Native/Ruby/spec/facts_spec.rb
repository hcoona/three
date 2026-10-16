# frozen_string_literal: true
require_relative "spec_helper"

module NativeHelpers

  RSpec.describe "Native Ruby facts" do
    # These operations never load Bundler, so one locked context covers them.
    context "Bundler-independent operations" do
      let(:native) { facts_native(NativeHelpers::CONTEXTS.first) }

      it "source_metadata_is_native_and_not_a_test_inventory" do
        source = gem_fixture(@scratch / "source gem", "fixture_a", ["fixture_b"])
        write(source.dirname / "spec/required_spec.rb", "raise 'not package data'\n")
        result = answer(facts(native, "gemspec", source))
        expect(result.values_at("name", "version", "gemspec")).to eq(["fixture_a", "1.2.3", source.to_s])
        expect(result["runtime_dependencies"]).to eq([{"name" => "fixture_b", "requirement" => "= 1.2.3"}])
        expect(result["files"]).to eq(["lib/fixture_a.rb"])
        expect(result["require_paths"]).to eq(["lib"])
      end

      [["1.2.3", "1.2.3"], ["0.1.0-beta.12+gabcdef0", "0.1.0.pre.beta.12"]].each do |raw, expected|
        it "native_version_projection_preserves_input_and_ruby_literal [#{raw}]" do
          source = gem_fixture(@scratch / "source", "fixture_a")
          before = source.binread
          result = answer(facts(native, "version", source, raw: raw))
          expect(result).to eq({"gemspec" => source.to_s, "raw" => raw, "version" => expected, "literal" => expected.to_json})
          literal = command(native, source.dirname, ["-e", "puts eval(ARGV.fetch(0))", result["literal"]])
          expect(literal.returncode).to eq(0), literal.stderr
          expect(literal.stdout.strip).to eq(expected)
          expect(source.binread).to eq(before)
        end
      end

      [nil, "", " 1.2.3", "1.2.3\n", "not-a-version", "+gabcdef0"].each do |raw|
        it "invalid_native_version_cannot_be_projected [#{raw.inspect}]" do
          source = gem_fixture(@scratch / "source", "fixture_a")
          result = facts(native, "version", source, raw: raw)
          expect(result.returncode).not_to eq(0)
          expect(result.stdout).to eq("")
        end
      end

      it "failed_gemspec_and_corrupt_archive_fail_collection" do
        source = write(@scratch / "invalid.gemspec", "raise 'required metadata unavailable'\n")
        archive = write(@scratch / "corrupt.gem", "not a native gem archive")
        [["gemspec", source], ["archive", archive]].each do |operation, path|
          result = facts(native, operation, path)
          expect(result.returncode).not_to eq(0)
          expect(result.stdout).to eq("")
        end
      end

      it "archive_metadata_needs_no_bundle_or_specs" do
        source = gem_fixture(@scratch / "source", "fixture_a")
        built = command(native, source.dirname, ["-S", "gem", "build", source])
        expect(built.returncode).to eq(0), built.stderr
        archive = source.dirname / "fixture_a-1.2.3.gem"
        result = answer(facts(native, "archive", archive))
        expect(result.values_at("name", "version", "contents")).to eq(["fixture_a", "1.2.3", ["lib/fixture_a.rb"]])
      end
    end

    NativeHelpers::CONTEXTS.each do |context|
      context "Bundler #{context.version}" do
        let(:native) { facts_native(context) }

        it "lock_context_does_not_evaluate_or_qualify_the_gemfile" do
          gem_fixture(@scratch / "source", "fixture_a")
          gemfile = write(@scratch / "Gemfile", "gem \"fixture_a\", path: \"source\"\n")
          environment = lock(native, gemfile)
          locked = @scratch / "Gemfile.lock"
          before = locked.binread
          gemfile.write("raise \"not a lock-context operation\"\n")
          expect(answer(facts(native, "bundle-context", gemfile, environment: environment))).to eq({"gemfile" => gemfile.to_s, "lockfile" => locked.to_s, "bundler_version" => context.version, "locked_bundler_version" => context.version})
          expect(locked.binread).to eq(before)
          expect(facts(native, "bundle", gemfile, environment: environment).returncode).not_to eq(0)
        end

        it "lock_context_keeps_probe_and_locked_versions_separate" do
          gem_fixture(@scratch / "source", "fixture_a")
          gemfile = write(@scratch / "Gemfile", "gem \"fixture_a\", path: \"source\"\n")
          environment = lock(native, gemfile)
          other = CONTEXTS.find { |candidate| candidate.version != context.version }
          probe = [other.version, native[1].merge(context_environment(other))]
          result = answer(facts(probe, "bundle-context", gemfile, environment: environment))
          expect(result["bundler_version"]).to eq(other.version)
          expect(result["locked_bundler_version"]).to eq(context.version)
          expect(result["gemfile"]).to eq(gemfile.to_s)
        end

        it "lock_context_requires_a_real_lock" do
          gemfile = write(@scratch / "Gemfile", "source \"https://rubygems.org\"\n")
          result = facts(native, "bundle-context", gemfile, environment: {"BUNDLE_GEMFILE" => gemfile.to_s})
          expect(result.returncode).not_to eq(0)
          expect(result.stdout).to eq("")
          expect(@scratch / "Gemfile.lock").not_to exist
        end

        it "nested_bundle_retains_transitive_original_path_identity" do
          root = @scratch / "source tree with spaces"
          paths = {"fixture_a" => ["fixture_b"], "fixture_b" => ["fixture_c"], "fixture_c" => []}.to_h do |name, dependencies|
            [name, gem_fixture(root / name, name, dependencies)]
          end
          gemfile = write(root / "Gemfile", "eval_gemfile \"dependencies.rb\"\n")
          nested = write(root / "dependencies.rb", paths.keys.map { |name| "gem #{name.to_json}, path: #{name.to_json}\n" }.join)
          environment = lock(native, gemfile)
          locked = root / "Gemfile.lock"
          before = locked.binread
          result = answer(facts(native, "bundle", gemfile, environment: environment))
          expect(result.values_at("bundler_version", "locked_bundler_version")).to eq([context.version, context.version])
          expect(result["gemfiles"]).to contain_exactly(gemfile.to_s, nested.to_s)
          selected = result["specifications"].to_h { |spec| [spec["name"], spec] }
          paths.each do |name, source|
            expect(selected[name]["source"]).to include("kind" => "path", "path" => source.dirname.to_s, "gemspec" => source.to_s)
          end
          expect(selected["fixture_a"]["runtime_dependencies"]).to eq([{"name" => "fixture_b", "requirement" => "= 1.2.3"}])
          expect(selected["fixture_b"]["runtime_dependencies"]).to eq([{"name" => "fixture_c", "requirement" => "= 1.2.3"}])
          expect(selected["bundler"]["source"]).to eq({"kind" => "metadata"})
          expect(locked.binread).to eq(before)
        end

        it "native_group_selection_excludes_without_groups" do
          gem_fixture(@scratch / "required", "fixture_a")
          gem_fixture(@scratch / "optional", "fixture_optional")
          gemfile = write(@scratch / "Gemfile", "gem \"fixture_a\", path: \"required\"\ngroup :optional do\n  gem \"fixture_optional\", path: \"optional\"\nend\n")
          environment = lock(native, gemfile).merge("BUNDLE_WITHOUT" => "optional")
          result = answer(facts(native, "bundle", gemfile, environment: environment))
          expect(result["direct_dependencies"]).to eq(["fixture_a"])
          expect(result["specifications"].map { |spec| spec["name"] }).not_to include("fixture_optional")
        end

        it "active_local_git_override_uses_original_checkout_identity" do
          original = gem_fixture(@scratch / "original git source", "fixture_a")
          git = lambda do |directory, *arguments|
            result = run_process(["git", *arguments], directory: directory, environment: native[1])
            expect(result.returncode).to eq(0), result.stderr
          end
          git.call(original.dirname, "init", "--initial-branch=main")
          git.call(original.dirname, "add", ".")
          git.call(original.dirname, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "-m", "Initialize native local fixture")
          checkout = @scratch / "active local checkout"
          git.call(@scratch, "clone", original.dirname.to_s, checkout.to_s)
          gemfile = write(@scratch / "Gemfile", "gem \"fixture_a\", git: #{original.dirname.to_s.to_json}, branch: \"main\"\n")
          environment = lock(native, gemfile, environment: {"BUNDLE_LOCAL__FIXTURE_A" => checkout.to_s})
          locked = @scratch / "Gemfile.lock"
          before = locked.binread
          selected = answer(facts(native, "bundle", gemfile, environment: environment))["specifications"].to_h { |spec| [spec["name"], spec] }
          expect(selected["fixture_a"]["source"]).to eq({"kind" => "local_git", "path" => checkout.to_s, "gemspec" => (checkout / "fixture_a.gemspec").to_s, "directory" => checkout.to_s})
          expect(locked.binread).to eq(before)
        end

        # Helper-owned frozen, locked-version and explicit lock-equivalence checks. The
        # helper-requested Gemfile/lock equivalence is version-independent, so one context
        # covers [changed].
        defects = %w[unfrozen wrong_lock]
        defects << "changed" if context.equal?(NativeHelpers::CONTEXTS.first)
        defects.each do |defect|
          it "invalid_bundle_context_cannot_return_facts [#{defect}]" do
            gem_fixture(@scratch / "source", "fixture_a")
            gemfile = write(@scratch / "Gemfile", "gem \"fixture_a\", path: \"source\"\n")
            environment = lock(native, gemfile)
            locked = @scratch / "Gemfile.lock"
            case defect
            when "unfrozen" then environment["BUNDLE_FROZEN"] = "0"
            when "changed" then gemfile.write("gem \"fixture_a\", \"= 9.9.9\", path: \"source\"\n")
            when "wrong_lock"
              other = CONTEXTS.find { |candidate| candidate.version != context.version }
              locked.write(locked.read.gsub(context.version, other.version))
            end
            before = locked.binread
            result = facts(native, "bundle", gemfile, environment: environment)
            expect(result.returncode).not_to eq(0)
            expect(result.stdout).to eq("")
            expect(locked.binread).to eq(before)
          end
        end
      end
    end
  end
end
