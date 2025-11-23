# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Font size configuration" do
  it "uses 12pt by default" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        stub_tool_availability(dvisvgm: true, pdftoppm: true)

        source = <<~ADOC
          :latexmath-keep-artifacts: true
          :latexmath-artifacts-dir: artifacts

          [latexmath,default-size]
          ++++
          a^2
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})

        tex_path = File.join("artifacts", "default-size.tex")
        expect(File.exist?(tex_path)).to be(true)
        tex_body = File.read(tex_path)
        expect(tex_body).to include("\\documentclass[preview,border=2pt,12pt]{standalone}")
      end
    end
  end

  it "allows document and element overrides" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        stub_tool_availability(dvisvgm: true, pdftoppm: true)

        source = <<~ADOC
          :latexmath-keep-artifacts: true
          :latexmath-artifacts-dir: artifacts
          :latexmath-fontsize: 10pt

          [latexmath,doc-size]
          ++++
          b^2
          ++++

          [latexmath,override-size, fontsize=18pt]
          ++++
          c^2
          ++++
        ADOC

        convert_with_extension(source, attributes: {"imagesdir" => "images"})

        doc_tex = File.read(File.join("artifacts", "doc-size.tex"))
        override_tex = File.read(File.join("artifacts", "override-size.tex"))

        expect(doc_tex).to include("\\documentclass[preview,border=2pt,10pt]{standalone}")
        expect(override_tex).to include("\\documentclass[preview,border=2pt,18pt]{standalone}")
      end
    end
  end

  it "rejects invalid font size values" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        stub_tool_availability(dvisvgm: true, pdftoppm: true)

        source = <<~ADOC
          [latexmath, fontsize=large]
          ++++
          d^2
          ++++
        ADOC

        expect {
          convert_with_extension(source, attributes: {"imagesdir" => "images"})
        }.to raise_error(Asciidoctor::Latexmath::UnsupportedValueError)
      end
    end
  end
end
