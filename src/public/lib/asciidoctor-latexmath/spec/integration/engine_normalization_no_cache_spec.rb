# frozen_string_literal: true

require "asciidoctor-latexmath"

class RecordingBackend
  attr_reader :invocations

  def initialize
    @invocations = []
  end

  def run(command, timeout:, chdir:, env: {}, stdin: nil)
    @invocations << command.dup
    Asciidoctor::Latexmath::CommandRunner::Result.new(stdout: "", stderr: "", exit_status: 0, duration: 0.01)
  end
end

RSpec.describe "Engine normalization with nocache" do
  it "ensures required pdflatex flags are present exactly once" do
    backend = RecordingBackend.new

    stub_tool_availability(dvisvgm: true)

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :latexmath-pdflatex: pdflatex -interaction=nonstopmode -halt-on-error

          [latexmath%nocache]
          ++++
          x^2 + y^2
          ++++

          [latexmath%nocache, pdflatex="pdflatex -interaction=nonstopmode -halt-on-error -file-line-error -no-shell-escape"]
          ++++
          z^2
          ++++
        ADOC

        Asciidoctor::Latexmath::CommandRunner.with_backend(backend) do
          convert_with_extension(source, attributes: {"imagesdir" => "images"})
        end
      end
    end

    expect(backend.invocations.size).to eq(2)

    required_flags = ["-interaction=nonstopmode", "-halt-on-error", "-file-line-error", "-no-shell-escape"]

    backend.invocations.each do |command|
      required_flags.each do |flag|
        expect(command.count(flag)).to eq(1), "expected #{flag} to appear exactly once in #{command.inspect}"
      end
      expect(command.first).to eq("pdflatex")
      expect(command).to include("-output-directory")
    end
  end
end
