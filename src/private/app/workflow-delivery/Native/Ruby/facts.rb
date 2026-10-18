# frozen_string_literal: true

# Native libraries own evaluation and dependency selection. This helper only serializes their answers.
require "json"
require "rubygems"
require "rubygems/package"
require "rubygems/config_file"

Gem.configuration = Gem::ConfigFile.new(["--norc"])

def specification_facts(spec)
  raise "Missing native gem specification" unless spec

  {
    "name" => spec.name,
    "version" => spec.version.to_s,
    "platform" => spec.platform.to_s,
    "require_paths" => spec.require_paths,
    "files" => spec.files,
    "extensions" => spec.extensions,
    "metadata" => spec.metadata,
    "runtime_dependencies" => spec.runtime_dependencies.map do |dependency|
      { "name" => dependency.name, "requirement" => dependency.requirement.to_s }
    end
  }
end

# Bundler 2.4 lock specifications expose only `dependencies`; select their runtime edges.
def locked_specification_facts(spec)
  {
    "name" => spec.name,
    "version" => spec.version.to_s,
    "platform" => spec.platform.to_s,
    "runtime_dependencies" => spec.dependencies.select { |dependency| dependency.type == :runtime }
      .map { |dependency| { "name" => dependency.name, "requirement" => dependency.requirement.to_s } }
  }
end

def bundle_source_facts(spec)
  source = spec.source
  case source
  when Bundler::Source::Git
    if source.local?
      local = local_specification(spec)
      { "kind" => "local_git", "path" => source.path.expand_path.to_s,
        "gemspec" => local.loaded_from, "directory" => local.full_gem_path }
    else
      { "kind" => "git", "uri" => source.uri, "revision" => source.revision }
    end
  when Bundler::Source::Path
    local = local_specification(spec)
    { "kind" => "path", "path" => source.expanded_original_path.to_s,
      "gemspec" => local.loaded_from, "directory" => local.full_gem_path }
  when Bundler::Source::Rubygems
    { "kind" => "rubygems" }
  when Bundler::Source::Metadata
    { "kind" => "metadata" }
  else
    raise "Unsupported native Bundler source: #{source.class}"
  end
end

# Local Path/Git sources evaluate their original gemspec; no installed gem is involved.
def local_specification(spec)
  local = spec.materialize_for_installation
  raise "Missing native local gem specification" unless local.respond_to?(:loaded_from) && local.loaded_from

  local
end

def selected_bundle_specs(definition)
  # Frozen Bundler answers its locked resolution. Select the requested groups for this
  # platform without materializing installed gems. Only the two qualified lock-selected
  # versions, whose SpecSet#for signatures differ, are supported. An explicit local
  # platform matches Bundler's own selection except on TruffleRuby, whose default
  # ruby-platform forcing is outside the supported runtime.
  resolved = definition.resolve
  dependencies = definition.requested_dependencies
  platforms = [Bundler.local_platform]
  selected = case Bundler::VERSION
  when "2.4.20" then resolved.for(dependencies, true, platforms)
  when "2.7.2" then resolved.for(dependencies, platforms)
  else raise "Unsupported native Bundler lock interface: #{Bundler::VERSION}"
  end
  incomplete = resolved.incomplete_specs
  raise "Incomplete native locked resolution: #{incomplete.map(&:full_name).join(", ")}" unless incomplete.empty?

  bundler = Gem.loaded_specs["bundler"]
  raise "Missing activated native Bundler" unless bundler && bundler.version.to_s == Bundler::VERSION

  selected.map do |spec|
    facts = locked_specification_facts(spec).merge("source" => bundle_source_facts(spec))
    if %w[path local_git].include?(facts.dig("source", "kind"))
      # A local gemspec owns its current runtime edges, as Bundler converges them.
      facts.merge!(locked_specification_facts(local_specification(spec)).slice("runtime_dependencies"))
    end
    facts
  end + [locked_specification_facts(bundler).merge("source" => { "kind" => "metadata" })]
end

def bundle_facts(gemfile)
  require "bundler"

  raise "Bundle collection requires frozen mode" unless Bundler.frozen_bundle?
  actual_gemfile = Bundler.default_gemfile.expand_path.to_s
  raise "Native Gemfile differs from the request" unless actual_gemfile == gemfile

  definition = Bundler.definition
  definition.validate_runtime!
  definition.ensure_equivalent_gemfile_and_lockfile(true)
  locked_version = definition.locked_gems.bundler_version
  unless locked_version && locked_version.to_s == Bundler::VERSION
    raise "Activated Bundler differs from the locked version"
  end

  {
    "gemfile" => actual_gemfile,
    "lockfile" => Bundler.default_lockfile.expand_path.to_s,
    "bundler_version" => Bundler::VERSION,
    "locked_bundler_version" => locked_version.to_s,
    "gemfiles" => definition.gemfiles.map { |path| File.expand_path(path) },
    "direct_dependencies" => definition.requested_dependencies.map(&:name),
    "specifications" => selected_bundle_specs(definition)
  }
end

def bundle_context(gemfile)
  require "bundler"

  actual_gemfile = Bundler.default_gemfile.expand_path.to_s
  raise "Native Gemfile differs from the request" unless actual_gemfile == gemfile

  locked = Bundler.locked_gems
  raise "Missing locked Bundler version" unless locked && locked.bundler_version

  { "gemfile" => actual_gemfile,
    "lockfile" => Bundler.default_lockfile.expand_path.to_s,
    "bundler_version" => Bundler::VERSION,
    "locked_bundler_version" => locked.bundler_version.to_s }
end

def version_facts(raw)
  unless raw.is_a?(String) && !raw.empty? && raw == raw.strip && !raw.match?(/\s/)
    raise "Invalid native version input"
  end

  # Preserve the existing project projection: RubyGems has no SemVer build metadata.
  base = raw.split("+", 2).first
  raise "Missing native version before build metadata" unless base && !base.empty?

  version = Gem::Version.new(base).to_s
  { "raw" => raw, "version" => version, "literal" => version.dump }
end

request = JSON.parse(File.read(ARGV.fetch(0)))
path = File.expand_path(request.fetch("path"))
result = case request.fetch("operation")
when "gemspec"
  specification_facts(Gem::Specification.load(path)).merge("gemspec" => path)
when "bundle"
  bundle_facts(path)
when "bundle-context"
  bundle_context(path)
when "version"
  version_facts(request.fetch("raw")).merge("gemspec" => path)
when "archive"
  package = Gem::Package.new(path)
  package.verify
  specification_facts(package.spec).merge("archive" => path, "contents" => package.contents)
else
  raise "Unsupported native Ruby fact operation"
end

puts JSON.generate(result)
