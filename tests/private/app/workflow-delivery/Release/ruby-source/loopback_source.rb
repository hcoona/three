# frozen_string_literal: true

# Contract seam: route the production RubyGems source hosts to a loopback gem source. Only the
# URI host, port and scheme change; user information and path stay as source.rb built them, and
# every RubyGems method (inventory, specification, download) runs unchanged.
require "rubygems"
require "rubygems/source"
require "uri"

module LoopbackGemSource
  PORT = Integer(ENV.fetch("LOOPBACK_GEM_SOURCE_PORT"))
  HOSTS = %w[rubygems.org rubygems.pkg.github.com].freeze

  def initialize(uri)
    parsed = uri.is_a?(String) ? URI.parse(uri) : uri.dup
    if HOSTS.include?(parsed.host)
      # Ruby's URI clears user information when the host changes; restore it unchanged.
      user, password = parsed.user, parsed.password
      parsed.host = "127.0.0.1"
      parsed.port = PORT
      parsed.user = user if user
      parsed.password = password if password
    end
    super(parsed)
  end
end

Gem::Source.prepend(LoopbackGemSource)
