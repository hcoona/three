# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Windows path compatibility" do
  it "resolves Windows-style separators for imagesdir and cachedir" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :imagesdir: assets\\math
          :latexmath-cachedir: cache\\latex

          [latexmath]
          ++++
          x + y
          ++++

          [latexmath, cachedir=custom\\blocks, format=png, ppi=150]
          ++++
          z
          ++++
        ADOC

        html = convert_with_extension(source)
        expect(html).to include('src="assets/math/')

        svg_files = Dir.glob(File.join("assets", "math", "*.svg"))
        expect(svg_files).not_to be_empty

        png_files = Dir.glob(File.join("assets", "math", "*.png"))
        expect(png_files).not_to be_empty

        default_cache = File.expand_path(File.join("cache", "latex"), Dir.pwd)
        expect(Dir.exist?(default_cache)).to be(true)

        element_cache = File.expand_path(File.join("custom", "blocks"), Dir.pwd)
        expect(Dir.exist?(element_cache)).to be(true)
      end
    end
  end

  it "handles mixed separators for outdir and imagesoutdir" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        FileUtils.mkdir_p("build")

        source = <<~ADOC
          :outdir: build\\docs
          :imagesoutdir: build\\docs\\images

          [latexmath]
          ++++
          a^2 + b^2 = c^2
          ++++
        ADOC

        convert_with_extension(source)

        expected_dir = File.join("build", "docs", "images")
        produced = Dir.glob(File.join(expected_dir, "*.svg"))
        expect(produced).not_to be_empty
      end
    end
  end
end
