# frozen_string_literal: true
require_relative "spec_helper"

module NativeHelpers

  RSpec.describe "Native Ruby source" do
    BUDDY = "https://rubygems.pkg.github.com/hcoona"
    OFFICIAL = "https://rubygems.org"
    ORIGINAL = "actual controlled native download"
    USER = "read user:@/+?%&="
    TOKEN = "synthetic token:@/+?%&= punctuation"
    UNSAFE = "unsafe-native-exception-detail"

    def invoke(scenario, host: OFFICIAL, version: "1.2.3", defect: nil, linked: false)
      @state = @scratch / "native state"
      environment = closed_environment(@state, variables: %w[HOME USERPROFILE XDG_CONFIG_HOME XDG_CACHE_HOME XDG_DATA_HOME GEM_HOME GEM_PATH GEM_SPEC_CACHE TMPDIR TMP TEMP])
      write(Pathname.new(environment["HOME"]) / ".gemrc", "---\n:sources:\n- https://alternate.invalid\n")
      directory = @state / "download"
      if linked
        (@state / "download-target").mkpath
        File.symlink(@state / "download-target", directory)
      else
        directory.mkpath
      end
      request = {"host" => host, "name" => NAME, "version" => version, "directory" => directory.to_s}
      case defect
      when "host" then request["host"] = "https://unselected.invalid"
      when "name" then request["name"] = "another-package"
      when "version" then request["version"] = "invalid native version!"
      when "directory" then request["directory"] = (@state / "missing").to_s
      end
      path = write(@state / "request.json", JSON.generate(request))
      trace_path = @state / "calls.json"
      environment.merge!("TEST_SCENARIO" => scenario, "TEST_VERSION" => version, "TEST_TRACE" => trace_path.to_s)
      environment.merge!("RUBY_SOURCE_USER" => USER, "RUBY_SOURCE_TOKEN" => TOKEN) if host == BUDDY
      argv = [RUBY, "-r", CARRIER / "spec/support/public_source_answers.rb", HELPERS / "source.rb", path]
      [JSON.generate(request), *argv.map(&:to_s)].each do |value|
        expect(value).not_to include(USER, TOKEN)
      end
      result = run_process(argv, directory: @state, environment: environment)
      trace = JSON.parse(trace_path.read)
      expect(result.stderr).to eq("")
      expect(result.stdout.bytesize).to be <= 256 * 1024
      [result.stdout, result.stderr, JSON.generate(trace)].each do |value|
        expect(value).not_to include(USER, TOKEN, UNSAFE)
      end
      value = JSON.parse(result.stdout)
      validate_schema("ruby-source-request", request)
      validate_schema("ruby-source-answer", value)
      [result, value, trace, request]
    end

    def assert_queries(trace)
      expect(trace.first(2).map { |call| call["type"] }).to eq(%w[released prerelease])
      expect(trace.first(2).map { |call| call["method"] }).to eq(%w[load_specs load_specs])
      expect(trace.first(2).all? { |call| call["selected_only"] && call["credential_ok"] }).to be(true)
      expect(trace.map { |call| call["source_id"] }.uniq.size).to eq(1)
    end

    [OFFICIAL, BUDDY].product(%w[1.2.3 1.2.3.pre.beta.7]).each do |host, version|
      it "exact_native_tuple_uses_selected_public_source [#{host}, #{version}]" do
        result, value, trace, request = invoke("exact", host: host, version: version)
        expect(result.returncode).to eq(0)
        assert_queries(trace)
        expect(trace.map { |call| call["method"] }).to eq(%w[load_specs load_specs fetch_spec download])
        original = Pathname.new(value["path"])
        expect(original.dirname.to_s).to eq(request["directory"])
        expect(original.binread).to eq(ORIGINAL)
        expect(value).to eq({"state" => "present", "name" => NAME, "version" => version, "platform" => "ruby", "path" => original.to_s, "size" => ORIGINAL.bytesize, "sha256" => Digest::SHA256.hexdigest(ORIGINAL)})
        expect(trace[2].merge("source_id" => 0)).to eq({"method" => "fetch_spec", "name" => NAME, "version" => version, "platform" => "ruby", "source_id" => 0})
        expect(trace[3].merge("source_id" => 0)).to eq({"method" => "download", "name" => NAME, "version" => version, "platform" => "ruby", "directory" => request["directory"], "source_id" => 0})
        expect(Pathname.new(RUBY)).to be_absolute
        expect(original.dirname.glob("*.gemrc")).to be_empty
      end
    end

    %w[1.2.3 1.2.3.pre.beta.7].each do |version|
      it "absence_requires_both_successful_inventories [#{version}]" do
        result, value, trace, request = invoke("absent", version: version)
        expect(result.returncode).to eq(0)
        expect(value).to eq({"state" => "absent"})
        assert_queries(trace)
        expect(trace.size).to eq(2)
        expect(Pathname.new(request["directory"]).children).to be_empty
      end
    end

    failures = {
      "load_specs" => %w[fail-released fail-prerelease too-many duplicate],
      "fetch_spec" => %w[fail-spec spec-name spec-version spec-platform],
      "download" => %w[fail-download outside-original prefix-sibling missing-original directory-original invalid-original empty-original]
    }
    failures.each do |last_method, scenarios|
      scenarios.each do |scenario|
        it "native_failures_are_unknown_without_exception_diagnostics [#{scenario}]" do
          result, value, trace, = invoke(scenario, host: BUDDY, version: "1.2.3.pre.beta.7")
          expect(result.returncode).to eq(1)
          expect(value).to eq({"state" => "unknown"})
          expect(result.stdout).to eq("{\"state\":\"unknown\"}\n")
          expect(result.stderr).to eq("")
          expect(trace.last["method"]).to eq(last_method)
          if scenario == "fail-released"
            expect(trace.map { |call| call["type"] }).to eq(["released"])
          else
            assert_queries(trace)
          end
          if %w[fail-prerelease too-many duplicate spec-name spec-version spec-platform].include?(scenario)
            expect(trace.map { |call| call["method"] }).not_to include("download")
          end
        end
      end
    end

    it "linked_download_directory_keeps_the_requested_spelling" do
      result, value, trace, request = invoke("exact", linked: true)
      expect(result.returncode).to eq(0)
      assert_queries(trace)
      expect(value["state"]).to eq("present")
      expect(File.dirname(value["path"])).to eq(request["directory"])
      expect(Pathname.new(value["path"]).binread).to eq(ORIGINAL)
    end

    it "large_original_is_portable_present_without_an_application_size_cap" do
      result, value, trace, request = invoke("large-original")
      expect(result.returncode).to eq(0)
      assert_queries(trace)
      expect(value["state"]).to eq("present")
      expect(value["size"]).to eq(8 * 1024 * 1024 + 1)
      original = Pathname.new(value["path"])
      expect(original.dirname.to_s).to eq(request["directory"])
      expect(value["sha256"]).to eq(digest(original))
      expect(original.size).to eq(value["size"])
    end

    it "punctuated_buddy_credentials_stay_in_memory" do
      result, value, trace, = invoke("exact", host: BUDDY)
      expect(result.returncode).to eq(0)
      expect(value["state"]).to eq("present")
      assert_queries(trace)
      @scratch.glob("**/*", File::FNM_DOTMATCH).select(&:file?).each do |path|
        expect(path.read).not_to include(USER, TOKEN)
      end
      expect(@state.glob("**/.gemrc").size).to eq(1)
      expect((@state / "home/.gemrc").read).to eq("---\n:sources:\n- https://alternate.invalid\n")
    end

    %w[host name version directory].each do |defect|
      it "invalid_request_is_unknown_without_native_calls [#{defect}]" do
        result, value, trace, = invoke("exact", defect: defect)
        expect(result.returncode).to eq(1)
        expect(value).to eq({"state" => "unknown"})
        expect(trace).to eq([])
        expect(result.stderr).to eq("")
      end
    end
  end
end
