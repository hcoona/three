# frozen_string_literal: true
require_relative "spec_helper"

module NativeHelpers

  RSpec.describe "Native Ruby consumer" do
    UNSAFE_CONSUMER = "UNSAFE-native-exception-URI-and-token"

    before do
      @gems = @scratch / "installed gems"
      @gems.mkpath
      @calls = []
    end

    def consumer_command(arguments, cwd: nil)
      state = @scratch / format("command-%02d", @calls.length)
      environment = closed_environment(state).merge("GEM_HOME" => @gems.to_s, "GEM_PATH" => @gems.to_s)
      receipt = {"environment" => environment, "cwd" => (cwd || state).to_s, "stdin" => "closed EOF", "timeout_seconds" => 30}
      result = run_process([RUBY, *arguments], directory: cwd || state, environment: environment)
      receipt.merge!(@receipts.last)
      write(state / "receipt.json", JSON.pretty_generate(receipt))
      @calls << receipt
      result
    end

    def build(version = "1.2.3")
      source = @scratch / "source product"
      [*FILES.first(3), "#{NAME}.gemspec"].each do |relative|
        target = source / relative
        target.dirname.mkpath
        FileUtils.cp(PRODUCT / relative, target)
      end
      write(source / FILES[3], "module HcoonaReleaseSmokeRuby\n  VERSION = #{version.to_json}\nend\n")
      original = @scratch / "original.gem"
      result = consumer_command(["-S", "gem", "build", "--norc", "#{NAME}.gemspec", "--output", original], cwd: source)
      expect(result.returncode).to eq(0), result.stderr
      expect(original).to be_file
      expect(original.size).to be > 0
      request = {"original" => original.to_s, "version" => version, "sha256" => digest(original), "install_dir" => @gems.to_s}
      FileUtils.cp(original, @scratch / "built-original.gem")
      write(@scratch / "built-original.json", JSON.pretty_generate(request.merge("size" => original.size)))
      request
    end

    def consumer_helper(operation, request)
      message = request.merge("operation" => operation)
      validate_schema("ruby-consumer-request", message)
      path = write(@scratch / format("request-%02d.json", @calls.length), JSON.generate(message))
      consumer_command([HELPERS / "consumer.rb", path])
    end

    def success(result, version)
      expect(result.returncode).to eq(0), result.stderr
      expect(result.stderr).to eq("")
      value = JSON.parse(result.stdout)
      validate_schema("ruby-consumer-answer", value)
      expect(value).to eq({"name" => NAME, "version" => version, "passed" => true})
    end

    def failure(result)
      expect(result.returncode).to eq(1)
      expect(result.stdout).to eq("{\"passed\":false}\n")
      expect(result.stderr).to eq("")
      expect(result.stdout + result.stderr).not_to include(UNSAFE_CONSUMER)
    end

    %w[1.2.3 1.2.3.pre.beta.4].each do |version|
      it "native_original_install_and_separate_clean_api [#{version}]" do
        request = build(version)
        original = Pathname.new(request["original"])
        before = original.binread
        success(consumer_helper("install", request), version)
        success(consumer_helper("consume", request), version)
        cache = @gems / "cache/#{NAME}-#{version}.gem"
        expect(cache.binread).to eq(before)
        expect(digest(cache)).to eq(request["sha256"])
        expect(original.binread).to eq(before)
        probe = consumer_command(["-rjson", "-rdigest", "-e", <<~RUBY])
          abort "stdin was not EOF" unless STDIN.read.empty?
          require "hcoona_release_smoke_ruby"
          spec = Gem.loaded_specs.fetch(#{NAME.to_json})
          puts JSON.generate({name: spec.name, version: spec.version.to_s,
            platform: spec.platform.to_s, origin: File.realpath(spec.full_gem_path),
            features: $LOADED_FEATURES.select { |p|
              p.end_with?("/hcoona_release_smoke_ruby.rb") ||
                p.end_with?("/hcoona_release_smoke_ruby/version.rb")
            }.map { |p| File.realpath(p) },
            id: HcoonaReleaseSmokeRuby.project_id,
            api_version: HcoonaReleaseSmokeRuby::VERSION,
            cache: Digest::SHA256.file(spec.cache_file).hexdigest})
        RUBY
        expect(probe.returncode).to eq(0), probe.stderr
        value = JSON.parse(probe.stdout)
        expect(value.values_at("name", "id")).to eq([NAME, NAME])
        expect(value.values_at("version", "api_version")).to eq([version, version])
        expect(value["platform"]).to eq("ruby")
        origin = Pathname.new(value["origin"])
        expect(origin.to_s).to start_with(@gems.to_s + File::SEPARATOR)
        expect(value["features"]).to contain_exactly(*FILES.last(2).map { |path| (origin / path).to_s })
        expect(value["cache"]).to eq(request["sha256"])
        child_calls = @calls.drop(1)
        expect(child_calls.map { |call| call["environment"]["HOME"] }.uniq.size).to eq(3)
        expect(child_calls.map { |call| call["environment"]["TMPDIR"] }.uniq.size).to eq(3)
        child_calls.each do |call|
          expect(call["cwd"]).not_to start_with(ROOT.to_s + File::SEPARATOR)
          expect(call["stdin"]).to eq("closed EOF")
          expect(call["timeout_seconds"]).to eq(30)
          expect(call["environment"].values_at("GEM_HOME", "GEM_PATH")).to eq([@gems.to_s, @gems.to_s])
          expect(call["environment"].keys).not_to include(*%w[RUBY_SOURCE_USER RUBY_SOURCE_TOKEN GEM_HOST_API_KEY RUBYOPT RUBYLIB BUNDLE_GEMFILE HTTPS_PROXY])
        end
      end
    end

    %w[bytes reported-digest].each do |change|
      it "changed_digest_prevents_install [#{change}]" do
        request = build
        original = Pathname.new(request["original"])
        before = original.binread
        if change == "bytes"
          changed = before.dup
          changed.setbyte(0, before.getbyte(0) ^ 1)
          original.binwrite(changed)
          expect(original.size).to eq(before.bytesize)
        else
          request["sha256"] = "0" * 64
        end
        failure(consumer_helper("install", request))
        expect(@gems.children).to be_empty
        expect(digest(original)).not_to eq(request["sha256"])
      end
    end

    %w[api-id api-version native-version cache origin import-origin missing-version-file].each do |defect|
      it "wrong_installed_facts_fail_cleanly [#{defect}]" do
        request = build
        success(consumer_helper("install", request), request["version"])
        origin = @gems / "gems/#{NAME}-#{request['version']}"
        main = origin / FILES[2]
        version = origin / FILES[3]
        case defect
        when "api-id" then main.write(main.read.gsub(NAME, "another-product"))
        when "api-version", "native-version"
          version.write("module HcoonaReleaseSmokeRuby; VERSION = \"9.8.7\"; end\n")
          request["version"] = "9.8.7" if defect == "native-version"
        when "cache" then (@gems / "cache/#{NAME}-1.2.3.gem").binwrite("changed")
        when "origin"
          other = @scratch / "unrelated install root"
          other.mkpath
          request["install_dir"] = other.to_s
        when "import-origin"
          outside = @scratch / "outside-version.rb"
          FileUtils.cp(version, outside)
          version.delete
          File.symlink(outside, version)
        when "missing-version-file" then version.delete
        end
        failure(consumer_helper("consume", request))
        expect(digest(request["original"])).to eq(request["sha256"])
      end
    end

    it "wrong_install_identity_retains_failed_effect" do
      request = build
      request["version"] = "9.8.7"
      failure(consumer_helper("install", request))
      expect(@gems / "specifications/#{NAME}-1.2.3.gemspec").to exist
      expect(digest(@gems / "cache/#{NAME}-1.2.3.gem")).to eq(request["sha256"])
    end

    it "existing_install_is_not_force_replaced" do
      request = build
      success(consumer_helper("install", request), request["version"])
      cache = @gems / "cache/#{NAME}-1.2.3.gem"
      before = cache.binread
      failure(consumer_helper("install", request))
      expect(cache.binread).to eq(before)
    end

    %w[malformed missing-argument missing-file missing-version missing-root missing-operation unsupported missing-original corrupt-original].each do |defect|
      it "invalid_request_emits_only_fixed_failure [#{defect}]" do
        request = {"operation" => "install", "version" => "1.2.3", "install_dir" => @gems.to_s, "sha256" => "0" * 64, "original" => (@scratch / UNSAFE_CONSUMER).to_s}
        case defect
        when "corrupt-original"
          original = write(Pathname.new(request["original"]), "not a native gem")
          request["sha256"] = digest(original)
        when "unsupported" then request["operation"] = UNSAFE_CONSUMER
        when "missing-version", "missing-root", "missing-operation"
          field = {"missing-version" => "version", "missing-root" => "install_dir", "missing-operation" => "operation"}.fetch(defect)
          request.delete(field)
        end
        path = @scratch / "invalid-request.json"
        write(path, defect == "malformed" ? "{" : JSON.generate(request)) unless defect == "missing-file"
        arguments = defect == "missing-argument" ? [HELPERS / "consumer.rb"] : [HELPERS / "consumer.rb", path]
        failure(consumer_command(arguments))
        expect(@gems / "specifications/#{NAME}-1.2.3.gemspec").not_to exist
      end
    end

    it "native_exception_is_omitted" do
      request = build
      success(consumer_helper("install", request), request["version"])
      write(@gems / "gems/#{NAME}-1.2.3/#{FILES[2]}", "raise #{UNSAFE_CONSUMER.to_json}\n")
      failure(consumer_helper("consume", request))
    end
  end
end
