# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require "asciidoctor"

module SpecSupport
  module DocumentHelpers
    DEFAULT_ATTRIBUTES = {
      "backend" => "html5",
      "doctype" => "book"
    }.freeze

    def convert_with_extension(source, attributes: {}, options: {})
      merged_attributes = DEFAULT_ATTRIBUTES.merge(attributes)
      Asciidoctor.convert(source, safe: options.fetch(:safe, :safe),
        standalone: false,
        attributes: merged_attributes,
        to_file: false) do |document|
        Asciidoctor::Latexmath.register(document.extensions)
      end
    end
  end
end
