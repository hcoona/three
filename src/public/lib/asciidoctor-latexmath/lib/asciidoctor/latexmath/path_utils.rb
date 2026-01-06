# frozen_string_literal: true

require "pathname"

module Asciidoctor
  module Latexmath
    module PathUtils
      module_function

      def normalize_separators(path)
        return path unless path.is_a?(String) || path.is_a?(Symbol)

        path.to_s.tr("\\", "/")
      end

      def absolute_path?(path)
        return false if path.nil?

        normalized = normalize_separators(path).to_s
        return false if normalized.empty?

        normalized.start_with?("/", "~") ||
          normalized.match?(/\A[A-Za-z]:\//) ||
          normalized.start_with?("//")
      end

      def expand_path(path, base_dir)
        return nil if path.nil?

        normalized_path = normalize_separators(path)
        normalized_base = normalize_separators(base_dir || Dir.pwd)

        if absolute_path?(normalized_path)
          File.expand_path(normalized_path)
        else
          File.expand_path(normalized_path, normalized_base)
        end
      end

      def clean_join(*parts)
        normalized_parts = parts.compact.map { |part| normalize_separators(part) }
        File.join(*normalized_parts)
      end
    end
  end
end
