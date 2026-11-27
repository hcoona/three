# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

module Asciidoctor
  module Latexmath
    # Base error class for all `asciidoctor-latexmath` failures.
    class LatexmathError < StandardError; end

    # Raised when an unsupported output format is requested.
    class UnsupportedFormatError < LatexmathError; end

    # Raised when a required external rendering or conversion tool cannot be found.
    class MissingToolError < LatexmathError
      # @return [String] missing executable or tool identifier
      attr_reader :tool

      # @param tool [String] missing executable or tool identifier
      # @param message [String, nil] optional custom error message
      def initialize(tool, message = nil)
        @tool = tool
        super(message || "Missing required tool: #{tool}")
      end
    end

    # Raised when a document or block attribute contains an invalid value.
    class InvalidAttributeError < LatexmathError; end

    # Raised when an attribute value is outside the supported set or range.
    class UnsupportedValueError < InvalidAttributeError
      # @return [String, Symbol] attribute category
      attr_reader :category
      # @return [String, Symbol, nil] attribute name
      attr_reader :subject
      # @return [Object] rejected value
      attr_reader :value
      # @return [Array<String>, String] supported values description
      attr_reader :supported
      # @return [Array<String>] remediation hints
      attr_reader :hints

      # @param category [String, Symbol] attribute category
      # @param subject [String, Symbol, nil] attribute name
      # @param value [Object] rejected value
      # @param supported [Array<String>, String] supported values
      # @param hint [String, Array<String>, nil] optional remediation hint(s)
      def initialize(category:, subject:, value:, supported:, hint: nil)
        @category = category
        @subject = subject
        @value = value
        @supported = supported
        @hints = Array(hint).compact
        super(build_message)
      end

      private

      def build_message
        descriptor = subject ? "#{subject}=#{present_value}" : present_value
        base = "unsupported #{category}: '#{descriptor}' (supported: #{formatted_supported})"
        hints.empty? ? base : "#{base}\nhint: #{formatted_hints}"
      end

      def present_value
        (value.nil? || value.to_s.empty?) ? "<empty>" : value.to_s
      end

      def formatted_supported
        case supported
        when Array
          values = supported.map { |entry| entry.to_s.strip }.reject(&:empty?)
          values.sort_by { |entry| entry.downcase }.join(", ")
        else
          supported.to_s
        end
      end

      def formatted_hints
        hints.map(&:to_s).reject(&:empty?).join("; ")
      end
    end

    # Raised when two distinct formulas attempt to write to the same explicit target path.
    class TargetConflictError < LatexmathError; end

    # Raised when a rendering command exceeds the configured timeout.
    class RenderTimeoutError < LatexmathError; end

    # Raised when a LaTeX compilation or format conversion subprocess exits non-zero.
    class StageFailureError < LatexmathError; end

    # Error-handling policies (`:log` placeholder vs `:abort` exception) and HTML placeholder builder.
    module ErrorHandling
      # Represents a resolved `latexmath-on-error` policy.
      class Policy
        # @return [Symbol] `:abort` or `:log`
        attr_reader :mode

        # @param mode [Symbol] `:abort` or `:log`
        def initialize(mode)
          @mode = mode
        end

        # @return [Boolean] true when rendering errors raise immediately
        def abort?
          mode == :abort
        end

        # @return [Boolean] true when rendering errors log and emit an HTML placeholder
        def log?
          mode == :log
        end

        alias_method :abort, :abort?
        alias_method :log, :log?
      end

      SUPPORTED_POLICIES = {
        abort: Policy.new(:abort),
        log: Policy.new(:log)
      }.freeze

      # Resolves a policy by name.
      #
      # @param name [String, Symbol] `:abort` or `:log`
      # @return [Policy]
      # @raise [ArgumentError] if `name` is unsupported
      def self.policy(name)
        SUPPORTED_POLICIES.fetch(name.to_sym) do
          raise ArgumentError, "Unknown on-error policy: #{name}"
        end
      end

      module Placeholder
        HTML_HEADER = %(<pre class="highlight latexmath-error" role="note" data-latex-error="1">)
        HTML_FOOTER = "</pre>"

        def self.render(message:, command:, stdout:, stderr:, source:, latex_source:)
          body = [
            "Error: #{message}",
            "Command: #{command}",
            "Stdout: #{present_or_placeholder(stdout)}",
            "Stderr: #{present_or_placeholder(stderr)}",
            "Source (AsciiDoc): #{source}",
            "Source (LaTeX): #{latex_source}"
          ].join("\n")

          <<~HTML.rstrip
            #{HTML_HEADER}
            #{escape_html(body)}
            #{HTML_FOOTER}
          HTML
        end

        def self.present_or_placeholder(value)
          return "<empty>" if value.nil? || value.to_s.empty?

          value
        end

        def self.escape_html(text)
          text.to_s.gsub("&", "&amp;").gsub("<", "&lt;").gsub(">", "&gt;")
        end
        private_class_method :escape_html, :present_or_placeholder
      end
    end
  end
end
