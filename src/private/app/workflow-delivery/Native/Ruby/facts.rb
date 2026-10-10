# frozen_string_literal: true

# Native libraries own evaluation and dependency selection. This helper only serializes their answers.
require "json"
require "rubygems"
require "rubygems/package"

def specification_facts(spec)
  raise "Missing native gem specification" unless spec

  {
    "name" => spec.name,
    "version" => spec.version.to_s,
    "platform" => spec.platform.to_s,
    "require_paths" => spec.require_paths,
    "files" => spec.files,
    "runtime_dependencies" => spec.runtime_dependencies.map do |dependency|
      { "name" => dependency.name, "requirement" => dependency.requirement.to_s }
    end
  }
end

def bundle_source_facts(spec)
  source = spec.source
  case source
  when Bundler::Source::Git
    if source.local?
      { "kind" => "local_git", "path" => source.path.expand_path.to_s,
        "gemspec" => spec.loaded_from, "directory" => spec.full_gem_path }
    else
      { "kind" => "git", "uri" => source.uri, "revision" => source.revision }
    end
  when Bundler::Source::Path
    { "kind" => "path", "path" => source.expanded_original_path.to_s,
      "gemspec" => spec.loaded_from, "directory" => spec.full_gem_path }
  when Bundler::Source::Rubygems
    { "kind" => "rubygems" }
  when Bundler::Source::Metadata
    { "kind" => "metadata" }
  else
    raise "Unsupported native Bundler source: #{source.class}"
  end
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

  selected = definition.requested_specs.to_a
  {
    "gemfile" => actual_gemfile,
    "lockfile" => Bundler.default_lockfile.expand_path.to_s,
    "bundler_version" => Bundler::VERSION,
    "locked_bundler_version" => locked_version.to_s,
    "gemfiles" => definition.gemfiles.map { |path| File.expand_path(path) },
    "direct_dependencies" => definition.requested_dependencies.map(&:name),
    "specifications" => selected.map do |spec|
      specification_facts(spec).merge("source" => bundle_source_facts(spec))
    end
  }
end

request = JSON.parse(File.read(ARGV.fetch(0)))
path = File.expand_path(request.fetch("path"))
result = case request.fetch("operation")
when "gemspec"
  specification_facts(Gem::Specification.load(path)).merge("gemspec" => path)
when "bundle"
  bundle_facts(path)
when "archive"
  package = Gem::Package.new(path)
  package.verify
  specification_facts(package.spec).merge("archive" => path, "contents" => package.contents)
else
  raise "Unsupported native Ruby fact operation"
end

puts JSON.generate(result)
