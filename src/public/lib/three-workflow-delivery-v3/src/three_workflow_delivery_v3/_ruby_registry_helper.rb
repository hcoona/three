# frozen_string_literal: true

# Decode registry data only in an unprivileged process with no credentials.
require "rubygems"
require "json"
require "stringio"
require "zlib"

raise "unsupported Ruby registry reader" unless RUBY_VERSION == "4.0.7" && Gem::VERSION == "4.0.20"

LIMIT = 2 * 1024 * 1024
COUNT_LIMIT = 4096
NAME = "hcoona-release-smoke-ruby"

def version(text)
  raise "invalid registry version" unless text.is_a?(String) && !text.empty? &&
    text == text.strip && !text.match?(/\s/) && !text.include?("+") && Gem::Version.correct?(text)
  Gem::Version.new(text)
end

input = STDIN.read(LIMIT + 1)
raise "registry reader input exceeds limit" if input.bytesize > LIMIT
request = JSON.parse(input)
expected = version(request.fetch("version"))
format = request.fetch("format")
entries = if format == "marshal"
  path = request.fetch("path")
  raise "registry index exceeds limit" unless File.size(path).between?(1, LIMIT)
  compressed = StringIO.new(File.binread(path))
  reader = Zlib::GzipReader.new(compressed)
  bytes = reader.read(LIMIT + 1)
  raise "expanded registry index exceeds limit" if bytes.bytesize > LIMIT
  raise "trailing registry index stream" unless reader.unused.to_s.empty? && compressed.eof?
  reader.close
  Gem.load_safe_marshal
  Gem::SafeMarshal.safe_load(bytes)
elsif format == "json"
  request.fetch("entries")
else
  raise "unsupported registry index format"
end
raise "invalid registry index inventory" unless entries.is_a?(Array) && entries.length <= COUNT_LIMIT

selected = []
entries.each do |entry|
  raise "invalid registry index entry" unless entry.is_a?(Array) && entry.length == 3
  name, number, platform = entry
  raise "invalid registry index identity" unless name.is_a?(String) && !name.empty? &&
    platform.is_a?(String) && !platform.empty?
  if format == "marshal"
    raise "invalid native registry version" unless number.instance_of?(Gem::Version)
    text = number.to_s
  else
    text = number
  end
  native = version(text)
  next unless name == NAME && native == expected
  selected << { "name" => name, "version" => text, "platform" => platform }
end
STDOUT.write(JSON.generate({ "candidates" => selected }))
