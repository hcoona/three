# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Rendering performance smoke" do
  it "renders twenty formulas within a reasonable wall clock budget" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        formulas = Array.new(20) do |idx|
          <<~BLOCK
            [latexmath]
            ++++
            x_{#{idx}}^2 + y_{#{idx}}^2 = z_{#{idx}}^2
            ++++
          BLOCK
        end.join("\n\n")

        source = <<~ADOC
          :latexmath-format: pdf

          #{formulas}
        ADOC

        start = Process.clock_gettime(Process::CLOCK_MONOTONIC)
        convert_with_extension(source, attributes: {"imagesdir" => "images"})
        duration = Process.clock_gettime(Process::CLOCK_MONOTONIC) - start

        expect(duration).to be < 2.0
        expect(Dir.glob("images/*.pdf").size).to eq(20)
      end
    end
  end
end
