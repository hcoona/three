# frozen_string_literal: true

# RubyGems owns inventory decoding, specification fetch and original download.
require "json"
require "digest"
require "uri"
require "rubygems"
require "rubygems/source"
require "rubygems/source_list"
require "rubygems/config_file"
require "rubygems/user_interaction"

def read_selected_source(request)
  host = request.fetch("host")
  unless ["https://rubygems.pkg.github.com/hcoona", "https://rubygems.org"].include?(host)
    raise "Unsupported native Ruby source"
  end
  name = request.fetch("name")
  raise "Unsupported native Ruby package" unless name == "hcoona-release-smoke-ruby"

  version = Gem::Version.new(request.fetch("version"))
  requirement = Gem::Requirement.new("= #{version}")
  directory = File.realpath(request.fetch("directory"))
  Gem.configuration = Gem::ConfigFile.new(["--norc"])
  Gem::DefaultUserInteraction.ui = Gem::SilentUI.new
  uri = URI.parse(host)
  if uri.host == "rubygems.pkg.github.com"
    user = ENV.fetch("RUBY_SOURCE_USER")
    token = ENV.fetch("RUBY_SOURCE_TOKEN")
    raise "Missing native Ruby read credential" if user.empty? || token.empty?

    uri.user = URI.encode_www_form_component(user).gsub("+", "%20")
    uri.password = URI.encode_www_form_component(token).gsub("+", "%20")
  end
  source = Gem::Source.new(uri.to_s)
  sources = Gem::SourceList.new
  sources << source
  Gem.sources = sources
  # Both inventories must succeed; convenience complete queries may hide errors.
  released = source.load_specs(:released)
  prerelease = source.load_specs(:prerelease)
  selected = (released + prerelease).select do |tuple|
    tuple.name == name && requirement.satisfied_by?(tuple.version) && tuple.platform == "ruby"
  end
  raise "Ambiguous native Ruby inventory" if selected.length > 64 || selected.uniq.length != selected.length
  return { "state" => "absent" } if selected.empty?
  raise "Ambiguous native Ruby identity" unless selected.length == 1

  tuple = selected.fetch(0)
  spec = source.fetch_spec(tuple)
  unless spec.name == tuple.name && spec.version == tuple.version && spec.platform.to_s == "ruby"
    raise "Contradictory native Ruby specification"
  end
  path = File.realpath(source.download(spec, directory))
  unless path.start_with?(directory + File::SEPARATOR) && File.file?(path)
    raise "Native Ruby download leaves owned state"
  end
  size = File.size(path)
  raise "Unsupported native Ruby original size" unless size.positive? && size <= 8 * 1024 * 1024

  { "state" => "present", "name" => spec.name, "version" => spec.version.to_s,
    "platform" => spec.platform.to_s, "path" => path, "size" => size,
    "sha256" => Digest::SHA256.file(path).hexdigest }
end

if $PROGRAM_NAME == __FILE__
  # Native exceptions can contain credential-bearing source URIs. Only this
  # portable answer may cross the read process boundary; diagnostics are omitted.
  begin
    answer = read_selected_source(JSON.parse(File.read(ARGV.fetch(0))))
    serialized = JSON.generate(answer)
    raise "Native Ruby answer too large" if serialized.bytesize > 256 * 1024

    puts serialized
  rescue StandardError
    puts JSON.generate("state" => "unknown")
    exit 1
  end
end
