# frozen_string_literal: true

# Writes the RubyGems compact index files a gem server serves for built originals: the
# Marshal 4.8 released and prerelease inventories and each quick specification.
require "rubygems"
require "rubygems/package"
require "fileutils"

out = ARGV.fetch(0)
released = []
prerelease = []
Dir[File.join(out, "gems", "*.gem")].sort.each do |path|
  spec = Gem::Package.new(path).spec
  tuple = [spec.name, spec.version, spec.platform.to_s]
  (spec.version.prerelease? ? prerelease : released) << tuple
  quick = File.join(out, "quick", "Marshal.#{Gem.marshal_version}")
  FileUtils.mkdir_p(quick)
  File.binwrite(File.join(quick, "#{spec.full_name}.gemspec.rz"), Gem.deflate(Marshal.dump(spec)))
end
File.binwrite(File.join(out, "specs.#{Gem.marshal_version}.gz"), Gem::Util.gzip(Marshal.dump(released)))
File.binwrite(File.join(out, "prerelease_specs.#{Gem.marshal_version}.gz"), Gem::Util.gzip(Marshal.dump(prerelease)))
