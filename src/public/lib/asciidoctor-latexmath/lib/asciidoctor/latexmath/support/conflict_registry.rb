# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require_relative "../errors"

module Asciidoctor
  module Latexmath
    module Support
      class ConflictRegistry
        Entry = Struct.new(:signature, :details)

        def initialize
          @entries = {}
        end

        def register!(basename, signature, details)
          normalized = normalize_details(details)
          entry = entries[basename]
          if entry
            return if entry.signature == signature

            raise TargetConflictError, conflict_message(basename, entry, normalized, signature)
          end

          entries[basename] = Entry.new(signature, normalized)
        end

        private

        attr_reader :entries

        def normalize_details(details)
          {
            location: details[:location] || "unknown location",
            format: (details[:format] || "unknown").to_s,
            content_hash: (details[:content_hash] || "").to_s,
            preamble_hash: (details[:preamble_hash] || "").to_s,
            fontsize_hash: (details[:fontsize_hash] || "").to_s,
            entry_type: (details[:entry_type] || "unknown").to_s
          }
        end

        def conflict_message(basename, existing_entry, incoming_details, incoming_signature)
          existing_details = existing_entry.details
          <<~MSG.strip
            conflicting target '#{basename}' already defined at #{existing_details[:location]} (signature #{signature_summary(existing_entry.signature, existing_details)}).
            new definition from #{incoming_details[:location]} would produce signature #{signature_summary(incoming_signature, incoming_details)}.
            hint: choose a unique target basename or remove the explicit target attribute.
          MSG
        end

        def signature_summary(signature, details)
          parts = []
          parts << "key=#{signature.to_s[0, 12]}" if signature
          parts << "format=#{details[:format]}"
          parts << "content=#{truncate(details[:content_hash])}"
          parts << "preamble=#{truncate(details[:preamble_hash])}"
          parts << "fontsize=#{truncate(details[:fontsize_hash])}"
          parts << "entry=#{details[:entry_type]}"
          parts.join(" ")
        end

        def truncate(value)
          text = value.to_s
          return "-" if text.empty?

          text[0, 12]
        end
      end
    end
  end
end
