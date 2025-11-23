# frozen_string_literal: true

require "asciidoctor-latexmath"

class StatsLogger
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

RSpec.describe "Statistics emission" do
  it "logs a single stats line after conversion" do
    logger = StatsLogger.new
    Asciidoctor::LoggerManager.logger = logger

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath]
          ++++
          e = mc^2
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})
      end
    end

    stats_lines = logger.infos.compact.select { |line| line&.start_with?("latexmath stats:") }
    expect(stats_lines.size).to eq(1)
    expect(stats_lines.first).to match(/renders=1 cache_hits=0/)
  ensure
    Asciidoctor::LoggerManager.logger = nil
  end
end
