# frozen_string_literal: true

require "asciidoctor-latexmath"

class ToolSummaryLogger
  attr_reader :messages

  def initialize
    @messages = []
  end

  def info(message = nil, &block)
    append(:info, message, &block)
  end

  def warn(message = nil, &block)
    append(:warn, message, &block)
  end

  def error(message = nil, &block)
    append(:error, message, &block)
  end

  def debug(message = nil, &block)
    append(:debug, message, &block)
  end

  private

  def append(level, message, &block)
    text = message
    text = block.call if message.nil? && block
    @messages << [level, text]
  end
end

RSpec.describe "Tool summary log" do
  around do |example|
    original_logger = Asciidoctor::LoggerManager.logger
    logger = ToolSummaryLogger.new
    Asciidoctor::LoggerManager.logger = logger
    example.run
  ensure
    Asciidoctor::LoggerManager.logger = original_logger
  end

  it "emits a single tools summary line with fixed ordering" do
    availability = {
      dvisvgm: true,
      pdf2svg: false,
      pdflatex: true,
      xelatex: false,
      lualatex: false,
      tectonic: false,
      pdftoppm: true,
      magick: false,
      gs: false
    }

    allow(Asciidoctor::Latexmath::Rendering::ToolDetector).to receive(:lookup).and_wrap_original do |original, identifier, command, &block|
      if availability.key?(identifier)
        Asciidoctor::Latexmath::Rendering::ToolchainRecord.new(
          id: identifier,
          available: availability.fetch(identifier),
          path: "/usr/bin/#{identifier}"
        )
      else
        original.call(identifier, command, &block)
      end
    end

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        Asciidoctor::Latexmath::Rendering::ToolDetector.reset!

        source = <<~ADOC
          [latexmath]
          ++++
          x
          ++++

          [latexmath]
          ++++
          y
          ++++
        ADOC

        2.times do
          convert_with_extension(source, attributes: {"imagesdir" => "images"})
        end

        logger = Asciidoctor::LoggerManager.logger
        summary_messages = logger.messages.select { |level, message| level == :info && message&.start_with?("latexmath.tools:") }
        expect(summary_messages.size).to eq(1)
        expect(summary_messages.first.last).to eq(
          "latexmath.tools: dvisvgm=ok pdf2svg=missing pdflatex=ok xelatex=missing lualatex=missing tectonic=missing pdftoppm=ok magick=missing gs=missing"
        )
      end
    end
  end
end
