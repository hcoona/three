# frozen_string_literal: true

require "asciidoctor-latexmath"

RSpec.describe "Accessibility markup" do
  it "adds alt text, role, and data attributes" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~'ADOC'
          [latexmath]
          ++++
          \int_a^b f(x) dx
          ++++
        ADOC

        html = convert_with_extension(source, attributes: {"imagesdir" => "images"})

        expect(html).to include('role="math"')
        expect(html).to include('data-latex-original="\\int_a^b f(x) dx"')
        expect(html).to include('alt="\\int_a^b f(x) dx"')
      end
    end
  end

  it "adds accessibility attributes for stem blocks and inline macros" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~'ADOC'
          :stem: latexmath

          [stem]
          ++++
          \sum_{i=0}^{n} i
          ++++

          Inline stem stem:[a^2 + b^2 = c^2].
        ADOC

        html = convert_with_extension(source, attributes: {"imagesdir" => "assets"})

        expect(html).to include('class="imageblock math"')
        expect(html).to include('data-latex-original="\sum_{i=0}^{n} i"')
        expect(html).to include('src="assets/')
        expect(html).to include('<span class="image math"><img')
        expect(html).to include('data-latex-original="a^2 + b^2 = c^2"')
      end
    end
  end

  it "honors user-provided alt while preserving original latex metadata" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        source = <<~'ADOC'
          [latexmath, alt="Custom description", role="equation"]
          ++++
          \frac{a}{b}
          ++++
        ADOC

        html = convert_with_extension(source, attributes: {"imagesdir" => "images"})

        expect(html).to include('alt="Custom description"')
        expect(html).to include('data-latex-original="\\frac{a}{b}"')
        expect(html).to include('class="imageblock equation math"')
      end
    end
  end
end
