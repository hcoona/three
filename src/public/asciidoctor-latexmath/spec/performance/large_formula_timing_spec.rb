# frozen_string_literal: true

require "asciidoctor-latexmath"
require "digest"

RSpec.describe "Large formula timing" do
  it "logs debug timing lines for large formulas on render and cache hit" do
    logger = CapturingLogger.new
    previous_logger = Asciidoctor::LoggerManager.logger
    Asciidoctor::LoggerManager.logger = logger

    formula_body = "a" * 3105
    digest = Digest::SHA256.hexdigest(formula_body)
    expected_prefix = digest[0, 8]
    expected_bytes = formula_body.bytesize

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :latexmath-cachedir: cache
          :imagesdir: images

          [latexmath]
          ++++
          #{formula_body}
          ++++
        ADOC

        convert_with_extension(source)
        convert_with_extension(source)
      end
    end

    debug_lines = logger.messages.filter_map do |severity, message|
      next unless severity == :debug
      next unless message&.start_with?("latexmath.timing:")

      message
    end

    expect(debug_lines.length).to eq(2)
    debug_lines.each do |line|
      expect(line).to include("key=#{expected_prefix}")
      expect(line).to include("bytes=#{expected_bytes}")
      expect(line).to match(/ms=\d+/)
    end
  ensure
    Asciidoctor::LoggerManager.logger = previous_logger
  end
end
