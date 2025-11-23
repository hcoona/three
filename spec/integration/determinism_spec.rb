# frozen_string_literal: true

require "digest"
require "asciidoctor-latexmath"

RSpec.describe "Deterministic rendering" do
  it "produces identical artifacts and skips re-rendering on cache hits" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        stub_tool_availability(dvisvgm: true, pdf2svg: false)
        Asciidoctor::Latexmath.reset_render_counters!

        source = <<~ADOC
          [latexmath]
          ++++
          e^{i\pi} + 1 = 0
          ++++
        ADOC

        html = convert_with_extension(source, attributes: {"imagesdir" => "images"})
        expect(html).to include("images/")
        first_svg = Dir.glob("images/*.svg").fetch(0)
        digest_before = Digest::SHA256.file(first_svg).hexdigest
        expect(Asciidoctor::Latexmath.render_invocations).to eq(1)

        Asciidoctor::Latexmath.reset_render_counters!
        html_again = convert_with_extension(source, attributes: {"imagesdir" => "images"})
        expect(html_again).to include("images/")
        second_svg = Dir.glob("images/*.svg").fetch(0)
        digest_after = Digest::SHA256.file(second_svg).hexdigest

        expect(digest_after).to eq(digest_before)
        expect(Asciidoctor::Latexmath.render_invocations).to eq(0)
      end
    end
  end
end
