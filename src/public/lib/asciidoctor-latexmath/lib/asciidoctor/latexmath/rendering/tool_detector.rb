# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require "asciidoctor"
require "shellwords"

require_relative "toolchain_record"
require_relative "../../latexmath/errors"

module Asciidoctor
  module Latexmath
    module Rendering
      class ToolDetector
        SVG_PRIORITY = %i[dvisvgm pdf2svg].freeze
        PNG_PRIORITY = %i[pdftoppm magick gs].freeze
        SUMMARY_IDENTIFIERS = (SVG_PRIORITY + %i[pdflatex xelatex lualatex tectonic] + PNG_PRIORITY).freeze

        def initialize(request, raw_attributes)
          @request = request
          @raw_attributes = raw_attributes || {}
          @tool_presence_map = {}
          @svg_log_emitted = false
        end

        def emit_tool_summary
          self.class.emit_summary_once do
            summary = SUMMARY_IDENTIFIERS.map do |identifier|
              record = summary_record(identifier)
              "#{identifier}=#{record.available ? "ok" : "missing"}"
            end.join(" ")
            Asciidoctor::LoggerManager.logger&.info { "latexmath.tools: #{summary}" }
          end
        end

        def ensure_svg_tool!
          return nil unless request.format == :svg

          record = detect_svg_tool
          log_svg_tool_selection(record)
          return record if record.available

          message = missing_tool_message(
            "SVG",
            record,
            SVG_PRIORITY,
            fallback_formats: "pdf|png",
            attribute: ":latexmath-pdf2svg:"
          )
          raise MissingToolError.new(record.id, message)
        end

        def ensure_png_tool!
          return nil unless request.format == :png

          record = detect_png_tool
          return record if record.available

          message = missing_tool_message(
            "PNG",
            record,
            PNG_PRIORITY,
            fallback_formats: "svg|pdf",
            attribute: ":latexmath-pdftoppm:"
          )
          raise MissingToolError.new(record.id, message)
        end

        def record_engine(command)
          return if command.nil?

          id = canonical_engine_id(command)
          tool_presence_map[id] = true
        end

        def tool_presence
          tool_presence_map.transform_keys(&:to_s)
        end

        private

        attr_reader :request
        attr_reader :raw_attributes
        attr_reader :tool_presence_map

        def log_svg_tool_selection(record)
          return if @svg_log_emitted

          selected = record&.available ? record.id.to_s : "missing"
          Asciidoctor::LoggerManager.logger&.info { "latexmath.svg.tool=#{selected}" }
          @svg_log_emitted = true
        end

        def detect_svg_tool
          if (explicit = explicit_svg_path)
            id = infer_identifier(request.tool_overrides[:svg] || :pdf2svg, SVG_PRIORITY)
            available = executable_file?(explicit)
            return remember_record(ToolchainRecord.new(id: id, available: available, path: explicit))
          end

          override = request.tool_overrides[:svg]
          if override
            record = resolve_override(override, SVG_PRIORITY)
            return remember_record(record)
          end

          records = SVG_PRIORITY.map { |candidate| remember_record(memoized_lookup(candidate)) }
          records.find(&:available) || records.first
        end

        def detect_png_tool
          if (explicit = explicit_png_path)
            id = infer_identifier(request.tool_overrides[:png] || :pdftoppm, PNG_PRIORITY)
            available = executable_file?(explicit)
            return remember_record(ToolchainRecord.new(id: id, available: available, path: explicit))
          end

          override = request.tool_overrides[:png]
          if override
            record = resolve_override(override, PNG_PRIORITY)
            return remember_record(record)
          end

          records = PNG_PRIORITY.map { |candidate| remember_record(memoized_lookup(candidate)) }
          records.find(&:available) || records.first
        end

        def detect_with_priority(priority)
          priority.each do |candidate|
            record = memoized_lookup(candidate)
            return record if record.available
          end

          memoized_lookup(priority.first)
        end

        def resolve_override(value, priority)
          return nil if value.nil? || value.to_s.strip.empty?

          candidate = value.to_s.strip
          id = infer_identifier(candidate, priority)

          if File.exist?(candidate)
            available = File.executable?(candidate)
            return remember_record(ToolchainRecord.new(id: id, available: available, path: candidate))
          end

          record = memoized_lookup(id, candidate)
          return remember_record(record) if record.available

          remember_record(ToolchainRecord.new(id: id, available: false, path: candidate))
        end

        def infer_identifier(candidate, priority)
          symbol = candidate.respond_to?(:to_sym) ? candidate.to_sym : candidate
          normalized = symbol.to_s.downcase
          explicit = normalized.gsub(/[^a-z0-9]+/, "_").to_sym
          return explicit if priority.include?(explicit)

          if File.exist?(candidate)
            basename = File.basename(candidate)
            inferred = basename.downcase.gsub(/[^a-z0-9]+/, "_").to_sym
            return inferred if priority.include?(inferred)
            return inferred
          end

          explicit
        end

        def memoized_lookup(identifier, override_command = nil)
          command = override_command || identifier.to_s
          self.class.lookup(identifier, command) do
            path = find_executable(command)
            if override_command
              ToolchainRecord.new(id: identifier, available: !path.nil?, path: path)
            else
              ToolchainRecord.new(id: identifier, available: true, path: path || command)
            end
          end
        end

        def find_executable(command)
          return command if executable_file?(command)

          path_extensions = if Gem.win_platform?
            ENV.fetch("PATHEXT", ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC").split(";")
          else
            [""]
          end

          ENV.fetch("PATH", "").split(File::PATH_SEPARATOR).each do |dir|
            path_extensions.each do |ext|
              candidate = File.join(dir, ext.empty? ? command : "#{command}#{ext}")
              return candidate if executable_file?(candidate)
            end
          end

          nil
        end

        def executable_file?(path)
          File.exist?(path) && File.executable?(path) && !File.directory?(path)
        end

        def explicit_svg_path
          override = request.tool_overrides[:svg_path]
          return override if override && !override.to_s.strip.empty?

          extract_path("latexmath-pdf2svg")
        end

        def explicit_png_path
          override = request.tool_overrides[:png_path]
          return override if override && !override.to_s.strip.empty?

          extract_path("latexmath-pdftoppm")
        end

        def extract_path(key)
          value = raw_attributes[key]
          return nil if value.nil?

          stripped = value.to_s.strip
          stripped.empty? ? nil : stripped
        end

        def svg_candidates
          override = request.tool_overrides[:svg]
          return [override.to_s.strip].reject(&:empty?) unless override.nil? || override.to_s.strip.empty?

          SVG_PRIORITY.map(&:to_s)
        end

        def png_candidates
          override = request.tool_overrides[:png]
          return [override.to_s.strip].reject(&:empty?) unless override.nil? || override.to_s.strip.empty?

          PNG_PRIORITY.map(&:to_s)
        end

        def missing_tool_message(kind, record, priority, fallback_formats:, attribute:)
          tried = if priority.include?(record.id)
            priority.map(&:to_s)
          else
            ([record.id.to_s] + priority.map(&:to_s)).uniq
          end

          base = "Required #{kind} conversion tool not available. Tried: #{tried.join(", ")}. Configure #{attribute} with an executable path or install one of the supported tools."

          preferred = record.id.to_s
          alternates = (priority.map(&:to_s) - [preferred]).uniq
          alt_hint = if alternates.empty?
            "install #{preferred} (preferred)"
          else
            available_alternates = alternates.select { |name| tool_presence_map[name.to_sym] }
            if available_alternates.any?
              "install #{preferred} (preferred) or keep using #{available_alternates.join("/")}"
            else
              "install #{preferred} (preferred) or install #{alternates.join("/")}"
            end
          end

          hints = [alt_hint, "set :latexmath-format: #{fallback_formats}"]
          "#{base}\nhint: #{hints.join("; ")}"
        end

        def remember_record(record)
          return record unless record

          tool_presence_map[record.id] = record.available
          record
        end

        def summary_record(identifier)
          command = identifier.to_s
          self.class.lookup(identifier, command) do
            path = find_executable(command)
            ToolchainRecord.new(id: identifier, available: !path.nil?, path: path || command)
          end
        end

        def canonical_engine_id(command)
          first = Shellwords.shellsplit(command.to_s).first
          return :pdflatex unless first

          first.downcase.gsub(/[^a-z0-9]+/, "_").to_sym
        rescue ArgumentError
          :pdflatex
        end

        class << self
          def lookup(identifier, command)
            @cache ||= {}
            key = [identifier, command]
            cached = @cache[key]
            return cached if cached

            record = yield
            @cache[key] = record
            record
          end

          def reset!
            @cache = {}
            @summary_emitted = false
          end

          def emit_summary_once
            return if @summary_emitted

            yield
            @summary_emitted = true
          end
        end
      end
    end
  end
end
