# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Format variants" do
  it "renders pdf, png, and svg outputs with correct metadata" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :imagesdir: assets

          [latexmath, format=pdf]
          ++++
          E = mc^2
          ++++

          [latexmath, format=png, ppi=144]
          ++++
          \int_0^1 x^2 dx
          ++++

          latexmath:[a + b,format=svg]
        ADOC

        html = convert_with_extension(source)

        expect(html).to include('src="assets/')
        expect(html).to include('.pdf"')
        expect(html).to include('.png"')
        expect(html).to include('.svg"')
        expect(html).to include('role="math"')
        expect(html).to include('data-latex-original="E = mc^2"')

        expect(Dir.glob(File.join("assets", "*.pdf")).size).to eq(1)
        expect(Dir.glob(File.join("assets", "*.png")).size).to eq(1)
        expect(Dir.glob(File.join("assets", "*.svg")).size).to be >= 1
      end
    end
  end
end
