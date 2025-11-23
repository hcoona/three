# frozen_string_literal: true

require "asciidoctor-latexmath"

class SvgToolLogger
  attr_reader :infos

  def initialize
    @infos = []
  end

  def info(message = nil, &block)
    @infos << (message || block&.call)
  end

  def warn(*)
    nil
  end

  def error(*)
    nil
  end

  def debug(*)
    nil
  end
end

RSpec.describe "SVG tool priority" do
  it "prefers dvisvgm when available and logs selection" do
    logger = SvgToolLogger.new
    original_logger = Asciidoctor::LoggerManager.logger
    Asciidoctor::LoggerManager.logger = logger

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        stub_tool_availability(dvisvgm: true, pdf2svg: true)

        source = <<~ADOC
          [latexmath]
          ++++
          x
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})
      end
    end

    tool_line = logger.infos.compact.find { |line| line&.start_with?("latexmath.svg.tool=") }
    expect(tool_line).to eq("latexmath.svg.tool=dvisvgm")
  ensure
    Asciidoctor::LoggerManager.logger = original_logger
  end

  it "falls back to pdf2svg when dvisvgm is unavailable" do
    logger = SvgToolLogger.new
    original_logger = Asciidoctor::LoggerManager.logger
    Asciidoctor::LoggerManager.logger = logger

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        stub_tool_availability(dvisvgm: false, pdf2svg: true)

        source = <<~ADOC
          [latexmath]
          ++++
          y
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})
      end
    end

    tool_line = logger.infos.compact.find { |line| line&.start_with?("latexmath.svg.tool=") }
    expect(tool_line).to eq("latexmath.svg.tool=pdf2svg")
  ensure
    Asciidoctor::LoggerManager.logger = original_logger
  end

  it "logs missing and raises when no svg tool available" do
    logger = SvgToolLogger.new
    original_logger = Asciidoctor::LoggerManager.logger
    Asciidoctor::LoggerManager.logger = logger

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        stub_tool_availability(dvisvgm: false, pdf2svg: false)

        source = <<~ADOC
          [latexmath]
          ++++
          z
          ++++
        ADOC

        expect {
          convert_with_extension(source, attributes: {"imagesdir" => "images"})
        }.to raise_error(Asciidoctor::Latexmath::MissingToolError)
      end
    end

    tool_line = logger.infos.compact.find { |line| line&.start_with?("latexmath.svg.tool=") }
    expect(tool_line).to eq("latexmath.svg.tool=missing")
  ensure
    Asciidoctor::LoggerManager.logger = original_logger
  end
end
