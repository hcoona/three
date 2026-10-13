# frozen_string_literal: true

# Concrete hcoona-release-smoke-ruby installation and API recipe.

# RubyGems owns archive and installation semantics. This child never receives a read credential.
require "json"
require "digest"
require "rubygems"
require "rubygems/installer"
require "rubygems/config_file"
require "rubygems/user_interaction"

Gem.configuration = Gem::ConfigFile.new(["--norc"])
Gem::DefaultUserInteraction.ui = Gem::SilentUI.new

begin
  request = JSON.parse(File.read(ARGV.fetch(0)))
  name = "hcoona-release-smoke-ruby"
  version = request.fetch("version")
  root = File.realpath(request.fetch("install_dir"))
  case request.fetch("operation")
  when "install"
    original = File.realpath(request.fetch("original"))
    digest = request.fetch("sha256")
    raise "Changed original" unless Digest::SHA256.file(original).hexdigest == digest

    # The shared native archive inspector qualifies this exact digest before this child starts.
    raise "Product already installed" unless Gem::Specification.find_all_by_name(name).empty?

    installed = Gem::Installer.at(original, install_dir: root, only_install_dir: true,
      force: false, ignore_dependencies: false, development: false).install
    cache = File.realpath(installed.cache_file)
    unless installed.name == name && installed.version == Gem::Version.new(version) &&
           installed.platform.to_s == "ruby" &&
           cache.start_with?(root + File::SEPARATOR) &&
           Digest::SHA256.file(cache).hexdigest == digest &&
           Digest::SHA256.file(original).hexdigest == digest
      raise "Installed original differs"
    end
  when "consume"
    require "hcoona_release_smoke_ruby"
    spec = Gem.loaded_specs.fetch(name)
    origin = File.realpath(spec.full_gem_path)
    loaded = $LOADED_FEATURES.select do |path|
      path.end_with?("/hcoona_release_smoke_ruby.rb") ||
        path.end_with?("/hcoona_release_smoke_ruby/version.rb")
    end
    unless HcoonaReleaseSmokeRuby.project_id == name &&
           HcoonaReleaseSmokeRuby::VERSION == version &&
           spec.name == name && spec.version == Gem::Version.new(version) &&
           spec.platform.to_s == "ruby" && origin.start_with?(root + File::SEPARATOR) &&
           loaded.length == 2 &&
           loaded.all? { |path| File.realpath(path).start_with?(origin + File::SEPARATOR) } &&
           Digest::SHA256.file(spec.cache_file).hexdigest == request.fetch("sha256")
      raise "Wrong installed API or origin"
    end
  else
    raise "Unsupported native consumer operation"
  end
  puts JSON.generate({ "name" => name, "version" => version, "passed" => true })
rescue Exception
  # Never persist arbitrary native errors; failed installation may include product-controlled text.
  puts JSON.generate({ "passed" => false })
  exit 1
end
