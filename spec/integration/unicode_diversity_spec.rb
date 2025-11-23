# frozen_string_literal: true

require "json"
require "digest"
require "asciidoctor-latexmath"

RSpec.describe "Unicode diversity" do
  it "preserves raw code points and reuses cached artifacts on subsequent runs" do
    within_tmpdir do |dir|
      Dir.chdir(dir) do
        stub_tool_availability(dvisvgm: true, pdftoppm: true)
        Asciidoctor::Latexmath.reset_render_counters!

        block_expressions = [
          "\\text{cafe\u0301}",
          "\\text{café}",
          "\\text{漢字と仮名}",
          "\\mathbb{R} \\setminus \\mathbb{Q}",
          "\\text{😀 emoji check}"
        ]
        inline_expression = "\\sum_{i=1}^{n} α_i β_i"
        all_expressions = block_expressions + [inline_expression]

        block_sections = block_expressions.map do |expression|
          <<~BLOCK
            [latexmath]
            ++++
            #{expression}
            ++++
          BLOCK
        end.join("\n\n")

        source = <<~ADOC
          #{block_sections}

          Inline sample latexmath:[#{inline_expression}] ensures inline coverage.
        ADOC

        html = convert_with_extension(source, attributes: {"imagesdir" => "images"})

        expect(Asciidoctor::Latexmath.render_invocations).to eq(all_expressions.size)

        all_expressions.each do |expression|
          expect(html).to include(expression)
        end

        metadata_entries = Dir.glob(".asciidoctor/latexmath/*/metadata.json").map do |path|
          JSON.parse(File.read(path))
        end
        expect(metadata_entries.size).to eq(all_expressions.size)

        content_hashes = metadata_entries.map { |entry| entry.fetch("content_hash") }
        expected_hashes = all_expressions.map { |expression| Digest::SHA256.hexdigest(expression) }
        expect(content_hashes).to match_array(expected_hashes)

        entry_types = metadata_entries.map { |entry| entry.fetch("entry_type") }
        expect(entry_types).to include("block")
        expect(entry_types).to include("inline")
        expect(Digest::SHA256.hexdigest(block_expressions[0])).not_to eq(Digest::SHA256.hexdigest(block_expressions[1]))

        generated_files = Dir.glob("images/*")
        expect(generated_files.size).to be >= all_expressions.size
        generated_files.each do |path|
          expect(File.size(path)).to be_positive
        end

        first_render_count = Asciidoctor::Latexmath.render_invocations
        FileUtils.rm_rf("images")
        html_second = convert_with_extension(source, attributes: {"imagesdir" => "images"})
        expect(Asciidoctor::Latexmath.render_invocations).to eq(first_render_count)

        metadata_second = Dir.glob(".asciidoctor/latexmath/*/metadata.json").map do |path|
          JSON.parse(File.read(path))
        end
        expect(metadata_second.size).to eq(all_expressions.size)
        expect(metadata_second.map { |entry| entry.fetch("content_hash") }).to match_array(expected_hashes)

        all_expressions.each do |expression|
          expect(html_second).to include(expression)
        end

        Asciidoctor::Latexmath.reset_render_counters!
      end
    end
  end
end
