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
      class PdfToPngRenderer < Renderer
        def name
          "pdf_to_png"
        end

        def render(request, context)
          previous = context.respond_to?(:[]) ? context[:previous_output] : nil
          return previous if request.format != :png && previous

          pdf_path = select_pdf_path(previous, context)
          return pdf_path unless request.format == :png

          record = context.fetch(:tool_detector).ensure_png_tool!

          artifact_dir = context.fetch(:artifact_dir)
          FileUtils.mkdir_p(artifact_dir)
          output_path = File.join(artifact_dir, "#{context.fetch(:artifact_basename)}.png")

          command = build_command(record, pdf_path, output_path, request)
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
            fallback = detect_fallback_png(output_path)
            if fallback
              FileUtils.mv(fallback, output_path)
            else
              raise StageFailureError, "PNG conversion did not produce expected output: #{output_path}"
            end
          end

          output_path
        end

        private

        def select_pdf_path(previous, context)
          if previous && File.extname(previous.to_s).casecmp(".pdf").zero?
            previous
          else
            File.join(context.fetch(:tmp_dir), "#{context.fetch(:artifact_basename)}.pdf")
          end
        end

        def build_command(record, pdf_path, output_path, request)
          case record.id
          when :pdftoppm
            prefix = output_path.sub(/\.png\z/, "")
            [record.path, "-png", "-singlefile", "-r", effective_ppi(request).to_s, pdf_path, prefix]
          when :magick
            [record.path, "-density", effective_ppi(request).to_s, pdf_path, "-quality", "90", output_path]
          when :gs
            [record.path, "-dSAFER", "-dBATCH", "-dNOPAUSE", "-sDEVICE=pngalpha", "-r#{effective_ppi(request)}", "-sOutputFile=#{output_path}", pdf_path]
          else
            [record.path, pdf_path, output_path]
          end
        end

        def effective_ppi(request)
          request.ppi || 300
        end

        def detect_fallback_png(expected_path)
          base = expected_path.sub(/\.png\z/, "")
          candidate = "#{base}-1.png"
          return candidate if File.exist?(candidate)

          nil
        end
      end
    end
  end
end
