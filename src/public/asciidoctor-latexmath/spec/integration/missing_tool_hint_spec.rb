# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Missing tool hint" do
  it "provides actionable hints when required svg tool is unavailable" do
    stub_tool_availability(dvisvgm: false, pdf2svg: false)

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath]
          ++++
          x + y
          ++++
        ADOC

        expect {
          convert_with_extension(source, attributes: {"imagesdir" => "images"})
        }.to raise_error(Asciidoctor::Latexmath::MissingToolError) { |error|
          expect(error.message).to include("hint:")
          expect(error.message).to include("install dvisvgm")
          expect(error.message).to include("set :latexmath-format: pdf|png")
        }
      end
    end
  end
end
