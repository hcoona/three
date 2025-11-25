# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe Asciidoctor::Latexmath::Rendering::Pipeline do
  describe ".default_stage_identifiers" do
    it "returns the canonical ordered list of stage identifiers" do
      expect(described_class.default_stage_identifiers)
        .to eq(%i[pdflatex pdf_to_svg pdf_to_png])
    end
  end

  describe ".signature" do
    it "returns a stable signature digest for the default pipeline" do
      expect(described_class.signature)
        .to eq("pdflatex|pdf_to_svg|pdf_to_png")
    end
  end

  describe "#execute" do
    it "invokes stages sequentially and returns the final path" do
      calls = []
      stages = [
        instance_double("Renderer", name: "pdflatex", render: "/tmp/output.pdf"),
        instance_double("Renderer", name: "pdf_to_svg", render: "/tmp/output.svg"),
        instance_double("Renderer", name: "pdf_to_png", render: "/tmp/output.png")
      ]

      stages.each_with_index do |stage, index|
        allow(stage).to receive(:render) do |request, context|
          calls << [stage.name, request, context, index]
          ["/tmp/output.pdf", "/tmp/output.svg", "/tmp/output.png"][index]
        end
      end

      pipeline = described_class.new(stages)
      final_path = pipeline.execute(instance_double("RenderRequest"), instance_double("Context"))

      expect(final_path).to eq("/tmp/output.png")
      expect(calls.map(&:first)).to eq(%w[pdflatex pdf_to_svg pdf_to_png])
    end

    it "stops execution at the first failing stage" do
      stages = [
        instance_double("Renderer", name: "pdflatex"),
        instance_double("Renderer", name: "pdf_to_svg")
      ]

      allow(stages.first).to receive(:render).and_return("/tmp/output.pdf")
      allow(stages.last).to receive(:render).and_raise(Asciidoctor::Latexmath::RenderTimeoutError)

      pipeline = described_class.new(stages)

      expect {
        pipeline.execute(instance_double("RenderRequest"), instance_double("Context"))
      }.to raise_error(Asciidoctor::Latexmath::RenderTimeoutError)
    end
  end
end
