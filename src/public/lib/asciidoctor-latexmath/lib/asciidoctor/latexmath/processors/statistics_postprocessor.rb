# frozen_string_literal: true

require "asciidoctor"
require "asciidoctor/extensions"

module Asciidoctor
  module Latexmath
    module Processors
      class StatisticsPostprocessor < ::Asciidoctor::Extensions::Postprocessor
        def process(document, output)
          service = document.instance_variable_get(:@latexmath_renderer_service)
          service&.send(:flush_statistics)
          output
        end
      end
    end
  end
end
