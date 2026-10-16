# frozen_string_literal: true

require "bundler"
require "digest"
require "fileutils"
require "json"
require "json_schemer"
require "open3"
require "pathname"
require "rbconfig"
require "time"
require "timeout"
require "tmpdir"

module NativeHelpers
  CARRIER = Pathname.new(__dir__).parent
  ROOT = CARRIER.ascend.find { |path| (path / "mise.toml").file? }
  HELPERS = ROOT / "src/private/app/workflow-delivery/Native/Ruby"
  CONTRACTS = ROOT / "src/private/app/workflow-delivery/Native/contracts"
  NAME = "hcoona-release-smoke-ruby"
  RUBY = File.realpath(RbConfig.ruby)
  Context = Struct.new(:gemfile, :version, :environment, keyword_init: true)
  Result = Struct.new(:stdout, :stderr, :returncode, keyword_init: true)

  # Native lock facts are the sole version source. Both finite contexts are
  # conventionally discoverable/preparable by repository Bundler inventory.
  GEMFILES = [CARRIER / "Gemfile", CARRIER / "contexts/bundler-2.4/Gemfile"].freeze
  CONTEXTS = GEMFILES.map do |gemfile|
    lock = Bundler::LockfileParser.new(File.read("#{gemfile}.lock"))
    Context.new(gemfile: gemfile, version: lock.bundler_version.to_s)
  end.freeze

  def write(path, content)
    FileUtils.mkdir_p(path.dirname)
    path.binwrite(content)
    path
  end

  def validate_schema(name, value)
    description = JSON.parse((CONTRACTS / "#{name}.schema.json").read)
    expect(JSONSchemer.validate_schema(description).to_a).to eq([])
    schema = JSONSchemer.schema(description)
    expect(schema.validate(value).to_a).to eq([]), "Invalid #{name}: #{schema.validate(value).to_a}"
  end

  def digest(path)
    Digest::SHA256.file(path).hexdigest
  end

  def closed_environment(state, variables: %w[HOME USERPROFILE XDG_CONFIG_HOME XDG_CACHE_HOME XDG_DATA_HOME APPDATA LOCALAPPDATA GEM_SPEC_CACHE TMPDIR TMP TEMP])
    environment = {"PATH" => [File.dirname(RUBY), "/usr/bin", "/bin"].join(File::PATH_SEPARATOR), "LANG" => "C.UTF-8", "LC_ALL" => "C.UTF-8"}
    variables.each do |variable|
      directory = state / variable.downcase
      FileUtils.mkdir_p(directory)
      environment[variable] = directory.to_s
    end
    environment
  end

  def run_process(argv, directory:, environment:, timeout: 30)
    state = @scratch / format("process-%03d", @receipts.length)
    FileUtils.mkdir_p(state)
    started = Process.clock_gettime(Process::CLOCK_MONOTONIC)
    receipt = {"argv" => argv.map(&:to_s), "cwd" => directory.to_s,
      "stdin" => "closed EOF", "timeout_seconds" => timeout, "started_utc" => Time.now.utc.iso8601}
    stdout = stderr = ""
    begin
      Open3.popen3(environment, *argv.map(&:to_s), chdir: directory.to_s, unsetenv_others: true, pgroup: true) do |input, output, error, child|
        input.close
        readers = [Thread.new { output.read }, Thread.new { error.read }]
        begin
          status = Timeout.timeout(timeout) do
            outcome = child.value
            stdout, stderr = readers.map(&:value)
            outcome
          end
          receipt["exit_code"] = status.exitstatus || -status.termsig
          receipt["termination"] = status.signaled? ? "signaled" : "exited"
        rescue Timeout::Error
          receipt["exit_code"] = nil
          receipt["termination"] = "timed-out"
          begin
            Process.kill("KILL", -child.pid)
          rescue Errno::ESRCH
            # The owned process group already terminated at the deadline.
          end
          child.value
          stdout, stderr = readers.map(&:value)
          raise
        end
      end
      Result.new(stdout: stdout, stderr: stderr, returncode: receipt.fetch("exit_code"))
    ensure
      receipt["duration_seconds"] = Process.clock_gettime(Process::CLOCK_MONOTONIC) - started
      receipt["finished_utc"] = Time.now.utc.iso8601
      state.join("stdout.txt").binwrite(stdout)
      state.join("stderr.txt").binwrite(stderr)
      state.join("receipt.json").write(JSON.pretty_generate(receipt))
      @receipts << receipt
    end
  end

  def context_environment(context)
    return context.environment if context.environment

    environment = Bundler.unbundled_env.merge("BUNDLE_PATH" => Bundler.settings[:path].to_s, "BUNDLE_GEMFILE" => context.gemfile.to_s, "BUNDLE_FROZEN" => "1", "BUNDLE_IGNORE_CONFIG" => "1")
    query = run_process([RUBY, "-rjson", "-e", 'gem "bundler", ARGV.shift; require "bundler"; prepared = Gem.path; Bundler.setup; puts JSON.generate({home: Gem.dir, paths: (prepared + Gem.path + Bundler.load.specs.map(&:base_dir)).uniq})', context.version], directory: CARRIER, environment: environment)
    expect(query.returncode).to eq(0), query.stderr
    facts = JSON.parse(query.stdout)
    context.environment = {"GEM_HOME" => facts.fetch("home"), "GEM_PATH" => facts.fetch("paths").join(File::PATH_SEPARATOR)}
  end

  def facts_native(context)
    state = @scratch / "native state"
    environment = closed_environment(state).merge(context_environment(context))
    %w[BUNDLE_APP_CONFIG BUNDLE_USER_HOME BUNDLE_USER_CACHE].each do |variable|
      path = state / variable.downcase
      FileUtils.mkdir_p(path)
      environment[variable] = path.to_s
    end
    environment.merge!("BUNDLE_IGNORE_CONFIG" => "1", "BUNDLE_FROZEN" => "1")
    [context.version, environment]
  end

  def command(native, directory, arguments, environment: {})
    run_process([RUBY, *arguments], directory: directory, environment: native[1].merge(environment))
  end

  def facts(native, operation, path, environment: {}, raw: nil)
    request = {"operation" => operation, "path" => path.to_s, "raw" => raw}
    validate_schema("ruby-fact-request", request)
    request_path = write(path.dirname / "facts-request.json", JSON.generate(request))
    command(native, path.dirname, ["-e", 'gem "bundler", ARGV.shift; load ARGV.shift', native[0], HELPERS / "facts.rb", request_path], environment: environment)
  end

  def answer(result)
    expect(result.returncode).to eq(0), result.stderr
    value = JSON.parse(result.stdout)
    name = if value.key?("archive") then "ruby-archive"
    elsif value.key?("specifications") then "ruby-bundle"
    elsif value.key?("raw") then "ruby-version"
    elsif value.key?("gemfile") then "ruby-context"
    else "ruby-source"
    end
    validate_schema(name, value)
    value
  end

  def gem_fixture(directory, name, dependencies = [])
    write(directory / "lib/#{name}.rb", "VALUE = :native_fixture\n")
    declaration = <<~RUBY
      Gem::Specification.new do |spec|
        spec.name = #{name.to_json}
        spec.version = "1.2.3"
        spec.summary = "Native facts fixture"
        spec.authors = ["Fixture"]
        spec.license = "MIT"
        spec.files = Dir["lib/**/*.rb"]
    RUBY
    dependencies.each { |dependency| declaration << "  spec.add_runtime_dependency #{dependency.to_json}, '= 1.2.3'\n" }
    write(directory / "#{name}.gemspec", declaration + "end\n")
  end

  def lock(native, gemfile, environment: {})
    result = command(native, gemfile.dirname, ["-S", "bundle", "_#{native[0]}_", "lock", "--local"], environment: {"BUNDLE_GEMFILE" => gemfile.to_s, "BUNDLE_FROZEN" => "0"}.merge(environment))
    expect(result.returncode).to eq(0), result.stderr
    {"BUNDLE_GEMFILE" => gemfile.to_s}.merge(environment)
  end
end

RSpec.configure do |config|
  config.include NativeHelpers
  config.around do |example|
    # Fixture state and child diagnostics are temporary execution inputs.
    # The ordinary group result retains RSpec's report and outer command outcome.
    Dir.mktmpdir("workflow-ruby-native-") do |directory|
      @scratch = Pathname.new(directory)
      @receipts = []
      example.run
    end
  end
end
