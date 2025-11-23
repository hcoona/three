# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require "fileutils"

require_relative "renderer"
require_relative "tool_detector"
require_relative "../command_runner"
require_relative "../errors"

module Asciidoctor
  module Latexmath
    module Rendering
      class PdfToSvgRenderer < Renderer
        def name
          "pdf_to_svg"
        end

        def render(request, context)
          previous = context.respond_to?(:[]) ? context[:previous_output] : nil
          pdf_path = previous || pdf_path_for(context)
          return previous if request.format != :svg && previous
          return pdf_path unless request.format == :svg

          # `ensure_svg_tool!` raises MissingToolError when no converter is available;
          # allow it to bubble so upstream handling can surface detailed hints.
          record = context.fetch(:tool_detector).ensure_svg_tool!
          artifact_dir = context.fetch(:artifact_dir)
          FileUtils.mkdir_p(artifact_dir)
          output_path = File.join(artifact_dir, "#{context.fetch(:artifact_basename)}.svg")

          command = build_command(record, pdf_path, output_path)
          result = CommandRunner.run(command, timeout: request.timeout, chdir: File.dirname(pdf_path), env: {})
          unless result.exit_status.zero?
            message = <<~MSG.strip
              #{record.id} exited with status #{result.exit_status}
              command: #{command.join(" ")}
              stdout: #{truncate_output(result.stdout)}
              stderr: #{truncate_output(result.stderr)}
            MSG
            raise StageFailureError, message
          end

          unless File.exist?(output_path)
            fallback = detect_fallback_svg(output_path)
            if fallback
              FileUtils.mv(fallback, output_path)
            else
              raise StageFailureError, "SVG conversion did not produce expected output: #{output_path}"
            end
          end

          output_path
        end

        private

        def pdf_path_for(context)
          File.join(context.fetch(:tmp_dir), "#{context.fetch(:artifact_basename)}.pdf")
        end

        def build_command(record, pdf_path, output_path)
          case record.id
          when :dvisvgm
            [record.path, "--pdf", "--page=1", "-n", "-o", output_path, pdf_path]
          else
            [record.path, pdf_path, output_path]
          end
        end

        def detect_fallback_svg(expected_path)
          base = expected_path.sub(/\.svg\z/, "")
          candidate = "#{base}-1.svg"
          return candidate if File.exist?(candidate)

          nil
        end
      end
    end
  end
end
