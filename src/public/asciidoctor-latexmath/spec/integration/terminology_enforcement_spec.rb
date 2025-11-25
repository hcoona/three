# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Terminology enforcement" do
  it "logs the cache-dir alias only once and normalizes attribute keys" do
    logger = CapturingLogger.new
    Asciidoctor::LoggerManager.logger = logger
    stub_tool_availability(dvisvgm: true, pdftoppm: true)

    captured_attributes = []
    captured_cachedirs = []

    allow_any_instance_of(Asciidoctor::Latexmath::AttributeResolver).to receive(:resolve).and_wrap_original do |original, *args, **kwargs|
      resolved = original.call(*args, **kwargs)
      captured_attributes << resolved.raw_attributes.dup
      captured_cachedirs << resolved.render_request.cachedir
      resolved
    end

    expected_cachedirs = nil

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        expected_cachedirs = ["legacy-one", "legacy-two"].map { |path| File.expand_path(path, dir) }

        source = <<~ADOC
          :latexmath-cache-dir: doc-cache

          [latexmath, cache-dir=legacy-one]
          ++++
          x
          ++++

          [latexmath, cache-dir=legacy-two]
          ++++
          y
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})
      end
    end

    deprecation_messages = logger.messages.select { |(_, message)| message&.include?("cache-dir is deprecated") }
    expect(deprecation_messages.size).to eq(1)

    expect(captured_attributes).not_to be_empty
    captured_attributes.each do |attrs|
      expect(attrs).not_to have_key("cache-dir")
    end

    expect(captured_attributes.any? { |attrs| attrs.key?("cachedir") }).to be(true)

    expect(expected_cachedirs).not_to be_nil
    expected_cachedirs.each do |expected|
      expect(captured_cachedirs).to include(expected)
    end
  ensure
    Asciidoctor::LoggerManager.logger = nil
  end
end
