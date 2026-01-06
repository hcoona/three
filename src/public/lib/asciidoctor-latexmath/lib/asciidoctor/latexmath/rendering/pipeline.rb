# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require_relative "renderer"

module Asciidoctor
  module Latexmath
    module Rendering
      class Pipeline
        DEFAULT_STAGE_IDENTIFIERS = %i[pdflatex pdf_to_svg pdf_to_png].freeze

        def self.default_stage_identifiers
          DEFAULT_STAGE_IDENTIFIERS
        end

        def self.signature
          DEFAULT_STAGE_IDENTIFIERS.join("|")
        end

        def initialize(stages)
          @stages = stages
        end

        def execute(request, context)
          current_output = nil

          stages.each do |stage|
            stage_context = enrich_context(context, current_output)
            current_output = stage.render(request, stage_context)
          end

          current_output
        end

        private

        attr_reader :stages

        def enrich_context(context, previous_output)
          return context if previous_output.nil?

          if context.respond_to?(:merge)
            context.merge(previous_output: previous_output)
          elsif context.respond_to?(:dup) && context.respond_to?(:[]=)
            duplicated = context.dup
            duplicated[:previous_output] = previous_output
            duplicated
          else
            context
          end
        end
      end
    end
  end
end
