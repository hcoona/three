# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Pipeline stage immutability" do
  it "keeps the canonical stage order even when required svg tools are missing" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        stub_tool_availability(dvisvgm: false, pdf2svg: false, pdftoppm: true)
        signature_before = Asciidoctor::Latexmath::Rendering::Pipeline.signature
        identifiers_before = Asciidoctor::Latexmath::Rendering::Pipeline.default_stage_identifiers.dup

        captured_stage_names = nil
        allow(Asciidoctor::Latexmath::Rendering::Pipeline).to receive(:new).and_wrap_original do |original, stages|
          captured_stage_names = stages.map(&:name)
          original.call(stages)
        end

        source = <<~ADOC
          [latexmath, format=svg]
          ++++
          x
          ++++
        ADOC

        expect {
          convert_with_extension(source, attributes: {"imagesdir" => "images"})
        }.to raise_error(Asciidoctor::Latexmath::MissingToolError)

        expect(captured_stage_names).to eq(identifiers_before.map(&:to_s))
        expect(Asciidoctor::Latexmath::Rendering::Pipeline.signature).to eq(signature_before)
        expect(Asciidoctor::Latexmath::Rendering::Pipeline.default_stage_identifiers).to eq(identifiers_before)
      end
    end
  end
end
