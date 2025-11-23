# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

module Asciidoctor
  module Latexmath
    module Rendering
      RendererResult = Struct.new(:output_path, :format, :intermediate, keyword_init: true)

      class Renderer
        def name
          raise NotImplementedError, "Renderer subclasses must implement #name"
        end

        def render(_request, _context)
          raise NotImplementedError, "Renderer subclasses must implement #render"
        end

        private

        def truncate_output(output, limit = 500)
          return "" unless output

          return output if output.length <= limit

          "#{output[0, limit]}..."
        end
      end
    end
  end
end
