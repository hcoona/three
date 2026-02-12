# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require "asciidoctor"
require "asciidoctor/extensions"

require_relative "../renderer_service"

module Asciidoctor
  module Latexmath
    module Processors
      class BlockProcessor < ::Asciidoctor::Extensions::BlockProcessor
        use_dsl

        named :latexmath
        on_context :listing, :literal, :open, :pass, :paragraph, :stem
        parse_content_as :raw

        def process(parent, reader, attrs)
          result = renderer_service(parent).render_block(parent, reader, attrs)
          return create_block(parent, :pass, result.placeholder_html, {}) if result.type == :placeholder

          create_image_block(parent, build_block_attributes(result, attrs))
        end

        private

        def renderer_service(parent)
          document = parent.document
          document.instance_variable_get(:@latexmath_renderer_service) || begin
            service = RendererService.new(document)
            document.instance_variable_set(:@latexmath_renderer_service, service)
            service
          end
        end

        def build_block_attributes(result, original_attrs)
          attributes = result.attributes.dup
          attributes["target"] ||= result.target
          attributes["alt"] = sanitize_alt(fetch_attr(original_attrs, :alt), result.alt_text)
          attributes["data-latex-original"] = result.alt_text
          attributes["role"] = merge_roles(attributes["role"], fetch_attr(original_attrs, :role))
          if (align = fetch_attr(original_attrs, :align))
            attributes["align"] = align
          end
          attributes
        end

        def fetch_attr(attrs, name)
          return nil unless attrs

          attrs[name.to_s] || attrs[name]
        end

        def sanitize_alt(user_value, fallback)
          return fallback if user_value.nil?

          value = user_value.to_s
          value.empty? ? fallback : value
        end

        def merge_roles(existing_role, additional_role)
          existing = existing_role.to_s.split
          additional = additional_role.to_s.split
          combined = (existing + additional).uniq
          combined.empty? ? nil : combined.join(" ")
        end
      end
    end
  end
end
