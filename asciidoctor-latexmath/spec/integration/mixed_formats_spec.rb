# frozen_string_literal: true

require "json"
require "fileutils"
require "asciidoctor-latexmath"

RSpec.describe "Mixed format rendering" do
  it "renders svg, png, and pdf outputs independently with separate cache entries" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        stub_tool_availability(dvisvgm: true, pdftoppm: true)
        Asciidoctor::Latexmath.reset_render_counters!

        source = <<~ADOC
          [latexmath]
          ++++
          x^2 + y^2 = z^2
          ++++

          [latexmath, format=png]
          ++++
          x^2 + y^2 = z^2
          ++++

          [latexmath, format=pdf]
          ++++
          x^2 + y^2 = z^2
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})

        expect(Asciidoctor::Latexmath.render_invocations).to eq(3)

        svg_files = Dir.glob("images/*.svg")
        png_files = Dir.glob("images/*.png")
        pdf_files = Dir.glob("images/*.pdf")

        expect(svg_files.size).to eq(1)
        expect(png_files.size).to eq(1)
        expect(pdf_files.size).to eq(1)

        base_name = File.basename(svg_files.first, ".svg")
        expect(File.basename(png_files.first, ".png")).to eq(base_name)
        expect(File.basename(pdf_files.first, ".pdf")).to eq(base_name)

        cache_entries = Dir.glob(".asciidoctor/latexmath/*").select do |path|
          File.directory?(path) && File.exist?(File.join(path, "metadata.json"))
        end
        expect(cache_entries.size).to eq(3)

        formats = cache_entries.map do |path|
          metadata = JSON.parse(File.read(File.join(path, "metadata.json")))
          metadata.fetch("format")
        end
        expect(formats).to contain_exactly("svg", "png", "pdf")

        FileUtils.rm_rf("images")
        convert_with_extension(source, attributes: {"imagesdir" => "images"})

        expect(Asciidoctor::Latexmath.render_invocations).to eq(3)

        svg_files = Dir.glob("images/*.svg")
        png_files = Dir.glob("images/*.png")
        pdf_files = Dir.glob("images/*.pdf")

        expect(svg_files.size).to eq(1)
        expect(png_files.size).to eq(1)
        expect(pdf_files.size).to eq(1)
        expect(File.basename(svg_files.first, ".svg")).to eq(base_name)
        expect(File.basename(png_files.first, ".png")).to eq(base_name)
        expect(File.basename(pdf_files.first, ".pdf")).to eq(base_name)

        Asciidoctor::Latexmath.reset_render_counters!
      end
    end
  end
end
