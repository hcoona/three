# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Missing tool failure" do
  it "raises a MissingToolError when required svg converter is absent" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath]
          ++++
          E = mc^2
          ++++
        ADOC

        expect {
          convert_with_extension(
            source,
            attributes: {
              "imagesdir" => "images",
              "latexmath-pdf2svg" => "/nonexistent/pdf2svg",
              "latexmath-svg-tool" => "pdf2svg"
            }
          )
        }.to raise_error(Asciidoctor::Latexmath::MissingToolError)
      end
    end
  end
end
