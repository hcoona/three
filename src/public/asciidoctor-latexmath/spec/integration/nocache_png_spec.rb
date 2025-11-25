# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "%nocache png rendering" do
  it "renders png without storing cache entries" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath%nocache, format=png, ppi=200]
          ++++
          x^2 + y^2 = z^2
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})

        png = Dir.glob("images/*.png").fetch(0)
        expect(File.exist?(png)).to be(true)
        expect(Dir.exist?(".asciidoctor/latexmath")).to be(false)
      end
    end
  end

  it "rejects invalid ppi values" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath%nocache, format=png, ppi=42]
          ++++
          x^2 + y^2 = z^2
          ++++
        ADOC

        expect {
          convert_with_extension(source, attributes: {"imagesdir" => "images"})
        }.to raise_error(Asciidoctor::Latexmath::InvalidAttributeError)
      end
    end
  end

  it "rejects non-integer ppi values" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath%nocache, format=png, ppi=abc]
          ++++
          x^2 + y^2 = z^2
          ++++
        ADOC

        expect {
          convert_with_extension(source, attributes: {"imagesdir" => "images"})
        }.to raise_error(Asciidoctor::Latexmath::InvalidAttributeError)
      end
    end
  end

  it "raises when document-level ppi is out of range" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :latexmath-format: png
          :latexmath-ppi: 700

          [latexmath]
          ++++
          x^2 + y^2 = z^2
          ++++
        ADOC

        expect {
          convert_with_extension(source, attributes: {"imagesdir" => "images"})
        }.to raise_error(Asciidoctor::Latexmath::InvalidAttributeError)
      end
    end
  end
end
