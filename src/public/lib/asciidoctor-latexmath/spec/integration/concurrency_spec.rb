# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Concurrency atomicity" do
  it "creates a single artifact when two renders happen concurrently" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath]
          ++++
          x = y
          ++++
        ADOC

        threads = 2.times.map do
          Thread.new do
            convert_with_extension(source, attributes: {"imagesdir" => "images"})
          end
        end
        threads.each(&:join)

        cache_files = Dir.glob(".asciidoctor/latexmath/**/*.json")
        expect(cache_files.size).to eq(1)
        expect(Dir.glob("images/*.svg").size).to eq(1)
      end
    end
  end
end
