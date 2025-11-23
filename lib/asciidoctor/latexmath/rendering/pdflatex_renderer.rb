# frozen_string_literal: true

# SPDX-FileCopyrightText: 2025 Shuai Zhang
#
# SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

require "fileutils"
require "shellwords"

require_relative "renderer"
require_relative "../command_runner"
require_relative "../errors"

module Asciidoctor
  module Latexmath
    module Rendering
      class PdflatexRenderer < Renderer
        DEFAULT_FLAGS = ["-interaction=nonstopmode", "-halt-on-error", "-file-line-error"].freeze
        SECURE_FLAG = "-no-shell-escape"
        DISALLOWED_FLAGS = %w[-shell-escape --shell-escape -enable-write18 --enable-write18].freeze

        def name
          "pdflatex"
        end

        def render(request, context)
          tmp_dir = context.fetch(:tmp_dir)
          FileUtils.mkdir_p(tmp_dir)

          tex_path = prepare_tex_source(context.fetch(:tex_artifact_path), tmp_dir, context.fetch(:artifact_basename))
          command = build_command(request, tex_path, tmp_dir)

          result = CommandRunner.run(command, timeout: request.timeout, chdir: tmp_dir, env: sanitized_env)
          unless result.exit_status.zero?
            message = <<~MSG.strip
              pdflatex exited with status #{result.exit_status}
              command: #{command.join(" ")}
              stdout: #{truncate_output(result.stdout)}
              stderr: #{truncate_output(result.stderr)}
            MSG
            raise StageFailureError, message
          end

          pdf_path = pdf_path_for(context)
          unless File.exist?(pdf_path)
            raise StageFailureError, "pdflatex did not produce expected output: #{pdf_path}"
          end

          pdf_path
        rescue RenderTimeoutError
          raise
        rescue => error
          raise StageFailureError, error.message
        end

        private

        def pdf_path_for(context)
          File.join(context.fetch(:tmp_dir), "#{context.fetch(:artifact_basename)}.pdf")
        end

        def prepare_tex_source(artifact_path, tmp_dir, basename)
          filename = "#{basename}.tex"
          destination = File.join(tmp_dir, filename)
          FileUtils.cp(artifact_path, destination)
          destination
        end

        def build_command(request, tex_path, tmp_dir)
          tokens = Shellwords.shellsplit(request.engine.to_s)
          raise StageFailureError, "pdflatex executable not specified" if tokens.empty?

          executable = tokens.shift
          engine = identify_engine(executable)
          sanitized_flags = tokens.reject { |flag| disallowed_flag?(flag) }

          if %i[pdflatex xelatex lualatex].include?(engine)
            DEFAULT_FLAGS.each do |flag|
              sanitized_flags.reject! { |existing| equivalent_flag?(existing, flag) }
              sanitized_flags << flag
            end

            sanitized_flags.reject! { |existing| equivalent_flag?(existing, SECURE_FLAG) }
            sanitized_flags << SECURE_FLAG

            sanitized_flags = remove_output_directory_flags(sanitized_flags)
            sanitized_flags << "-output-directory" << tmp_dir
          elsif engine == :tectonic
            sanitized_flags = remove_outdir_flags(sanitized_flags)
            sanitized_flags << "--outdir" << tmp_dir
          else
            sanitized_flags = remove_output_directory_flags(sanitized_flags)
            sanitized_flags << "-output-directory" << tmp_dir
          end

          [executable, *sanitized_flags, tex_path]
        end

        def sanitized_env
          {
            "openout_any" => "p",
            "shell_escape" => "f"
          }
        end

        def identify_engine(executable)
          File.basename(executable.to_s).downcase.gsub(/[^a-z0-9]+/, "_").to_sym
        end

        def disallowed_flag?(flag)
          base = flag.split("=").first
          DISALLOWED_FLAGS.include?(base)
        end

        def equivalent_flag?(existing, desired)
          base_existing = existing.split("=").first
          base_desired = desired.split("=").first
          base_existing == base_desired
        end

        def remove_output_directory_flags(flags)
          result = []
          skip_next = false

          flags.each do |flag|
            if skip_next
              skip_next = false
              next
            end

            base = flag.split("=").first
            if base == "-output-directory"
              skip_next = !flag.include?("=")
              next
            end

            result << flag
          end

          result
        end

        def remove_outdir_flags(flags)
          result = []
          skip_next = false

          flags.each do |flag|
            if skip_next
              skip_next = false
              next
            end

            if flag.start_with?("--outdir")
              skip_next = !flag.include?("=")
              next
            end

            result << flag
          end

          result
        end
      end
    end
  end
end
