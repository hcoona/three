# frozen_string_literal: true

# Reviewed native mechanisms. Run only in an unprivileged evaluation/quality zone.
require "rubygems"
require "rubygems/package"
require "json"
require "digest"
require "stringio"
require "zlib"

NAME = "hcoona-release-smoke-ruby"
FILES = [
  "LICENSE", "README.md", "lib/hcoona_release_smoke_ruby.rb",
  "lib/hcoona_release_smoke_ruby/_workflow_delivery_provenance.json",
  "lib/hcoona_release_smoke_ruby/version.rb"
].freeze
SOURCE_FILES = FILES.reject { |path| path.end_with?("/_workflow_delivery_provenance.json") }.freeze
LIMIT = 2 * 1024 * 1024

def facts(spec, files: spec&.files, source: false)
  raise "missing native specification" unless spec
  raise "unsupported native specification" unless spec.name == NAME &&
    spec.platform.to_s == "ruby" && spec.dependencies.empty? &&
    spec.extensions.empty? && spec.executables.empty? && spec.cert_chain.empty? &&
    spec.require_paths == ["lib"] &&
    (files.sort == FILES || (source && files.sort == SOURCE_FILES)) &&
    spec.metadata == { "github_repo" => "https://github.com/hcoona/three" } &&
    spec.required_ruby_version.to_s == ">= 4.0" && spec.licenses == ["MIT"]

  { "name" => spec.name, "version" => spec.version.to_s,
    "platform" => spec.platform.to_s, "files" => files.sort,
    "required-ruby" => spec.required_ruby_version.to_s }
end

def bounded_gunzip(bytes)
  reader = Zlib::GzipReader.new(StringIO.new(bytes))
  decoded = reader.read(LIMIT + 1)
  raise "expanded Ruby archive exceeds limit" if decoded.bytesize > LIMIT
  reader.close
  decoded
end

def members(bytes)
  result = {}
  Gem::Package::TarReader.new(StringIO.new(bytes)) do |tar|
    tar.each do |entry|
      name = entry.full_name
      raise "unsafe or duplicate gem member" unless entry.file? &&
        !name.empty? && !name.start_with?("/") && !name.include?("\\") &&
        !name.include?("\0") && name.split("/", -1).none? { |p| ["", ".", ".."].include?(p) } &&
        !result.key?(name) && entry.size <= LIMIT && result.length < 16
      result[name] = entry.read
    end
  end
  result
end

request = JSON.parse(STDIN.read(LIMIT + 1))
raise "invalid native request" unless request.is_a?(Hash)
result = case request.fetch("operation")
when "profile"
  { "ruby" => RUBY_VERSION, "rubygems" => Gem::VERSION,
    "ruby-platform" => RUBY_PLATFORM, "ruby-revision" => RUBY_REVISION.to_s,
    "zlib" => Zlib::ZLIB_VERSION, "zlib-runtime" => Zlib.zlib_version }
when "version"
  raw = request.fetch("raw")
  raise "invalid Ruby version input" unless raw.is_a?(String) && !raw.empty? &&
    raw == raw.strip && !raw.match?(/\s/) && !raw.include?("+") && Gem::Version.correct?(raw)
  { "raw" => raw, "native" => Gem::Version.new(raw).to_s }
when "specification"
  facts(Gem::Specification.load(request.fetch("gemspec")), source: true)
when "build"
  spec = Gem::Specification.load(request.fetch("gemspec"))
  result = facts(spec)
  saved_stdout = $stdout
  begin
    $stdout = $stderr
    Gem::Package.build(spec, false, true, request.fetch("output"))
  ensure
    $stdout = saved_stdout
  end
  result
when "inspect"
  path = request.fetch("archive")
  raise "gem exceeds limit" if File.size(path) > LIMIT
  outer = members(File.binread(path))
  raise "unexpected gem envelope" unless outer.keys.sort == ["checksums.yaml.gz", "data.tar.gz", "metadata.gz"]
  outer.each_value { |bytes| bounded_gunzip(bytes) }
  package = Gem::Package.new(path)
  package.verify
  result = facts(package.spec)
  data = members(bounded_gunzip(outer.fetch("data.tar.gz")))
  raise "unexpected gem payload" unless data.keys.sort == FILES
  result.merge("members" => data.transform_values { |bytes| {
    "sha256" => "sha256:#{Digest::SHA256.hexdigest(bytes)}", "size" => bytes.bytesize
  } }, "witness" => data.fetch("lib/hcoona_release_smoke_ruby/_workflow_delivery_provenance.json"),
    "version-source" => data.fetch("lib/hcoona_release_smoke_ruby/version.rb"))
when "consumer"
  require "hcoona_release_smoke_ruby"
  spec = Gem.loaded_specs.fetch(NAME)
  # RubyGems intentionally omits files from its installed cache specification.
  # Validate the extracted files themselves instead of the stripped cache field.
  paths = Dir.glob("**/*", File::FNM_DOTMATCH, base: spec.full_gem_path)
  raise "symlink in installed gem" if paths.any? { |p| File.symlink?(File.join(spec.full_gem_path, p)) }
  files = paths.select { |p| File.file?(File.join(spec.full_gem_path, p)) }
  result = facts(spec, files: files)
  result.merge("project-id" => HcoonaReleaseSmokeRuby.project_id,
    "installed-version" => HcoonaReleaseSmokeRuby::VERSION,
    "installed-root" => spec.full_gem_path,
    "witness" => File.binread(File.join(spec.full_gem_path,
      "lib/hcoona_release_smoke_ruby/_workflow_delivery_provenance.json")))
else
  raise "unsupported native operation"
end
STDOUT.write(JSON.generate(result))
