# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Attribute precedence" do
  it "prefers element cachedir over document attribute and defaults" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :latexmath-cachedir: doc-cache

          [latexmath]
          ++++
          A
          ++++

          [latexmath, cachedir=block-cache]
          ++++
          B
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})

        doc_cache_entries = Dir.glob("doc-cache/**/*.json")
        block_cache_entries = Dir.glob("block-cache/**/*.json")

        expect(doc_cache_entries.size).to eq(1)
        expect(block_cache_entries.size).to eq(1)
      end
    end
  end
end
