# frozen_string_literal: true

require "json"
require "asciidoctor-latexmath"

RSpec.describe "Engine selection precedence" do
  it "applies block override over document and global attributes" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          :pdflatex: xelatex
          :latexmath-pdflatex: lualatex

          [latexmath]
          ++++
          x
          ++++

          [latexmath, pdflatex=tectonic]
          ++++
          y
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})

        metadata = Dir.glob(".asciidoctor/latexmath/**/metadata.json").map do |path|
          JSON.parse(File.read(path))
        end

        engines = metadata.map { |m| m.fetch("engine") }
        expect(engines).to include("lualatex")
        expect(engines).to include("tectonic")
      end
    end
  end
end
