# frozen_string_literal: true

require "json"
require "uri"
require "rubygems/source"
require "rubygems/source_list"
require "rubygems/name_tuple"
raise "stdin must be EOF" unless STDIN.read.empty?
scenario = ENV.fetch("TEST_SCENARIO")
trace_path = ENV.fetch("TEST_TRACE")
calls = []
at_exit { File.write(trace_path, JSON.generate(calls)) }
Gem::Source.prepend(Module.new do
  define_method(:load_specs) do |type|
    raise "not a real native Source" unless is_a?(Gem::Source)
    unless Gem.sources.is_a?(Gem::SourceList)
      raise "not the selected native SourceList"
    end
    raise "alternate source configured" unless Gem.sources.to_a == [uri.to_s]
    if (Gem.configuration.sources || []).any? { |source| source == "https://alternate.invalid" }
      raise "unexpected inherited config"
    end
    expected_host = JSON.parse(File.read(ARGV.fetch(0))).fetch("host")
    selected = URI.parse(expected_host)
    unless uri.host == selected.host && uri.path.chomp("/") == selected.path.chomp("/")
      raise "wrong native host"
    end
    credential_ok = if uri.host == "rubygems.pkg.github.com"
      URI.decode_www_form_component(uri.user) ==
        ENV.fetch("RUBY_SOURCE_USER") &&
        URI.decode_www_form_component(uri.password) ==
          ENV.fetch("RUBY_SOURCE_TOKEN") &&
        uri.user.include?("%40") && uri.user.include?("%3A") &&
        uri.password.include?("%2B") && uri.password.include?("%25") &&
        !uri.user.include?(" ") && !uri.password.include?(" ")
    else
      uri.user.nil? && uri.password.nil?
    end
    raise "incorrect native credential encoding" unless credential_ok
    calls << { "method" => "load_specs", "type" => type.to_s,
      "source_id" => object_id, "selected_only" => true,
      "credential_ok" => credential_ok }
    raise "unsafe-native-exception-detail #{uri}" if scenario == "fail-#{type}"
    version = Gem::Version.new(ENV.fetch("TEST_VERSION"))
    tuple = Gem::NameTuple.new(ENV.fetch("TEST_NAME"), version, "ruby")
    # Native NameTuple, Version and Requirement own exact tuple interpretation.
    distractors = [Gem::NameTuple.new("another-package", version, "ruby"),
      Gem::NameTuple.new(tuple.name, Gem::Version.new("9.9.9"), "ruby"),
      Gem::NameTuple.new(tuple.name, version, "x86_64-linux")]
    matching_query = version.prerelease? ? :prerelease : :released
    return distractors if scenario == "absent" || type != matching_query
    return [tuple] * 65 if scenario == "too-many"
    if scenario == "duplicate"
      return [tuple, Gem::NameTuple.new(tuple.name, version, "ruby")]
    end
    [*distractors, tuple]
  end

  define_method(:fetch_spec) do |tuple|
    raise "not a native NameTuple" unless tuple.is_a?(Gem::NameTuple)
    requirement = Gem::Requirement.new("= #{ENV.fetch('TEST_VERSION')}")
    unless tuple.name == ENV.fetch("TEST_NAME") &&
      requirement.satisfied_by?(tuple.version) && tuple.platform == "ruby"
      raise "not exact native tuple"
    end
    calls << { "method" => "fetch_spec", "name" => tuple.name,
      "version" => tuple.version.to_s, "platform" => tuple.platform,
      "source_id" => object_id }
    raise "unsafe-native-exception-detail #{uri}" if scenario == "fail-spec"
    Gem::Specification.new do |spec|
      spec.name = scenario == "spec-name" ? "another-package" : tuple.name
      spec.version = scenario == "spec-version" ? "9.0.0" : tuple.version
      spec.platform = scenario == "spec-platform" ?
        "x86_64-linux" : tuple.platform
      spec.summary = "Controlled native source fixture"
      spec.authors = ["Fixture"]
    end
  end

  define_method(:download) do |spec, directory|
    raise "not a native Specification" unless spec.is_a?(Gem::Specification)
    request = JSON.parse(File.read(ARGV.fetch(0)))
    requested_directory = request.fetch("directory")
    unless directory == File.expand_path(requested_directory)
      raise "wrong original destination"
    end
    calls << { "method" => "download", "name" => spec.name,
      "version" => spec.version.to_s, "platform" => spec.platform.to_s,
      "directory" => directory, "source_id" => object_id }
    raise "unsafe-native-exception-detail #{uri}" if scenario == "fail-download"
    path = File.join(directory, "native-original.gem")
    bytes = "actual controlled native download"
    bytes = "" if scenario == "empty-original"
    if scenario == "outside-original"
      path = File.join(File.dirname(directory), "outside.gem")
    elsif scenario == "prefix-sibling"
      sibling = directory + "-other"
      Dir.mkdir(sibling)
      path = File.join(sibling, "original.gem")
    elsif scenario == "missing-original"
      return path
    elsif scenario == "directory-original"
      Dir.mkdir(path)
      return path
    elsif scenario == "invalid-original"
      return nil
    end
    File.binwrite(path, bytes)
    path
  end
end)
