# frozen_string_literal: true

require "asciidoctor-latexmath"
require "asciidoctor/latexmath/command_runner"

class EngineCapturingRunner < SpecSupport::FakeCommandRunner
  attr_reader :commands

  def initialize(result: Asciidoctor::Latexmath::CommandRunner::Result.new(stdout: "", stderr: "", exit_status: 0, duration: 0.01), fail: false)
    super()
    @commands = []
    @result = result
    @fail = fail
  end

  def run(command, **kwargs)
    @commands << command
    if @fail
      raise Asciidoctor::Latexmath::StageFailureError, "simulated failure"
    end

    simulate_command(command, kwargs.fetch(:chdir, Dir.pwd))
    @result
  end
end

def engine_command?(value)
  name = File.basename(value.to_s)
  name.match?(/(pdf|xe|lua)latex|tectonic/)
end

RSpec.describe "Engine precedence and normalization" do
  before do
    @original_backend = Asciidoctor::Latexmath::CommandRunner.backend
  end

  after do
    Asciidoctor::Latexmath::CommandRunner.backend = @original_backend
  end

  it "applies precedence across element, document, and global scopes" do
    runner = EngineCapturingRunner.new
    Asciidoctor::Latexmath::CommandRunner.backend = runner

    stub_tool_availability(dvisvgm: true, pdf2svg: false)

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :latexmath-engine: xelatex
          :latexmath-xelatex: xelatex --synctex=1
          :xelatex: /usr/bin/xelatex
          :lualatex: lualatex --global
          :latexmath-pdflatex: pdflatex

          [latexmath, engine=xelatex]
          ++++
          a^2
          ++++

          [latexmath, xelatex=xelatex --custom]
          ++++
          b^2
          ++++

          [latexmath, engine=lualatex]
          ++++
          c^2
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})
      end
    end

    engine_commands = runner.commands.select { |command| engine_command?(command.first) }
    expect(engine_commands.size).to eq(3)

    doc_scoped = engine_commands[0]
    expect(doc_scoped.first).to eq("xelatex")
    expect(doc_scoped).to include("--synctex=1")
    expect(doc_scoped).to include("-interaction=nonstopmode")
    expect(doc_scoped).to include("-file-line-error")

    element_scoped = engine_commands[1]
    expect(element_scoped.first).to eq("xelatex")
    expect(element_scoped).to include("--custom")

    global_scoped = engine_commands[2]
    expect(global_scoped.first).to eq("lualatex")
    expect(global_scoped).to include("--global")

    default_offset = engine_commands.size

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath%nocache]
          ++++
          z^2
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images_default"})
      end
    end

    engine_commands = runner.commands.select { |command| engine_command?(command.first) }
    expect(engine_commands.size).to eq(default_offset + 1)
    default_scoped = engine_commands.last
    expect(default_scoped.first).to eq("pdflatex")
    expect(default_scoped).to include("-interaction=nonstopmode")
    expect(default_scoped).to include("-file-line-error")
  end

  it "does not fallback to a different engine when the executable is missing" do
    runner = EngineCapturingRunner.new(fail: true)
    Asciidoctor::Latexmath::CommandRunner.backend = runner

    stub_tool_availability(dvisvgm: true, pdf2svg: false)

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath, engine=xelatex, xelatex=/nonexistent/xelatex, on-error=abort]
          ++++
          x
          ++++
        ADOC

        expect {
          convert_with_extension(source, attributes: {"imagesdir" => "images"})
        }.to raise_error(Asciidoctor::Latexmath::StageFailureError)
      end
    end

    expect(runner.commands.size).to eq(1)
    expect(runner.commands.first.first).to eq("/nonexistent/xelatex")
  end
end
