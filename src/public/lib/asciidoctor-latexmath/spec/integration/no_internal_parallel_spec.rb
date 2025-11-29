# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "No internal parallel execution" do
  it "renders multiple expressions sequentially without altering pipeline stages" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        stub_tool_availability(dvisvgm: true, pdftoppm: true)

        guard = {active: false}
        call_sequences = Hash.new { |hash, key| hash[key] = [] }
        serial_errors = []

        monitoring_stage = Class.new do
          def initialize(inner, guard:, log:, errors:)
            @inner = inner
            @guard = guard
            @log = log
            @errors = errors
          end

          def name
            inner.name
          end

          def render(request, context)
            stage_name = name

            if guard[:active]
              errors << "parallel render detected for #{stage_name}"
              raise "parallel render detected for #{stage_name}"
            end

            guard[:active] = true
            key = request.expression.content.to_s.strip
            log[key] << stage_name

            begin
              inner.render(request, context)
            ensure
              guard[:active] = false
            end
          end

          private

          attr_reader :inner, :guard, :log, :errors
        end

        allow_any_instance_of(Asciidoctor::Latexmath::RendererService).to receive(:build_pipeline) do
          stages = [
            Asciidoctor::Latexmath::Rendering::PdflatexRenderer.new,
            Asciidoctor::Latexmath::Rendering::PdfToSvgRenderer.new,
            Asciidoctor::Latexmath::Rendering::PdfToPngRenderer.new
          ].map { |stage| monitoring_stage.new(stage, guard: guard, log: call_sequences, errors: serial_errors) }

          Asciidoctor::Latexmath::Rendering::Pipeline.new(stages)
        end

        source = <<~ADOC
          :imagesdir: images

          [latexmath]
          ++++
          a + b
          ++++

          [latexmath, format=pdf]
          ++++
          c + d
          ++++

          [latexmath, format=png]
          ++++
          e + f
          ++++
        ADOC

        convert_with_extension(source)

        expect(serial_errors).to be_empty

        expected_order = Asciidoctor::Latexmath::Rendering::Pipeline.default_stage_identifiers.map(&:to_s)
        sanitized_keys = call_sequences.keys.map(&:to_s)
        expect(sanitized_keys).to contain_exactly("a + b", "c + d", "e + f")
        call_sequences.each_value do |sequence|
          expect(sequence).to eq(expected_order)
        end
      end
    end
  end
end
