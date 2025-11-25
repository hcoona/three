# frozen_string_literal: true

require "asciidoctor-latexmath"
require "objspace"

RSpec.describe "Memory retention" do
  it "does not retain excessive memory after rendering many expressions" do
    skip "ObjectSpace memsize measurement unavailable" unless ObjectSpace.respond_to?(:memsize_of_all)

    within_tmpdir do |dir|
      Dir.chdir(dir) do
        GC.start
        baseline = ObjectSpace.memsize_of_all(String)

        formulas = Array.new(120) do |idx|
          <<~BLOCK
            [latexmath]
            ++++
            x_{#{idx}}^{#{idx}} + y_{#{idx}}
            ++++
          BLOCK
        end.join("\n\n")

        source = <<~ADOC
          :imagesdir: images
          :latexmath-cachedir: cache

          #{formulas}
        ADOC

        convert_with_extension(source)

        GC.start
        after = ObjectSpace.memsize_of_all(String)
        increase = [after - baseline, 0].max

        expect(increase).to be < 16 * 1024 * 1024
      end
    end
  end
end
