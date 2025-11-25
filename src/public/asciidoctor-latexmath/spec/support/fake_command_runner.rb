# frozen_string_literal: true

require "fileutils"
require "asciidoctor/latexmath/command_runner"

module SpecSupport
  class FakeCommandRunner
    attr_reader :commands

    def initialize
      @commands = []
    end

    def run(command, timeout:, chdir:, env: {}, stdin: nil)
      @commands << command

      simulate_command(command, chdir)

      Asciidoctor::Latexmath::CommandRunner::Result.new(
        stdout: "",
        stderr: "",
        exit_status: 0,
        duration: 0.001
      )
    end

    protected

    def simulate_command(command, chdir)
      executable = File.basename(command.first.to_s)

      case executable
      when /pdflatex|xelatex|lualatex|tectonic/
        tex_path = command.last
        basename = File.basename(tex_path.to_s, ".tex")
        output_pdf = File.join(chdir, "#{basename}.pdf")
        FileUtils.mkdir_p(File.dirname(output_pdf))
        File.write(output_pdf, "%PDF-FAKE #{basename}\n")
      when /dvisvgm/
        output_path = extract_option_value(command, "-o") || default_output_path(chdir, ".svg")
        FileUtils.mkdir_p(File.dirname(output_path))
        File.write(output_path, "<svg><!-- fake dvisvgm output --></svg>")
      when /pdf2svg/
        output_path = command[2]
        FileUtils.mkdir_p(File.dirname(output_path))
        File.write(output_path, "<svg><!-- fake pdf2svg output --></svg>")
      when /pdftoppm/
        prefix = command.last
        output_path = "#{prefix}.png"
        FileUtils.mkdir_p(File.dirname(output_path))
        File.write(output_path, "PNGFAKE")
      when /magick/
        output_path = command.last
        FileUtils.mkdir_p(File.dirname(output_path))
        File.write(output_path, "PNGFAKE")
      when /^gs$/
        output_arg = command.find { |arg| arg.start_with?("-sOutputFile=") }
        output_path = output_arg ? output_arg.split("=", 2).last : default_output_path(chdir, ".png")
        FileUtils.mkdir_p(File.dirname(output_path))
        File.write(output_path, "PNGFAKE")
      end
    end

    private

    def extract_option_value(command, flag)
      index = command.index(flag)
      return nil unless index

      command[index + 1]
    end

    def default_output_path(chdir, extension)
      File.join(chdir, "output#{extension}")
    end
  end
end
