# frozen_string_literal: true

require "asciidoctor/converter/html5"

require_relative "../html_builder"
require_relative "../renderer_service"

module Asciidoctor
  module Latexmath
    module Converters
      module Html5
        def convert_stem(node)
          if latexmath_block?(node)
            convert_latexmath_block(node)
          else
            super
          end
        end

        def convert_inline_quoted(node)
          if latexmath_inline?(node)
            convert_latexmath_inline(node)
          else
            super
          end
        end

        private

        def latexmath_block?(node)
          node.style == "latexmath"
        end

        def latexmath_inline?(node)
          node.type == :latexmath
        end

        def convert_latexmath_block(node)
          result = latexmath_renderer_service(node.document)
            .render_block_content(node.parent || node.document, node.source, safe_attributes(node.attributes))

          return result.placeholder_html if result.type == :placeholder

          HtmlBuilder.block_html(node, result)
        end

        def convert_latexmath_inline(node)
          result = latexmath_renderer_service(node.document)
            .render_inline_content(node.parent || node.document, node.text, safe_attributes(node.attributes))

          return result.placeholder_html if result.type == :placeholder

          HtmlBuilder.inline_html(node, result)
        end

        def latexmath_renderer_service(document)
          document.instance_variable_get(:@latexmath_renderer_service) || begin
            service = RendererService.new(document)
            document.instance_variable_set(:@latexmath_renderer_service, service)
            service
          end
        end

        def safe_attributes(attrs)
          attrs ? attrs.dup : {}
        end
      end
    end
  end
end

Asciidoctor::Converter::Html5Converter.prepend(Asciidoctor::Latexmath::Converters::Html5)
