# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require "digest"

module Asciidoctor
  module Latexmath
    module Cache
      class CacheKey
        FIELDS_ORDER = %i[ext_version content_hash format preamble_hash fontsize_hash ppi entry_type].freeze

        attr_reader(*FIELDS_ORDER)

        def initialize(ext_version:, content_hash:, format:, preamble_hash:, fontsize_hash:, ppi:, entry_type:)
          @ext_version = ext_version
          @content_hash = content_hash
          @format = format
          @preamble_hash = preamble_hash
          @fontsize_hash = fontsize_hash || "-"
          @ppi = ppi || "-"
          @entry_type = entry_type
        end

        def digest
          @digest ||= Digest::SHA256.hexdigest(FIELDS_ORDER.map { |field| public_send(field).to_s }.join("\n"))
        end
      end
    end
  end
end
