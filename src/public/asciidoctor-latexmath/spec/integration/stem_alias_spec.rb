# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Stem alias equivalence" do
  it "renders stem:[...] and latexmath:[...] using the same cache entry" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :stem: latexmath

          stem:[x]
          latexmath:[x]
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})

        expect(Dir.glob("images/*.svg").size).to eq(1)
        expect(Dir.glob(".asciidoctor/latexmath/**/*.json").size).to eq(1)
      end
    end
  end
end
