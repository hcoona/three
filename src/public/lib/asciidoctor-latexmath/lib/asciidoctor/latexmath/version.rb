# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require "rubygems"

module Asciidoctor
  module Latexmath
    # Resolves the installed or source-tree gem version and normalizes SemVer2 strings.
    module Version
      GEM_NAME = "asciidoctor-latexmath"
      STATIC_VERSION = "2.1.0.alpha.1"

      module_function

      # Returns the active gem version string, preferring loaded gem metadata.
      #
      # @return [String]
      def current
        from_loaded_gem || STATIC_VERSION
      end

      # Returns the version from `Gem.loaded_specs` when loaded as a gem.
      #
      # @return [String, nil]
      def from_loaded_gem
        Gem.loaded_specs[GEM_NAME]&.version&.to_s
      end

      # Converts a SemVer2 string (stripping build metadata) to RubyGems version syntax.
      #
      # @param version [String, #to_s] SemVer2 or RubyGems version string
      # @return [String] normalized RubyGems version string
      def rubygems_version(version)
        Gem::Version.new(version.to_s.split("+", 2).first).to_s
      end
    end

    # Active version of `asciidoctor-latexmath`.
    VERSION = Version.current
  end
end
