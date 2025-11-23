# frozen_string_literal: true

require "asciidoctor"
require "asciidoctor-latexmath"

class StatsMemoryLogger
  attr_reader :messages

  def initialize
    @messages = []
  end

  def info(message = nil, &block)
    log(:info, message, &block)
  end

  def warn(message = nil, &block)
    log(:warn, message, &block)
  end

  def error(message = nil, &block)
    log(:error, message, &block)
  end

  def debug(message = nil, &block)
    log(:debug, message, &block)
  end

  private

  def log(severity, message, &block)
    payload = if !message.nil?
      message
    elsif block
      block.call
    end
    @messages << {severity: severity, message: payload}
  end
end

RSpec.describe "Statistics logging" do
  let(:memory_logger) { StatsMemoryLogger.new }
  let(:original_logger) { Asciidoctor::LoggerManager.logger }

  before do
    Asciidoctor::LoggerManager.logger = memory_logger
  end

  after do
    Asciidoctor::LoggerManager.logger = original_logger
  end

  it "emits a single stats line after rendering expressions" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        Asciidoctor::Latexmath.reset_render_counters!
        source = <<~ADOC
          [latexmath]
          ++++
          x^2 + y^2 = z^2
          ++++

          Inline latexmath:[a^2 + b^2 = c^2]
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})

        stats_lines = memory_logger.messages.select do |msg|
          msg[:severity] == :info && msg[:message].to_s.start_with?("latexmath stats:")
        end
        expect(stats_lines.length).to eq(1)
        expect(stats_lines.first[:message]).to match(/latexmath stats: renders=\d+ cache_hits=\d+ avg_render_ms=\d+ avg_hit_ms=\d+/)
      end
    end
  end

  it "does not emit stats when no expressions were processed" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          = Title

          This document has no latexmath blocks.
        ADOC

        convert_with_extension(source)

        stats_lines = memory_logger.messages.select do |msg|
          msg[:severity] == :info && msg[:message].to_s.start_with?("latexmath stats:")
        end
        expect(stats_lines).to be_empty
      end
    end
  end
end
