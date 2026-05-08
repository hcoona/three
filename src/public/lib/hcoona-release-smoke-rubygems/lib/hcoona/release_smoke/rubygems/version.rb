# frozen_string_literal: true

require "rubygems"

module Hcoona
  module ReleaseSmoke
    module Rubygems
      module Version
        GEM_NAME = "hcoona-release-smoke-rubygems"
        STATIC_VERSION = "1.0.0.beta"

        module_function

        def current
          from_loaded_gem || STATIC_VERSION
        end

        def from_loaded_gem
          Gem.loaded_specs[GEM_NAME]&.version&.to_s
        end

        def rubygems_version(version)
          Gem::Version.new(version.to_s.split("+", 2).first).to_s
        end
      end

      VERSION = Version.current
    end
  end
end
