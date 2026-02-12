# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

module Asciidoctor
  module Latexmath
    module Cache
      class CacheEntry
        attr_reader :final_path, :format, :content_hash, :preamble_hash, :fontsize, :engine,
          :ppi, :entry_type, :created_at, :checksum, :size_bytes, :tool_presence

        def initialize(final_path:, format:, content_hash:, preamble_hash:, fontsize:, engine:, ppi:, entry_type:, created_at:, checksum:, size_bytes:, tool_presence: {})
          @final_path = final_path
          @format = format
          @content_hash = content_hash
          @preamble_hash = preamble_hash
          @fontsize = fontsize
          @engine = engine
          @ppi = ppi
          @entry_type = entry_type
          @created_at = created_at
          @checksum = checksum
          @size_bytes = size_bytes
          @tool_presence = (tool_presence || {}).transform_keys(&:to_s)
        end
      end
    end
  end
end
