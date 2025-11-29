# frozen_string_literal: true

require "spec_helper"
require "asciidoctor-latexmath"
require "asciidoctor/latexmath/command_runner"

RSpec.describe "Security and timeout handling" do
  before do
    @original_backend = Asciidoctor::Latexmath::CommandRunner.backend
  end

  after do
    Asciidoctor::Latexmath::CommandRunner.backend = @original_backend
  end

  it "strips shell-escape flags and enforces safe defaults" do
    capturing_runner = Class.new do
      attr_reader :commands

      def initialize
        @commands = []
      end

      def run(command, **_kwargs)
        @commands << command
        Asciidoctor::Latexmath::CommandRunner::Result.new(stdout: "", stderr: "", exit_status: 0, duration: 0.01)
      end
    end.new

    Asciidoctor::Latexmath::CommandRunner.backend = capturing_runner

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath, pdflatex="pdflatex -shell-escape -interaction=batchmode -output-directory=/tmp"]
          ++++
          x^2
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})
      end
    end

    recorded = capturing_runner.commands.first
    expect(recorded).not_to include("-shell-escape")
    expect(recorded).to include("-no-shell-escape")
    expect(recorded).to include("-interaction=nonstopmode")
    expect(recorded).to include("-halt-on-error")

    output_dir_index = recorded.index("-output-directory")
    expect(output_dir_index).not_to be_nil
    expect(recorded[output_dir_index + 1]).to match(/latexmath/)
  end

  it "raises RenderTimeoutError when the command runner times out" do
    timeout_runner = Class.new do
      def run(_command, **_kwargs)
        raise Asciidoctor::Latexmath::RenderTimeoutError, "execution timed out"
      end
    end.new

    Asciidoctor::Latexmath::CommandRunner.backend = timeout_runner

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath, on-error=abort]
          ++++
          x^2
          ++++
        ADOC

        expect {
          convert_with_extension(source, attributes: {"imagesdir" => "images"})
        }.to raise_error(Asciidoctor::Latexmath::RenderTimeoutError)
      end
    end
  end

  it "renders placeholder when timeout occurs under on-error=log" do
    timeout_runner = Class.new do
      def run(_command, **_kwargs)
        raise Asciidoctor::Latexmath::RenderTimeoutError, "execution timed out"
      end
    end.new

    Asciidoctor::Latexmath::CommandRunner.backend = timeout_runner

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath, on-error=log]
          ++++
          x^2
          ++++
        ADOC

        html = convert_with_extension(source, attributes: {"imagesdir" => "images"})

        expect(html).to include("Error: execution timed out")
      end
    end
  end
end
