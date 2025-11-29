# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Inline output structure" do
  it "wraps inline math in an image span with accessible metadata" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~ADOC
          Inline latexmath:[a^2 + b^2 = c^2, role=highlight] example.
        ADOC

        html = convert_with_extension(source, attributes: {"imagesdir" => "assets"})

        expect(html).to include('<span class="image math">')
        expect(html).to match(%r{<img\s+[^>]*src="assets/[^"']+\.svg"})
        expect(html).to include("alt=\"a^2 + b^2 = c^2")
        expect(html).to include('role="math"')
        expect(html).to include('data-latex-original="a^2 + b^2 = c^2')
        expect(html).to include("highlight")
        expect(html).not_to include('src="data:')
      end
    end
  end
end
