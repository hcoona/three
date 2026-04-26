# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require "rubygems"

module Asciidoctor
  module Latexmath
    module Version
      GEM_NAME = "asciidoctor-latexmath"
      STATIC_VERSION = "2.1.0.alpha.1"

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
