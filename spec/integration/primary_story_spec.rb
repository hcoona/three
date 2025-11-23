# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Primary story end-to-end" do
  it "renders latexmath blocks and inline macros to SVG and reuses cache on second run" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          = Sample
          :stem: latexmath

          [latexmath]
          ++++
          E = mc^2
          ++++

          Inline example latexmath:[a^2 + b^2 = c^2].
        ADOC

        html_first = convert_with_extension(source, attributes: {"imagesdir" => "images"})

        svg_path = Dir.glob("images/*.svg").fetch(0)
        cache_entries = Dir.glob(".asciidoctor/latexmath/**/*").select { |p| File.file?(p) }
        expect(svg_path).not_to be_nil
        expect(cache_entries).not_to be_empty
        expect(html_first).to include(svg_path)

        mtime = File.mtime(svg_path)
        sleep 1

        html_second = convert_with_extension(source, attributes: {"imagesdir" => "images"})
        expect(html_second).to include(svg_path)
        expect(File.mtime(svg_path)).to eq(mtime)
      end
    end
  end
end
