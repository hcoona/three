# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require "asciidoctor"
require "asciidoctor/extensions"

module Asciidoctor
  module Latexmath
    module Processors
      # Postprocessor flushing the single per-document `latexmath stats:` summary log line.
      class StatisticsPostprocessor < ::Asciidoctor::Extensions::Postprocessor
        # @param document [::Asciidoctor::Document] converted document
        # @param output [String] rendered output payload
        # @return [String] unmodified `output`
        def process(document, output)
          service = document.instance_variable_get(:@latexmath_renderer_service)
          service&.send(:flush_statistics)
          output
        end
      end
    end
  end
end
