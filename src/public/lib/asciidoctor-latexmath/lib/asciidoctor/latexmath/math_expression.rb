# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

module Asciidoctor
  module Latexmath
    class MathExpression
      attr_reader :content, :entry_type, :target_basename, :attributes, :options, :location

      def initialize(content:, entry_type:, target_basename: nil, attributes: {}, options: [], location: nil)
        @content = content
        @entry_type = entry_type
        @target_basename = target_basename
        @attributes = attributes
        @options = options
        @location = location
      end
    end
  end
end
