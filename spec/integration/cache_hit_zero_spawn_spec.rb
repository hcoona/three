# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Cache hit zero spawn" do
  it "does not invoke renderers on cache hit" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          [latexmath]
          ++++
          z
          ++++
        ADOC

        Asciidoctor::Latexmath.reset_render_counters!

        convert_with_extension(source, attributes: {"imagesdir" => "images"})
        first_invocations = Asciidoctor::Latexmath.render_invocations

        convert_with_extension(source, attributes: {"imagesdir" => "images"})
        second_invocations = Asciidoctor::Latexmath.render_invocations

        expect(first_invocations).to be > 0
        expect(second_invocations).to eq(first_invocations)
      end
    end
  end
end
