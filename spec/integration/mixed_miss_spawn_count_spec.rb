# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Mixed miss spawn count" do
  it "only renders newly introduced expressions on the next run" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        Asciidoctor::Latexmath.reset_render_counters!

        source_round_one = <<~ADOC
          :imagesdir: images
          :latexmath-cachedir: cache

          [latexmath]
          ++++
          x_0
          ++++

          [latexmath]
          ++++
          x_1
          ++++

          [latexmath]
          ++++
          x_2
          ++++
        ADOC

        convert_with_extension(source_round_one)
        first_count = Asciidoctor::Latexmath.render_invocations
        expect(first_count).to eq(3)

        source_round_two = <<~ADOC
          :imagesdir: images
          :latexmath-cachedir: cache

          [latexmath]
          ++++
          x_0
          ++++

          [latexmath]
          ++++
          x_1
          ++++

          [latexmath]
          ++++
          x_2
          ++++

          [latexmath]
          ++++
          x_3
          ++++

          [latexmath]
          ++++
          x_4
          ++++
        ADOC

        convert_with_extension(source_round_two)
        second_count = Asciidoctor::Latexmath.render_invocations

        expect(second_count - first_count).to eq(2)

        expect(Dir.glob(File.join("images", "*.svg")).size).to eq(5)
      end
    end
  end
end
